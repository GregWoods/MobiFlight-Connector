# MobiFlight Connector

MobiFlight is a Windows application that connects custom-built hardware (buttons, switches, LEDs, displays) to flight simulators (MSFS, X-Plane, P3D, FSX).

## Project Structure

- **Root** - C# WinForms application (.NET Framework 4.8)
- **frontend/** - React 19+ TypeScript web UI (Vite, Tailwind, shadcn/ui)
- **Scripts/** - Python scripts for CDU/hardware integration
- **tests/** - Playwright E2E tests

## Building

This is a .NET Framework 4.8 project using the old-style csproj format.

- **Use MSBuild**, not `dotnet build`:
  ```cmd
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" MobiFlightConnector.csproj -p:Configuration=Debug -t:Build
  ```
- New source files must be explicitly added to `MobiFlightConnector.csproj` under `<Compile Include="..." />`
- **Line endings**: Always use CRLF (`\r\n`) since this is a Windows application

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
- `MobiFlight/BLE/BlePortDetails.cs` - Data class for discovered device information
- `MobiFlight/Monitors/BleDeviceMonitor.cs` - Continuous ServiceUUID-based scanning
- `BluetoothLEDevices/*.json` - Device definition files

### Windows Runtime APIs
Uses Windows Runtime (WinRT) APIs directly from `Windows.Devices.Bluetooth` namespace:
- `BluetoothLEAdvertisementWatcher` - Scans for BLE devices
- `BluetoothLEDevice.FromBluetoothAddressAsync()` - Connects by MAC address (ulong)
- `GattDeviceService` / `GattCharacteristic` - GATT service/characteristic access

**Important**: Uses callback-based async pattern (`operation.Completed = ...`) instead of `async/await` with `.AsTask()` due to .NET Framework 4.8 WinRT interop limitations.

### Discovery & Connection Flow
1. Project loads → `ExecutionManager.OnProjectChanged` triggers `StartBleDeviceScanning()`
2. `BleDeviceMonitor` starts continuous scanning via `BluetoothLEAdvertisementWatcher`
3. Advertisements are filtered by ServiceUUIDs from loaded JSON definitions
4. When a device matches a known ServiceUUID, `DeviceAvailable` event fires
5. `BleDeviceManager` auto-connects using `BluetoothLEDevice.FromBluetoothAddressAsync()`
6. GATT service/characteristic discovery and notification subscription
7. Devices that stop advertising for 30 seconds trigger `DeviceUnavailable` and are disconnected

### Serial Format
- Prefix: `BLE-` (defined in `BleDevice.SerialPrefix`)
- Full format: `DeviceName / BLE-[MAC_ADDRESS]`

### Adding New BLE Devices
1. Create a JSON definition in `BluetoothLEDevices/` with ServiceUUID, CharacteristicUUID, and input mappings
2. The definition maps hex notification codes to input labels (buttons/encoders)
3. Devices advertising the ServiceUUID will be auto-discovered when a project loads

### NuGet Dependencies
- `Microsoft.Windows.SDK.Contracts` - Windows Runtime API access for .NET Framework

## Testing

- C#: MSTest with Moq, naming `MethodName_ShouldBehavior_WhenCondition`
- Frontend: Playwright E2E in `tests/`, Vitest for unit tests
- Run Playwright: `npx playwright test --project=chromium`

## Before Committing

- C#: Ensure build succeeds with no errors (use MSBuild command above)
- Frontend: Run `npm run lint` and `npm run check:i18n`
