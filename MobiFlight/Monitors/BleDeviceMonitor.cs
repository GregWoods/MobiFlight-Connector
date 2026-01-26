using MobiFlight.BLE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace MobiFlight.Monitors
{
    /// <summary>
    /// Monitors for BLE devices by scanning for advertisements matching known ServiceUUIDs.
    /// Fires DeviceAvailable when a matching device is discovered, DeviceUnavailable when
    /// a device hasn't been seen for a timeout period.
    /// </summary>
    public class BleDeviceMonitor
    {
        public event EventHandler<BlePortDetails> DeviceAvailable;
        public event EventHandler<BlePortDetails> DeviceUnavailable;

        /// <summary>
        /// Currently detected BLE devices.
        /// </summary>
        public List<BlePortDetails> DetectedDevices { get; private set; } = new List<BlePortDetails>();

        private Dictionary<Guid, BleDeviceDefinition> _serviceUuidToDefinition = new Dictionary<Guid, BleDeviceDefinition>();
        private BluetoothLEAdvertisementWatcher _watcher;
        private readonly Dictionary<ulong, DateTime> _lastSeenTimes = new Dictionary<ulong, DateTime>();
        private readonly Timer _timeoutCheckTimer = new Timer();
        private const int DeviceTimeoutSeconds = 30;
        private bool _isRunning = false;
        private readonly object _lock = new object();

        public BleDeviceMonitor()
        {
            _timeoutCheckTimer.Interval = 5000; // Check every 5 seconds
            _timeoutCheckTimer.Tick += TimeoutCheckTimer_Tick;
        }

        /// <summary>
        /// Sets the device definitions to scan for.
        /// </summary>
        /// <param name="definitions">Dictionary mapping ServiceUUID to BleDeviceDefinition</param>
        public void SetDefinitions(Dictionary<Guid, BleDeviceDefinition> definitions)
        {
            _serviceUuidToDefinition = definitions ?? new Dictionary<Guid, BleDeviceDefinition>();
        }

        /// <summary>
        /// Starts continuous BLE scanning.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Log.Instance.log("[BLE Monitor] Already running", LogSeverity.Debug);
                return;
            }

            if (_serviceUuidToDefinition.Count == 0)
            {
                Log.Instance.log("[BLE Monitor] No device definitions to scan for", LogSeverity.Warn);
                return;
            }

            try
            {
                _watcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active
                };

                _watcher.Received += Watcher_Received;
                _watcher.Stopped += Watcher_Stopped;

                _watcher.Start();
                _timeoutCheckTimer.Start();
                _isRunning = true;

                Log.Instance.log($"[BLE Monitor] Started scanning for {_serviceUuidToDefinition.Count} ServiceUUID(s)", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE Monitor] Failed to start: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Stops continuous BLE scanning.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _timeoutCheckTimer.Stop();

                if (_watcher != null)
                {
                    _watcher.Stop();
                    _watcher.Received -= Watcher_Received;
                    _watcher.Stopped -= Watcher_Stopped;
                    _watcher = null;
                }

                _isRunning = false;
                Log.Instance.log("[BLE Monitor] Stopped scanning", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE Monitor] Error stopping: {ex.Message}", LogSeverity.Warn);
            }
        }

        private void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                // Check if this advertisement contains a service UUID we're looking for
                var matchingDefinition = FindMatchingDefinition(args.Advertisement);
                if (matchingDefinition == null) return;

                var bluetoothAddress = args.BluetoothAddress;
                var formattedAddress = FormatBluetoothAddress(bluetoothAddress);
                var matchingUuid = GetMatchingServiceUuid(args.Advertisement);

                lock (_lock)
                {
                    // Update last seen time
                    _lastSeenTimes[bluetoothAddress] = DateTime.Now;

                    // Check if this is a new device
                    var existingDevice = DetectedDevices.FirstOrDefault(d => d.BluetoothAddress == bluetoothAddress);
                    if (existingDevice == null)
                    {
                        var deviceName = !string.IsNullOrEmpty(args.Advertisement.LocalName)
                            ? args.Advertisement.LocalName
                            : matchingDefinition.Name;

                        var details = new BlePortDetails
                        {
                            Name = matchingDefinition.Name,
                            BluetoothAddress = bluetoothAddress,
                            FormattedAddress = formattedAddress,
                            Definition = matchingDefinition,
                            ServiceUUID = matchingUuid
                        };

                        DetectedDevices.Add(details);
                        Log.Instance.log($"[BLE Monitor] Device discovered: {details.Name} at {formattedAddress}", LogSeverity.Info);

                        // Fire event on UI thread
                        DeviceAvailable?.Invoke(this, details);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE Monitor] Error processing advertisement: {ex.Message}", LogSeverity.Error);
            }
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Log.Instance.log($"[BLE Monitor] Watcher stopped: {args.Error}", LogSeverity.Debug);

            // If we're still supposed to be running and the watcher stopped unexpectedly, restart it
            if (_isRunning && args.Error != BluetoothError.Success)
            {
                Log.Instance.log("[BLE Monitor] Restarting watcher after error", LogSeverity.Warn);
                try
                {
                    _watcher?.Start();
                }
                catch (Exception ex)
                {
                    Log.Instance.log($"[BLE Monitor] Failed to restart watcher: {ex.Message}", LogSeverity.Error);
                }
            }
        }

        private void TimeoutCheckTimer_Tick(object sender, EventArgs e)
        {
            CheckForTimedOutDevices();
        }

        private void CheckForTimedOutDevices()
        {
            var now = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(DeviceTimeoutSeconds);
            var devicesToRemove = new List<BlePortDetails>();

            lock (_lock)
            {
                foreach (var device in DetectedDevices)
                {
                    if (_lastSeenTimes.TryGetValue(device.BluetoothAddress, out var lastSeen))
                    {
                        if (now - lastSeen > timeout)
                        {
                            devicesToRemove.Add(device);
                        }
                    }
                }

                foreach (var device in devicesToRemove)
                {
                    DetectedDevices.Remove(device);
                    _lastSeenTimes.Remove(device.BluetoothAddress);
                    Log.Instance.log($"[BLE Monitor] Device timed out: {device.Name} at {device.FormattedAddress}", LogSeverity.Info);

                    DeviceUnavailable?.Invoke(this, device);
                }
            }
        }

        /// <summary>
        /// Finds a matching device definition based on advertised service UUIDs.
        /// </summary>
        private BleDeviceDefinition FindMatchingDefinition(BluetoothLEAdvertisement advertisement)
        {
            // Check service UUIDs in the advertisement
            foreach (var serviceUuid in advertisement.ServiceUuids)
            {
                if (_serviceUuidToDefinition.TryGetValue(serviceUuid, out var definition))
                {
                    return definition;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the first matching ServiceUUID from the advertisement.
        /// </summary>
        private Guid GetMatchingServiceUuid(BluetoothLEAdvertisement advertisement)
        {
            foreach (var serviceUuid in advertisement.ServiceUuids)
            {
                if (_serviceUuidToDefinition.ContainsKey(serviceUuid))
                {
                    return serviceUuid;
                }
            }
            return Guid.Empty;
        }

        /// <summary>
        /// Formats a Bluetooth address (ulong) to colon-separated hex string.
        /// </summary>
        private static string FormatBluetoothAddress(ulong address)
        {
            var bytes = BitConverter.GetBytes(address);
            // Bluetooth address is stored in little-endian, we want big-endian display
            return $"{bytes[5]:x2}:{bytes[4]:x2}:{bytes[3]:x2}:{bytes[2]:x2}:{bytes[1]:x2}:{bytes[0]:x2}";
        }
    }
}
