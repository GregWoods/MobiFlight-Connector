using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace MobiFlight.BLE
{
    /// <summary>
    /// Represents a connected BLE device (e.g., Simionic G1000).
    /// Handles BLE connection, service/characteristic discovery, and notification processing.
    /// Uses Windows Runtime APIs directly.
    /// </summary>
    public class BleDevice
    {
        public static readonly string SerialPrefix = "BLE-";

        public event ButtonEventHandler OnButtonPressed;
        public event EventHandler OnDisconnected;

        public string Name { get; private set; }
        public string Serial { get; private set; }
        public string Address { get; private set; }
        public bool IsConnected { get; private set; }

        private readonly BleDeviceDefinition _definition;
        private BluetoothLEDevice _bleDevice;
        private GattCharacteristic _characteristic;

        public BleDevice(BluetoothLEDevice bleDevice, BleDeviceDefinition definition)
        {
            _bleDevice = bleDevice ?? throw new ArgumentNullException(nameof(bleDevice));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            Name = definition.Name;
            // Format the address as colon-separated hex
            Address = FormatBluetoothAddress(bleDevice.BluetoothAddress);
            Serial = GenerateSerial(definition.Name, Address);

            // Subscribe to connection status changes
            _bleDevice.ConnectionStatusChanged += BleDevice_ConnectionStatusChanged;
        }

        private void BleDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                IsConnected = false;
                Log.Instance.log($"[BLE] Device {Name} disconnected", LogSeverity.Warn);
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }
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

        /// <summary>
        /// Generates a serial string in the format "DeviceName / BLE-[address]"
        /// </summary>
        public static string GenerateSerial(string deviceName, string address)
        {
            return $"{deviceName} / {SerialPrefix}[{address}]";
        }

        /// <summary>
        /// Extracts the MAC address from a BLE serial string.
        /// Serial format: "DeviceName / BLE-[88:6b:0f:a4:dd:d5]"
        /// Also handles legacy format: "BLESimionic / [88:6b:0f:a4:dd:d5]"
        /// </summary>
        public static string ExtractAddressFromSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return null;

            // Look for address in brackets
            var startBracket = serial.LastIndexOf('[');
            var endBracket = serial.LastIndexOf(']');

            if (startBracket >= 0 && endBracket > startBracket)
            {
                return serial.Substring(startBracket + 1, endBracket - startBracket - 1);
            }

            return null;
        }

        /// <summary>
        /// Checks if a serial string represents a BLE device.
        /// </summary>
        public static bool IsBleSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return false;
            // Check for new format with BLE- prefix or legacy format with BLESimionic
            return serial.Contains(SerialPrefix) || serial.Contains("BLESimionic");
        }

        /// <summary>
        /// Connects to the BLE device and subscribes to characteristic notifications.
        /// </summary>
        public Task ConnectAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                Log.Instance.log($"[BLE] Connecting to {Name} ({Address})...", LogSeverity.Info);

                // Get GATT services
                var serviceUuid = ParseUuid(_definition.ServiceUUID);
                Log.Instance.log($"[BLE] Looking for service: {serviceUuid}", LogSeverity.Debug);

                var servicesOperation = _bleDevice.GetGattServicesForUuidAsync(serviceUuid);
                servicesOperation.Completed = (asyncInfo, asyncStatus) =>
                {
                    try
                    {
                        if (asyncStatus != AsyncStatus.Completed)
                        {
                            tcs.SetException(new Exception($"Failed to get services: {asyncStatus}"));
                            return;
                        }

                        var servicesResult = asyncInfo.GetResults();
                        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                        {
                            tcs.SetException(new Exception($"Service {_definition.ServiceUUID} not found (Status: {servicesResult.Status})"));
                            return;
                        }

                        var service = servicesResult.Services[0];
                        Log.Instance.log($"[BLE] Found service: {service.Uuid}", LogSeverity.Debug);

                        // Get the characteristic
                        var characteristicUuid = ParseUuid(_definition.CharacteristicUUID);
                        Log.Instance.log($"[BLE] Looking for characteristic: {characteristicUuid}", LogSeverity.Debug);

                        var characteristicsOperation = service.GetCharacteristicsForUuidAsync(characteristicUuid);
                        characteristicsOperation.Completed = (charAsyncInfo, charAsyncStatus) =>
                        {
                            try
                            {
                                if (charAsyncStatus != AsyncStatus.Completed)
                                {
                                    tcs.SetException(new Exception($"Failed to get characteristics: {charAsyncStatus}"));
                                    return;
                                }

                                var characteristicsResult = charAsyncInfo.GetResults();
                                if (characteristicsResult.Status != GattCommunicationStatus.Success || characteristicsResult.Characteristics.Count == 0)
                                {
                                    tcs.SetException(new Exception($"Characteristic {_definition.CharacteristicUUID} not found (Status: {characteristicsResult.Status})"));
                                    return;
                                }

                                _characteristic = characteristicsResult.Characteristics[0];
                                Log.Instance.log($"[BLE] Found characteristic: {_characteristic.Uuid}", LogSeverity.Debug);

                                // Subscribe to notifications
                                _characteristic.ValueChanged += Characteristic_ValueChanged;

                                // Enable notifications
                                var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;

                                // Check if characteristic supports indications instead of notifications
                                if (_characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                                {
                                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                                    Log.Instance.log($"[BLE] Using indications for {Name}", LogSeverity.Debug);
                                }
                                else
                                {
                                    Log.Instance.log($"[BLE] Using notifications for {Name}", LogSeverity.Debug);
                                }

                                var writeOperation = _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);
                                writeOperation.Completed = (writeAsyncInfo, writeAsyncStatus) =>
                                {
                                    try
                                    {
                                        if (writeAsyncStatus != AsyncStatus.Completed)
                                        {
                                            tcs.SetException(new Exception($"Failed to enable notifications: {writeAsyncStatus}"));
                                            return;
                                        }

                                        var writeResult = writeAsyncInfo.GetResults();
                                        if (writeResult != GattCommunicationStatus.Success)
                                        {
                                            tcs.SetException(new Exception($"Failed to enable notifications (Status: {writeResult})"));
                                            return;
                                        }

                                        IsConnected = true;
                                        Log.Instance.log($"[BLE] Connected to {Name} and subscribed to notifications", LogSeverity.Info);
                                        tcs.SetResult(true);
                                    }
                                    catch (Exception ex)
                                    {
                                        tcs.SetException(ex);
                                    }
                                };
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Failed to connect to {Name}: {ex.Message}", LogSeverity.Error);
                IsConnected = false;
                tcs.SetException(ex);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Disconnects from the BLE device.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_characteristic != null)
                {
                    _characteristic.ValueChanged -= Characteristic_ValueChanged;
                }

                if (_bleDevice != null)
                {
                    _bleDevice.ConnectionStatusChanged -= BleDevice_ConnectionStatusChanged;
                    _bleDevice.Dispose();
                    _bleDevice = null;
                }

                IsConnected = false;
                Log.Instance.log($"[BLE] Disconnected from {Name}", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Error disconnecting from {Name}: {ex.Message}", LogSeverity.Warn);
            }
        }

        /// <summary>
        /// Called when a BLE notification/indication is received from the device.
        /// </summary>
        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                // Use DataReader to read the IBuffer
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                var data = new byte[args.CharacteristicValue.Length];
                reader.ReadBytes(data);
                if (data == null || data.Length == 0) return;

                // Convert byte(s) to hex string
                var hexCode = BitConverter.ToString(data).Replace("-", "");

                // For single byte values, just use the first byte
                if (data.Length == 1)
                {
                    hexCode = data[0].ToString("X2");
                }

                ProcessHexInput(hexCode);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Error processing notification from {Name}: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Processes a hex input code from the device and raises the appropriate event.
        /// </summary>
        private void ProcessHexInput(string hexCode)
        {
            var input = _definition.FindInputByHexCode(hexCode);
            if (input == null)
            {
                Log.Instance.log($"[BLE] Unknown hex code received from {Name}: {hexCode}", LogSeverity.Debug);
                return;
            }

            var eventType = _definition.GetEventTypeForHexCode(hexCode);

            Log.Instance.log($"[BLE] {Name}: {input.Label} -> {eventType} (0x{hexCode})", LogSeverity.Debug);

            var inputEventArgs = CreateInputEventArgs(input, eventType);
            OnButtonPressed?.Invoke(this, inputEventArgs);
        }

        /// <summary>
        /// Creates an InputEventArgs from the input definition and event type.
        /// </summary>
        private InputEventArgs CreateInputEventArgs(BleInputDefinition input, BleInputEventType eventType)
        {
            var args = new InputEventArgs
            {
                Serial = Serial,
                Name = Name,
                DeviceId = input.Label,
                DeviceLabel = input.Label,
            };

            if (input.IsButton)
            {
                args.Type = DeviceType.Button;
                args.Value = eventType == BleInputEventType.Press
                    ? (int)MobiFlightButton.InputEvent.PRESS
                    : (int)MobiFlightButton.InputEvent.RELEASE;
            }
            else if (input.IsEncoder)
            {
                args.Type = DeviceType.Encoder;
                args.Value = eventType == BleInputEventType.Increment
                    ? (int)MobiFlightEncoder.InputEvent.RIGHT
                    : (int)MobiFlightEncoder.InputEvent.LEFT;
            }

            return args;
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

        public void Stop()
        {
            // Placeholder for pausing input processing if needed
        }

        public void Shutdown()
        {
            Disconnect();
        }
    }
}
