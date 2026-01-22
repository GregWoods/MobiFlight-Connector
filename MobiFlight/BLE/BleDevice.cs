using InTheHand.Bluetooth;
using System;
using System.Threading.Tasks;

namespace MobiFlight.BLE
{
    /// <summary>
    /// Represents a connected BLE device (e.g., Simionic G1000).
    /// Handles BLE connection, service/characteristic discovery, and notification processing.
    /// </summary>
    public class BleDevice
    {
        public event ButtonEventHandler OnButtonPressed;
        public event EventHandler OnDisconnected;

        public string Name { get; private set; }
        public string Serial { get; private set; }
        public string Address { get; private set; }
        public bool IsConnected { get; private set; }

        private readonly BleDeviceDefinition _definition;
        private BluetoothDevice _bluetoothDevice;
        private RemoteGattServer _gattServer;
        private GattCharacteristic _characteristic;

        public BleDevice(BluetoothDevice bluetoothDevice, BleDeviceDefinition definition)
        {
            _bluetoothDevice = bluetoothDevice ?? throw new ArgumentNullException(nameof(bluetoothDevice));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            Name = definition.Name;
            Address = bluetoothDevice.Id;
            Serial = $"BLESimionic / [{bluetoothDevice.Id}]";
        }

        /// <summary>
        /// Connects to the BLE device and subscribes to characteristic notifications.
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                Log.Instance.log($"[BLE] Connecting to {Name} ({Address})...", LogSeverity.Info);

                // Connect to GATT server
                _gattServer = _bluetoothDevice.Gatt;
                await _gattServer.ConnectAsync();

                if (!_gattServer.IsConnected)
                {
                    throw new Exception("Failed to connect to GATT server");
                }

                // Get the primary service
                var serviceUuid = BluetoothUuid.FromGuid(ParseUuid(_definition.ServiceUUID));
                var service = await _gattServer.GetPrimaryServiceAsync(serviceUuid);

                if (service == null)
                {
                    throw new Exception($"Service {_definition.ServiceUUID} not found");
                }

                // Get the characteristic
                var characteristicUuid = BluetoothUuid.FromGuid(ParseUuid(_definition.CharacteristicUUID));
                _characteristic = await service.GetCharacteristicAsync(characteristicUuid);

                if (_characteristic == null)
                {
                    throw new Exception($"Characteristic {_definition.CharacteristicUUID} not found");
                }

                // Subscribe to notifications
                _characteristic.CharacteristicValueChanged += OnCharacteristicValueChanged;
                await _characteristic.StartNotificationsAsync();

                IsConnected = true;
                Log.Instance.log($"[BLE] Connected to {Name} and subscribed to notifications", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Failed to connect to {Name}: {ex.Message}", LogSeverity.Error);
                IsConnected = false;
                throw;
            }
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
                    _characteristic.CharacteristicValueChanged -= OnCharacteristicValueChanged;
                    // Note: StopNotificationsAsync() may not be available in all versions
                }

                _gattServer?.Disconnect();
                IsConnected = false;
                Log.Instance.log($"[BLE] Disconnected from {Name}", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"[BLE] Error disconnecting from {Name}: {ex.Message}", LogSeverity.Warn);
            }
        }

        /// <summary>
        /// Called when a BLE notification is received from the device.
        /// </summary>
        private void OnCharacteristicValueChanged(object sender, GattCharacteristicValueChangedEventArgs e)
        {
            try
            {
                var data = e.Value;
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
