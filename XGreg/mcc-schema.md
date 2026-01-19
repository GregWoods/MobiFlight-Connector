# MobiFlight Configuration File Schema (.mcc)

This is an XML configuration file that maps physical hardware controls to flight simulator actions.

## Root Structure

```
MobiflightConnector
├── outputs[]     # Simulator → Hardware (reading sim data, controlling LEDs/displays)
└── inputs[]      # Hardware → Simulator (buttons, encoders sending commands)
```

## Config Item (both inputs and outputs)

| Field | Description |
|-------|-------------|
| `guid` | Unique identifier (UUID) |
| `active` | Whether this config is enabled (true/false) |
| `description` | Human-readable name shown in the UI |
| `settings` | The actual configuration details |

## Settings Block

**Common fields:**
| Field | Description |
|-------|-------------|
| `serial` | Hardware device identifier (e.g., "FSD G1000 MFD/ SN-4F6-017") |
| `name` | User-defined label for the control (see [Device Name Mapping](#device-name-mapping) below) |
| `type` | Control type: `Button`, `Encoder`, `InputMultiplexer`, `Output` |
| `preconditions` | Conditions that must be met for this config to activate |
| `configrefs` | References to other configs |

## Output-Specific (Sim → Hardware)

| Field | Description |
|-------|-------------|
| `source` | Where to read data from (SimConnect variable, e.g., `(L:MFD BTN BACKLIGHT, Number)`) |
| `test` | Test value for previewing |
| `modifiers.transformation` | Math expression to transform the value (`$` = input value) |
| `modifiers.comparison` | If/else logic based on value |
| `modifiers.interpolation` | Map input ranges to output ranges (x→y value pairs) |
| `display` | Target hardware output (pin, brightness, PWM settings) |

## Input-Specific (Hardware → Sim)

**Button:**
| Field | Description |
|-------|-------------|
| `onPress` | Command to execute when pressed |
| `onRelease` | Command to execute when released |

**Encoder (rotary knob):**
| Field | Description |
|-------|-------------|
| `onLeft` / `onRight` | Commands for rotation direction |
| `onLeftFast` / `onRightFast` | Commands for fast rotation (optional) |

**InputMultiplexer:**
| Field | Description |
|-------|-------------|
| `DataPin` | Which pin on the multiplexer (0-15) |
| `onPress` | Command to execute |

## Command Types

Commands use MSFS RPN (Reverse Polish Notation) syntax:
- `(>H:AS1000_MFD_...)` - H-events (cockpit interactions)
- `(>K:AP_...)` - K-events (simulator key events)
- `(>L:...)` - Set local variables
- `(A:...)` - Read aircraft variables
- `(L:...)` - Read local variables
- `(E:...)` - Read environment variables

## Device Name Mapping

The `name` field is a **user-defined label**, not a direct hardware pin number. The mapping between names and physical pins happens through the firmware configuration.

### How It Works

**1. Device Configuration (stored in firmware EEPROM)**

Hardware devices are configured with named pins. For example, an encoder might be:
- Physical pins: GPIO 5, 6
- Name: `"NAV VOL"`

This name is stored in the device's EEPROM and reported to the connector on startup.

**2. Event Matching**

When firmware sends an input event (e.g., encoder rotated), it sends the **name** not the pin number:
```
Firmware → "NAV VOL rotated left"
```

The connector matches this to .mcc configs by comparing:
- `serial` contains the device's serial number
- `name` matches the event's device name

**3. Code Path**

| Step | File | What happens |
|------|------|--------------|
| Parse config | `InputConfigItem.cs:88-91` | `DeviceName = reader["name"]` |
| Receive event | `MobiFlightModule.cs:603` | Reads device name from firmware |
| Match config | `InputEventExecutor.cs:162` | `cfg.DeviceName == e.DeviceId` |

**4. Physical Pin Storage**

The actual GPIO pin numbers are stored in device config classes:
- `Config/Button.cs` - serializes as `DeviceType | Pin | Name | End`
- `Config/Encoder.cs` - stores `pinLeft`, `pinRight` values

So `"NAV VOL"` in your .mcc matches the firmware's configured device name, which internally maps to specific GPIO pins on the microcontroller.
