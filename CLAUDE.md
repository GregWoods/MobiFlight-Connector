# MobiFlight Connector

MobiFlight is a Windows application that connects custom-built hardware (buttons, switches, LEDs, displays) to flight simulators (MSFS, X-Plane, P3D, FSX).

## Project Structure

- **Root** - C# WinForms application (.NET Framework 4.8)
- **frontend/** - React 19+ TypeScript web UI (Vite, Tailwind, shadcn/ui)
- **Scripts/** - Python scripts for CDU/hardware integration
- **tests/** - Playwright E2E tests

## Key Patterns

- `ExecutionManager` - Central coordination for application state
- `MessageExchange.Instance` - Publishes messages to the frontend
- `Log.Instance.log()` - Logging with `LogSeverity` levels
- `Properties.Settings.Default` - Persistent configuration

## BLE (Bluetooth Low Energy) Support

BLE device support follows the same patterns as Joysticks and MIDI boards.

### Key Files
- `MobiFlight/BLE/BleDeviceManager.cs` - Manages discovery, connection, and input events
- `MobiFlight/BLE/BleDevice.cs` - Individual device connection and notification handling
- `MobiFlight/BLE/BleDeviceDefinition.cs` - JSON-based device definitions
- `BluetoothLEDevices/*.json` - Device definition files (e.g., SimionicG1000.json)

### Windows Runtime APIs
Uses Windows Runtime (WinRT) APIs directly from `Windows.Devices.Bluetooth` namespace:
- `BluetoothLEAdvertisementWatcher` - Scans for BLE devices
- `BluetoothLEDevice.FromBluetoothAddressAsync()` - Connects by MAC address (ulong)
- `GattDeviceService` / `GattCharacteristic` - GATT service/characteristic access

**Important**: Uses callback-based async pattern (`operation.Completed = ...`) instead of `async/await` with `.AsTask()` due to .NET Framework 4.8 WinRT interop limitations.

### Connection Flow
1. User clicks Play → `ExecutionManager.Start()` calls `ConnectBleDevicesFromConfig()`
2. Config items with BLE ModuleSerial values are identified (e.g., `"BLESimionic / [88:6b:0f:a4:dd:d5]"`)
3. MAC addresses are extracted and normalized (lowercase, no separators)
4. `BluetoothLEAdvertisementWatcher` scans for advertising devices
5. When a device matches a target address, `BluetoothLEDevice.FromBluetoothAddressAsync()` connects
6. GATT service/characteristic discovery and notification subscription

### Serial Format
- Prefix: `BLE-` (defined in `BleDevice.SerialPrefix`)
- Full format: `DeviceName / BLE-[MAC_ADDRESS]`
- Legacy format: `BLESimionic / [MAC_ADDRESS]`

### Adding New BLE Devices
1. Create a JSON definition in `BluetoothLEDevices/` with ServiceUUID, CharacteristicUUID, and input mappings
2. The definition maps hex notification codes to input labels (buttons/encoders)

### NuGet Dependencies
- `Microsoft.Windows.SDK.Contracts` - Windows Runtime API access for .NET Framework

## Testing

- C#: MSTest with Moq, naming `MethodName_ShouldBehavior_WhenCondition`
- Frontend: Playwright E2E in `tests/`, Vitest for unit tests
- Run Playwright: `npx playwright test --project=chromium`

## Before Committing

- C#: Ensure build succeeds with no errors
- Frontend: Run `npm run lint` and `npm run check:i18n`
