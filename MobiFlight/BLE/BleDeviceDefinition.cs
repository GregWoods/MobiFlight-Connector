using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace MobiFlight.BLE
{
    /// <summary>
    /// Represents a BLE device definition loaded from JSON configuration files.
    /// Maps hex input codes to named inputs for buttons and encoders.
    /// </summary>
    public class BleDeviceDefinition : IMigrateable
    {
        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("ServiceUUID")]
        public string ServiceUUID { get; set; }

        [JsonProperty("CharacteristicUUID")]
        public string CharacteristicUUID { get; set; }

        [JsonProperty("Inputs")]
        public List<BleInputDefinition> Inputs { get; set; } = new List<BleInputDefinition>();

        [JsonProperty("Outputs")]
        public List<BleOutputDefinition> Outputs { get; set; } = new List<BleOutputDefinition>();

        // Lookup tables for fast hex code to input mapping
        private Dictionary<string, BleInputDefinition> _hexToInputMap;

        public void Migrate()
        {
            // Build lookup table after loading
            BuildHexLookupTable();
        }

        private void BuildHexLookupTable()
        {
            _hexToInputMap = new Dictionary<string, BleInputDefinition>();

            foreach (var input in Inputs)
            {
                // Map all hex codes for this input
                foreach (var hexCode in input.GetAllHexCodes())
                {
                    if (!string.IsNullOrEmpty(hexCode))
                    {
                        _hexToInputMap[hexCode.ToUpperInvariant()] = input;
                    }
                }
            }
        }

        /// <summary>
        /// Finds the input definition that matches the given hex code.
        /// </summary>
        public BleInputDefinition FindInputByHexCode(string hexCode)
        {
            if (_hexToInputMap == null)
                BuildHexLookupTable();

            return _hexToInputMap.TryGetValue(hexCode.ToUpperInvariant(), out var input) ? input : null;
        }

        /// <summary>
        /// Gets the input event type for a hex code (Press, Release, Increment, Decrement).
        /// </summary>
        public BleInputEventType GetEventTypeForHexCode(string hexCode)
        {
            var input = FindInputByHexCode(hexCode);
            if (input == null) return BleInputEventType.Unknown;

            hexCode = hexCode.ToUpperInvariant();

            if (input.Type == "Button")
            {
                if (input.Press?.ToUpperInvariant() == hexCode) return BleInputEventType.Press;
                if (input.Release?.ToUpperInvariant() == hexCode) return BleInputEventType.Release;
            }
            else if (input.Type == "Encoder")
            {
                if (input.Increment?.ToUpperInvariant() == hexCode) return BleInputEventType.Increment;
                if (input.Decrement?.ToUpperInvariant() == hexCode) return BleInputEventType.Decrement;
            }

            return BleInputEventType.Unknown;
        }
    }

    /// <summary>
    /// Represents a single input (button or encoder) in a BLE device definition.
    /// </summary>
    public class BleInputDefinition
    {
        [JsonProperty("Type")]
        public string Type { get; set; }  // "Button" or "Encoder"

        [JsonProperty("Label")]
        public string Label { get; set; }

        // Button properties
        [JsonProperty("Press")]
        public string Press { get; set; }

        [JsonProperty("Release")]
        public string Release { get; set; }

        // Encoder properties
        [JsonProperty("Increment")]
        public string Increment { get; set; }

        [JsonProperty("Decrement")]
        public string Decrement { get; set; }

        public bool IsButton => Type == "Button";
        public bool IsEncoder => Type == "Encoder";

        /// <summary>
        /// Gets all hex codes associated with this input.
        /// </summary>
        public IEnumerable<string> GetAllHexCodes()
        {
            if (IsButton)
            {
                if (!string.IsNullOrEmpty(Press)) yield return Press;
                if (!string.IsNullOrEmpty(Release)) yield return Release;
            }
            else if (IsEncoder)
            {
                if (!string.IsNullOrEmpty(Increment)) yield return Increment;
                if (!string.IsNullOrEmpty(Decrement)) yield return Decrement;
            }
        }
    }

    /// <summary>
    /// Placeholder for output definitions (LEDs, displays, etc.)
    /// </summary>
    public class BleOutputDefinition
    {
        [JsonProperty("Type")]
        public string Type { get; set; }

        [JsonProperty("Label")]
        public string Label { get; set; }
    }

    /// <summary>
    /// Types of input events from BLE devices.
    /// </summary>
    public enum BleInputEventType
    {
        Unknown,
        Press,
        Release,
        Increment,
        Decrement
    }
}
