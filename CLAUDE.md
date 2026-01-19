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

## Testing

- C#: MSTest with Moq, naming `MethodName_ShouldBehavior_WhenCondition`
- Frontend: Playwright E2E in `tests/`, Vitest for unit tests
- Run Playwright: `npx playwright test --project=chromium`

## Before Committing

- C#: Ensure build succeeds with no errors
- Frontend: Run `npm run lint` and `npm run check:i18n`
