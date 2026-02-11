using MobiFlight.Base;
using MobiFlight.Monitors;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Foundation;

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
        public event EventHandler ControllerConnected;
        public event EventHandler ControllerDisconnected;
        public event ButtonEventHandler OnButtonPressed;

        private readonly List<BleDevice> Devices = new List<BleDevice>();
        private readonly List<BleDevice> ExcludedDevices = new List<BleDevice>();
        private readonly List<BleDevice> DevicesToBeRemoved = new List<BleDevice>();
        public readonly Dictionary<string, BleDeviceDefinition> Definitions = new Dictionary<string, BleDeviceDefinition>();

        /// <summary>
        /// Maps ServiceUUID to device definition for quick lookup during scanning.
        /// </summary>
        private Dictionary<Guid, BleDeviceDefinition> _serviceUuidToDefinition = new Dictionary<Guid, BleDeviceDefinition>();

        /// <summary>
        /// Monitor for continuous BLE device scanning.
        /// </summary>
        private readonly BleDeviceMonitor _deviceMonitor = new BleDeviceMonitor();

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

            // Subscribe to device monitor events
            _deviceMonitor.DeviceAvailable += OnMonitorDeviceAvailable;
            _deviceMonitor.DeviceUnavailable += OnMonitorDeviceUnavailable;
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
                return;
            }

            var rawDefinitions = JsonBackedObject.LoadDefinitions<BleDeviceDefinition>(
                jsonFiles,
                schemaFilePath,
                onSuccess: (device, definitionFile) => Log.Instance.log($"[BLE] Loaded device definition: {device.Name}", LogSeverity.Info),
                onError: () => LoadingError = true
            );

            foreach (var definition in rawDefinitions)
            {
                Definitions.Add(definition.Name, definition);
            }

            // Build ServiceUUID lookup for continuous scanning
            _serviceUuidToDefinition = Definitions.Values
                .Where(d => !string.IsNullOrEmpty(d.ServiceUUID))
                .ToDictionary(
                    d => ParseUuid(d.ServiceUUID),
                    d => d
                );

            Log.Instance.log($"[BLE] Loaded {Definitions.Count} device definition(s) with {_serviceUuidToDefinition.Count} ServiceUUID(s)", LogSeverity.Info);
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
            // Stop continuous scanning
            StopContinuousScanning();

            ProcessTimer.Stop();
            foreach (var device in Devices)
            {
                device.Shutdown();
            }
            Devices.Clear();
            ExcludedDevices.Clear();
        }

        /// <summary>
        /// Placeholder for Connect - actual connection is done via ConnectFromConfigItemsAsync
        /// which is called by ExecutionManager when a project is loaded.
        /// </summary>
        public void Connect()
        {
            // Connection is now handled by StartContinuousScanning which is called
            // from ExecutionManager when a project is loaded.
            // This method is kept for API compatibility but does nothing on its own.
            Log.Instance.log("[BLE] Connect() called - use StartContinuousScanning for continuous device discovery", LogSeverity.Debug);
        }

        /// <summary>
        /// Starts continuous BLE scanning based on ServiceUUIDs from loaded definitions.
        /// Devices matching known ServiceUUIDs will be auto-connected.
        /// </summary>
        public void StartContinuousScanning()
        {
            if (_serviceUuidToDefinition.Count == 0)
            {
                Log.Instance.log("[BLE] No device definitions loaded, skipping continuous scan", LogSeverity.Warn);
                return;
            }

            _deviceMonitor.SetDefinitions(_serviceUuidToDefinition);
            _deviceMonitor.Start();
            Log.Instance.log("[BLE] Started continuous device scanning", LogSeverity.Info);
        }

        /// <summary>
        /// Stops continuous BLE scanning.
        /// </summary>
        public void StopContinuousScanning()
        {
            _deviceMonitor.Stop();
            Log.Instance.log("[BLE] Stopped continuous device scanning", LogSeverity.Info);
        }

        /// <summary>
        /// Called when the device monitor discovers a new BLE device.
        /// </summary>
        private async void OnMonitorDeviceAvailable(object sender, BlePortDetails details)
        {
            try
            {
                // Check if we already have this device connected
                var existingDevice = Devices.FirstOrDefault(d =>
                    d.Address.Equals(details.FormattedAddress, StringComparison.OrdinalIgnoreCase));

                if (existingDevice != null)
                {
                    Log.Instance.log($"[BLE] Device already connected: {details.Name} at {details.FormattedAddress}", LogSeverity.Debug);
                    return;
                }

                Log.Instance.log($"[BLE] Auto-connecting to discovered device: {details.Name} at {details.FormattedAddress}", LogSeverity.Info);
                await ConnectDeviceByBluetoothAddress(details.BluetoothAddress, details.Definition);

                if (AreBleDevicesConnected())
                {
                    Connected?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Error auto-connecting device: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Called when the device monitor detects a device is no longer available (stopped advertising).
        /// Note: Connected BLE devices often stop advertising, so we only disconnect if not connected.
        /// </summary>
        private void OnMonitorDeviceUnavailable(object sender, BlePortDetails details)
        {
            try
            {
                var device = Devices.FirstOrDefault(d =>
                    d.Address.Equals(details.FormattedAddress, StringComparison.OrdinalIgnoreCase));

                if (device != null)
                {
                    // Don't disconnect devices that are still connected - BLE devices often stop
                    // advertising once a GATT connection is established
                    if (device.IsConnected)
                    {
                        Log.Instance.log($"[BLE] Device stopped advertising but still connected: {details.Name} at {details.FormattedAddress}", LogSeverity.Debug);
                        return;
                    }

                    Log.Instance.log($"[BLE] Device lost (timeout): {details.Name} at {details.FormattedAddress}", LogSeverity.Info);
                    device.Shutdown();
                    DevicesToBeRemoved.Add(device);
                    ControllerDisconnected?.Invoke(this, null);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Error handling device unavailable: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Connects to all BLE devices referenced in the given config items.
        /// Scans for devices and connects to those matching the target addresses.
        /// </summary>
        /// <param name="configItems">Config items that may reference BLE devices</param>
        public async Task ConnectFromConfigItemsAsync(IEnumerable<IConfigItem> configItems)
        {
            if (configItems == null) return;

            // Extract unique BLE device references
            var bleReferences = configItems
                .Where(item => item?.Controller != null && BleDevice.IsBleSerial(item.Controller.Serial))
                .Select(item => new
                {
                    Address = BleDevice.ExtractAddressFromSerial(item.Controller.Serial)?.ToLowerInvariant(),
                    DefinitionName = ExtractDefinitionNameFromSerial(item.Controller.Name)
                })
                .Where(x => !string.IsNullOrEmpty(x.Address) && !string.IsNullOrEmpty(x.DefinitionName))
                .GroupBy(x => x.Address)
                .Select(g => g.First())
                .ToList();

            if (bleReferences.Count == 0)
            {
                Log.Instance.log("[BLE] No BLE device references found in config items", LogSeverity.Debug);
                return;
            }

            Log.Instance.log($"[BLE] Found {bleReferences.Count} unique BLE device(s) in config", LogSeverity.Info);

            // Build a lookup of addresses we're looking for
            var targetAddresses = bleReferences.ToDictionary(
                r => NormalizeAddress(r.Address),
                r => r.DefinitionName);

            Log.Instance.log($"[BLE] Target addresses: {string.Join(", ", targetAddresses.Keys)}", LogSeverity.Debug);

            // Scan for devices matching our service UUIDs
            await ScanAndConnectToTargetDevices(targetAddresses);

            if (AreBleDevicesConnected())
            {
                Connected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Normalizes a MAC address to lowercase without separators for comparison.
        /// </summary>
        private string NormalizeAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return "";
            return address.Replace(":", "").Replace("-", "").Replace("[", "").Replace("]", "").ToLowerInvariant();
        }

        /// <summary>
        /// Scans for BLE devices using Windows Runtime BluetoothLEAdvertisementWatcher
        /// and connects to those matching target addresses.
        /// </summary>
        private async Task ScanAndConnectToTargetDevices(Dictionary<string, string> targetAddresses)
        {
            if (_isScanning)
            {
                Log.Instance.log("[BLE] Scan already in progress", LogSeverity.Warn);
                return;
            }

            _isScanning = true;
            var foundAddresses = new HashSet<string>();
            var scanCompletionSource = new TaskCompletionSource<bool>();
            BluetoothLEAdvertisementWatcher watcher = null;

            try
            {
                Log.Instance.log("[BLE] Starting Windows Runtime advertisement scan...", LogSeverity.Info);

                // Create Windows Runtime advertisement watcher
                watcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active
                };

                // Subscribe to advertisement events
                watcher.Received += async (w, args) =>
                {
                    try
                    {
                        // Convert BluetoothAddress (ulong) to normalized hex string
                        var deviceAddress = args.BluetoothAddress.ToString("x12");
                        var formattedAddress = FormatMacAddress(deviceAddress);

                        // Avoid processing the same device multiple times
                        if (foundAddresses.Contains(deviceAddress))
                            return;

                        var deviceName = args.Advertisement.LocalName ?? "Unknown";
                        Log.Instance.log($"[BLE] Advertisement from: {deviceName} at {formattedAddress} (normalized: {deviceAddress})", LogSeverity.Debug);

                        // Check if this device matches one of our targets
                        if (targetAddresses.TryGetValue(deviceAddress, out var definitionName))
                        {
                            foundAddresses.Add(deviceAddress);
                            Log.Instance.log($"[BLE] Device {formattedAddress} matches target address, connecting...", LogSeverity.Info);

                            if (Definitions.TryGetValue(definitionName, out var matchedDefinition))
                            {
                                // Connect using the BluetoothAddress
                                await ConnectDeviceByBluetoothAddress(args.BluetoothAddress, matchedDefinition);
                            }

                            // If we've found all target devices, we can stop
                            if (foundAddresses.Count >= targetAddresses.Count)
                            {
                                scanCompletionSource.TrySetResult(true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.log($"[BLE] Error processing advertisement: {ex.Message}", LogSeverity.Error);
                    }
                };

                watcher.Stopped += (w, args) =>
                {
                    Log.Instance.log($"[BLE] Watcher stopped: {args.Error}", LogSeverity.Debug);
                };

                // Start scanning
                watcher.Start();
                Log.Instance.log("[BLE] Advertisement watcher started", LogSeverity.Debug);

                // Wait for scan to complete or timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
                var completedTask = await Task.WhenAny(scanCompletionSource.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Log.Instance.log($"[BLE] Scan timed out. Found {foundAddresses.Count} of {targetAddresses.Count} target device(s)", LogSeverity.Info);
                }
                else
                {
                    Log.Instance.log($"[BLE] Scan complete. Found all {targetAddresses.Count} target device(s)", LogSeverity.Info);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Scan failed: {ex.Message}", LogSeverity.Error);
            }
            finally
            {
                // Stop the watcher
                if (watcher != null)
                {
                    watcher.Stop();
                    Log.Instance.log("[BLE] Advertisement watcher stopped", LogSeverity.Debug);
                }
                _isScanning = false;
            }
        }

        /// <summary>
        /// Formats a normalized MAC address (12 hex chars) to colon-separated format.
        /// </summary>
        private string FormatMacAddress(string normalizedAddress)
        {
            if (string.IsNullOrEmpty(normalizedAddress) || normalizedAddress.Length != 12)
                return normalizedAddress;

            return string.Join(":", Enumerable.Range(0, 6).Select(i => normalizedAddress.Substring(i * 2, 2)));
        }

        /// <summary>
        /// Connects to a BLE device using its Bluetooth address (ulong).
        /// </summary>
        private Task ConnectDeviceByBluetoothAddress(ulong bluetoothAddress, BleDeviceDefinition definition)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                Log.Instance.log($"[BLE] Connecting to device at address {bluetoothAddress:X12}...", LogSeverity.Info);

                // Use Windows Runtime API to get the device
                var operation = BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                operation.Completed = async (asyncInfo, asyncStatus) =>
                {
                    try
                    {
                        if (asyncStatus != Windows.Foundation.AsyncStatus.Completed)
                        {
                            Log.Instance.log($"[BLE] Failed to get device: {asyncStatus}", LogSeverity.Error);
                            tcs.SetResult(false);
                            return;
                        }

                        var bleDevice = asyncInfo.GetResults();
                        if (bleDevice == null)
                        {
                            Log.Instance.log($"[BLE] Failed to get BluetoothLEDevice from address {bluetoothAddress:X12}", LogSeverity.Error);
                            tcs.SetResult(false);
                            return;
                        }

                        Log.Instance.log($"[BLE] Got BluetoothLEDevice: {bleDevice.Name}", LogSeverity.Debug);

                        // Create our BleDevice wrapper
                        var device = new BleDevice(bleDevice, definition);
                        device.OnButtonPressed += Device_OnButtonPressed;
                        device.OnDisconnected += Device_OnDisconnected;

                        await device.ConnectAsync();

                        Devices.Add(device);
                        Log.Instance.log($"[BLE] Added device: {device.Name} ({device.Address})", LogSeverity.Info);
                        ControllerConnected?.Invoke(this, null);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.log($"[BLE] Failed to connect to device: {ex.Message}", LogSeverity.Error);
                        tcs.SetResult(false);
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Failed to connect to device: {ex.Message}", LogSeverity.Error);
                tcs.SetResult(false);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Finds the definition name that matches a controller name.
        /// </summary>
        private string ExtractDefinitionNameFromSerial(string controllerName)
        {
            if (string.IsNullOrEmpty(controllerName)) return null;

            var deviceName = controllerName;

            // Try to find an exact match
            if (Definitions.ContainsKey(deviceName))
            {
                return deviceName;
            }

            // Try to find a partial match
            var matchingDef = Definitions.Values.FirstOrDefault(d =>
                d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase) ||
                d.Name.IndexOf(deviceName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                deviceName.IndexOf(d.Name, StringComparison.OrdinalIgnoreCase) >= 0);

            return matchingDef?.Name;
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
                ControllerDisconnected?.Invoke(this, null);
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
        private static Guid ParseUuid(string uuid)
        {
            // Handle short UUIDs like "0x044F" or "044F"
            if (uuid.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uuid = uuid.Substring(2);
            }

            if (uuid.Length <= 8)
            {
                // Convert short UUID to full Bluetooth Base UUID
                // Base UUID: 00000000-0000-1000-8000-00805F9B34FB
                var shortUuid = uint.Parse(uuid, System.Globalization.NumberStyles.HexNumber);
                return new Guid($"{shortUuid:X8}-0000-1000-8000-00805F9B34FB");
            }

            // Full UUID
            return Guid.Parse(uuid);
        }
    }
}
