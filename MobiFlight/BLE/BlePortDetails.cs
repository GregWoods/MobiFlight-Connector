using System;

namespace MobiFlight.BLE
{
    /// <summary>
    /// Contains details about a discovered BLE device port.
    /// Similar to PortDetails for serial devices.
    /// </summary>
    public class BlePortDetails
    {
        /// <summary>
        /// The device definition name (e.g., "Simionic G1000 with Audio Panel").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The raw Bluetooth address as a ulong for connection purposes.
        /// </summary>
        public ulong BluetoothAddress { get; set; }

        /// <summary>
        /// The formatted MAC address (e.g., "88:6b:0f:a4:dd:d5").
        /// </summary>
        public string FormattedAddress { get; set; }

        /// <summary>
        /// The device definition loaded from JSON.
        /// </summary>
        public BleDeviceDefinition Definition { get; set; }

        /// <summary>
        /// The ServiceUUID that matched this device.
        /// </summary>
        public Guid ServiceUUID { get; set; }
    }
}
