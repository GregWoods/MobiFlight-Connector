using InTheHand.Bluetooth;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MobiFlight.BLE
{
    /// <summary>
    /// Manages BLE device discovery, connection, and input event handling.
    /// Follows the same patterns as MidiBoardManager and JoystickManager.
    /// </summary>
    public class BleDeviceManager
    {
        // Set to true if any errors occurred when loading the definition files.
        public bool LoadingError = false;

        public event EventHandler Connected;
        public event ButtonEventHandler OnButtonPressed;

        private readonly List<BleDevice> Devices = new List<BleDevice>();
        private readonly List<BleDevice> ExcludedDevices = new List<BleDevice>();
        private readonly List<BleDevice> DevicesToBeRemoved = new List<BleDevice>();
        public readonly Dictionary<string, BleDeviceDefinition> Definitions = new Dictionary<string, BleDeviceDefinition>();

        private readonly Timer ProcessTimer = new Timer();
        private int CheckAttachedRemovedCounter = 0;
        private bool _isScanning = false;

        private const string DefinitionsFolder = "BluetoothLEDevices";
        private const string SchemaFileName = "blesimionic.schema.json";

        public BleDeviceManager()
        {
            Load();
            ProcessTimer.Interval = 50;
            ProcessTimer.Tick += ProcessTimer_Tick;
        }

        /// <summary>
        /// Loads BLE device definitions from JSON files.
        /// </summary>
        private void Load()
        {
            if (!Directory.Exists(DefinitionsFolder))
            {
                Log.Instance.log($"[BLE] Definitions folder not found: {DefinitionsFolder}", LogSeverity.Warn);
                return;
            }

            var jsonFiles = Directory.GetFiles(DefinitionsFolder, "*.json", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".schema.json"))
                .ToArray();

            var schemaFilePath = Path.Combine(DefinitionsFolder, SchemaFileName);

            if (!File.Exists(schemaFilePath))
            {
                Log.Instance.log($"[BLE] Schema file not found: {schemaFilePath}", LogSeverity.Warn);
                // Load without schema validation
                foreach (var file in jsonFiles)
                {
                    try
                    {
                        var definition = JsonConvert.DeserializeObject<BleDeviceDefinition>(File.ReadAllText(file));
                        definition.Migrate();
                        Definitions.Add(definition.Name, definition);
                        Log.Instance.log($"[BLE] Loaded device definition: {definition.Name}", LogSeverity.Debug);
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.log($"[BLE] Failed to load {file}: {ex.Message}", LogSeverity.Error);
                        LoadingError = true;
                    }
                }
                return;
            }

            var rawDefinitions = JsonBackedObject.LoadDefinitions<BleDeviceDefinition>(
                jsonFiles,
                schemaFilePath,
                onSuccess: (device, definitionFile) => Log.Instance.log($"[BLE] Loaded device definition: {device.Name}", LogSeverity.Debug),
                onError: () => LoadingError = true
            );

            foreach (var definition in rawDefinitions)
            {
                Definitions.Add(definition.Name, definition);
            }

            Log.Instance.log($"[BLE] Loaded {Definitions.Count} device definition(s)", LogSeverity.Info);
        }

        public bool AreBleDevicesConnected()
        {
            return Devices.Count > 0;
        }

        public void Startup()
        {
            ProcessTimer.Start();
        }

        private void ProcessTimer_Tick(object sender, EventArgs e)
        {
            // Remove disconnected devices
            foreach (var device in DevicesToBeRemoved)
            {
                Devices.Remove(device);
            }
            DevicesToBeRemoved.Clear();

            // Periodic scan for new/removed devices
            UpdateOnAttachedOrRemovedDevices();
        }

        private void UpdateOnAttachedOrRemovedDevices()
        {
            CheckAttachedRemovedCounter++;
            // Check every 5 seconds (100 ticks at 50ms interval)
            if (CheckAttachedRemovedCounter < 100) return;

            CheckAttachedRemovedCounter = 0;

            // TODO: Implement periodic device scan if needed
            // For BLE, we may want to rely on explicit Connect() calls instead
        }

        public void Stop()
        {
            foreach (var device in Devices)
            {
                device.Stop();
            }
        }

        public void Shutdown()
        {
            ProcessTimer.Stop();
            foreach (var device in Devices)
            {
                device.Shutdown();
            }
            Devices.Clear();
            ExcludedDevices.Clear();
        }

        /// <summary>
        /// Synchronous wrapper for ConnectAsync - matches pattern used by JoystickManager/MidiBoardManager.
        /// </summary>
        public void Connect()
        {
            // Fire-and-forget async connection
            // The Connected event will fire when complete
            Task.Run(async () =>
            {
                await ConnectAsync();
                Connected?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// Scans for available BLE devices and connects to known devices.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_isScanning)
            {
                Log.Instance.log("[BLE] Scan already in progress", LogSeverity.Warn);
                return;
            }

            _isScanning = true;
            Log.Instance.log("[BLE] Starting device scan...", LogSeverity.Info);

            try
            {
                // Get excluded devices from settings
                List<string> excludedAddresses = new List<string>();
                try
                {
                    excludedAddresses = JsonConvert.DeserializeObject<List<string>>(
                        Properties.Settings.Default.ExcludedBleDevices ?? "[]") ?? new List<string>();
                }
                catch { /* Use empty list if settings not available */ }

                // Scan for devices that match our known service UUIDs
                foreach (var definition in Definitions.Values)
                {
                    await ScanAndConnectForDefinition(definition, excludedAddresses);
                }

                if (AreBleDevicesConnected())
                {
                    Connected?.Invoke(this, null);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Scan failed: {ex.Message}", LogSeverity.Error);
            }
            finally
            {
                _isScanning = false;
            }
        }

        /// <summary>
        /// Scans for and connects to devices matching a specific definition.
        /// </summary>
        private async Task ScanAndConnectForDefinition(BleDeviceDefinition definition, List<string> excludedAddresses)
        {
            try
            {
                var serviceUuid = ParseUuid(definition.ServiceUUID);

                // Request device with the specific service UUID
                var requestOptions = new RequestDeviceOptions
                {
                    AcceptAllDevices = false,
                    Filters = { new BluetoothLEScanFilter { Services = { serviceUuid } } }
                };

                Log.Instance.log($"[BLE] Scanning for {definition.Name} (Service: {definition.ServiceUUID})...", LogSeverity.Debug);

                // Note: RequestDeviceAsync may show a system picker dialog
                // For background scanning without UI, you may need to use different approach
                var bluetoothDevice = await Bluetooth.RequestDeviceAsync(requestOptions);

                if (bluetoothDevice == null)
                {
                    Log.Instance.log($"[BLE] No device found for {definition.Name}", LogSeverity.Debug);
                    return;
                }

                // Check exclusion list
                if (excludedAddresses.Contains(bluetoothDevice.Id))
                {
                    Log.Instance.log($"[BLE] Device {bluetoothDevice.Id} is excluded", LogSeverity.Info);
                    return;
                }

                // Check if already connected
                if (Devices.Any(d => d.Address == bluetoothDevice.Id))
                {
                    Log.Instance.log($"[BLE] Device {bluetoothDevice.Id} already connected", LogSeverity.Debug);
                    return;
                }

                await ConnectDevice(bluetoothDevice, definition);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Error scanning for {definition.Name}: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Connects to a specific BLE device by address.
        /// </summary>
        public Task ConnectDeviceByAddressAsync(string address, string definitionName)
        {
            if (!Definitions.TryGetValue(definitionName, out var definition))
            {
                Log.Instance.log($"[BLE] Unknown device definition: {definitionName}", LogSeverity.Error);
                return Task.CompletedTask;
            }

            // For direct connection by address, we need to use a different approach
            // This is a simplified version - actual implementation may vary by platform
            Log.Instance.log($"[BLE] Attempting direct connection to {address}...", LogSeverity.Info);

            // Note: Direct connection by address requires different API calls
            // depending on the InTheHand.BluetoothLE version and platform
            // This may need platform-specific implementation

            Log.Instance.log($"[BLE] Direct connection by address not yet implemented. Use ConnectAsync() for device picker.", LogSeverity.Warn);
            return Task.CompletedTask;
        }

        private async Task ConnectDevice(BluetoothDevice bluetoothDevice, BleDeviceDefinition definition)
        {
            try
            {
                var device = new BleDevice(bluetoothDevice, definition);
                device.OnButtonPressed += Device_OnButtonPressed;
                device.OnDisconnected += Device_OnDisconnected;

                await device.ConnectAsync();

                Devices.Add(device);
                Log.Instance.log($"[BLE] Added device: {device.Name} ({device.Address})", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Failed to connect device: {ex.Message}", LogSeverity.Error);
            }
        }

        private void Device_OnButtonPressed(object sender, InputEventArgs e)
        {
            OnButtonPressed?.Invoke(sender, e);
        }

        private void Device_OnDisconnected(object sender, EventArgs e)
        {
            var device = sender as BleDevice;
            Log.Instance.log($"[BLE] Device disconnected: {device?.Name}", LogSeverity.Warn);
            if (device != null)
            {
                DevicesToBeRemoved.Add(device);
            }
        }

        public List<BleDevice> GetDevices()
        {
            return Devices;
        }

        public List<BleDevice> GetExcludedDevices()
        {
            return ExcludedDevices;
        }

        public BleDevice GetDeviceBySerial(string serial)
        {
            return Devices.Find(d => d.Serial == serial);
        }

        public string MapDeviceNameToLabel(string deviceName, string inputName)
        {
            // Find the definition that matches
            foreach (var def in Definitions.Values)
            {
                var input = def.Inputs.FirstOrDefault(i => i.Label == inputName);
                if (input != null)
                {
                    return input.Label;
                }
            }
            return inputName;
        }

        public Dictionary<string, int> GetStatistics()
        {
            var result = new Dictionary<string, int>
            {
                ["BleDevices.Count"] = Devices.Count
            };

            foreach (var device in Devices)
            {
                string key = "BleDevice.Model." + device.Name;
                if (!result.ContainsKey(key))
                    result[key] = 0;
                result[key] += 1;
            }

            return result;
        }

        /// <summary>
        /// Parses a UUID string (handles both short 16-bit and full 128-bit formats).
        /// </summary>
        private static BluetoothUuid ParseUuid(string uuid)
        {
            // Handle short UUIDs like "0x044F" or "044F"
            if (uuid.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uuid = uuid.Substring(2);
            }

            if (uuid.Length <= 8)
            {
                // Convert short UUID to full Bluetooth Base UUID
                var shortUuid = ushort.Parse(uuid, System.Globalization.NumberStyles.HexNumber);
                return BluetoothUuid.FromShortId(shortUuid);
            }

            // Full UUID
            return BluetoothUuid.FromGuid(Guid.Parse(uuid));
        }
    }
}
