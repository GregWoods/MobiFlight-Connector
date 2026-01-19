# Python CDU Integration Scripts

Python scripts for CDU (Control Display Unit) integration with flight simulators (X-Plane, MSFS). These handle real-time websocket communication between simulator datarefs and WinWing hardware.

## Available Libraries

- `websockets` (>=14.0) - WebSocket client/server
- `asyncio` - Async programming
- `json`, `logging` - Standard library
- `SimConnect` (>=0.4) - MSFS integration (optional)
- `gql` (>=3.5) - GraphQL-based simulators (optional)

## Code Style

- Follow PEP 8, line length up to ~120 characters
- 4 spaces indentation
- Constants: `UPPER_SNAKE_CASE` at module level
- Type hints on functions
- Import order: stdlib, third-party, local

## Module Structure

```python
"""
Adds support for [Aircraft Name] in [Simulator]

[Brief description]

Two tasks started per CDU device:
1. handle_dataref_updates -> Listens to simulator
2. handle_device_update   -> Dispatches to MobiFlight
"""

import asyncio
import logging
import websockets

# Constants
WS_CAPTAIN = "..."
CHAR_MAP = {"$": "☐", "`": "°"}

async def main():
    available_devices = await get_available_devices()
    tasks = []
    for device in available_devices:
        queue = asyncio.Queue()
        tasks.append(asyncio.create_task(handle_dataref_updates(queue, device)))
        tasks.append(asyncio.create_task(handle_device_update(queue, device)))
    await asyncio.gather(*tasks)

if __name__ == "__main__":
    asyncio.run(main())
```

## Enums

Use `StrEnum` and `IntEnum` for device types and constants:

```python
from enum import StrEnum, IntEnum

class CduDevice(StrEnum):
    Captain = "cdu_0"
    CoPilot = "cdu_1"
```

## Logging

```python
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
```

- `info` - Connection status, state changes
- `warning` - Recoverable issues
- `error` - Errors affecting functionality
- `debug` - Detailed diagnostics

## WebSocket Pattern

```python
async def handle_device_update(queue: asyncio.Queue, device: CduDevice):
    async for websocket in websockets.connect(endpoint):
        while True:
            values = await queue.get()
            try:
                await websocket.send(generate_display_json(values))
            except websockets.exceptions.ConnectionClosed:
                logging.error("Connection closed, reconnecting...")
                await queue.put(values)  # Re-queue failed message
                break
```

## Error Handling

- Handle websocket disconnections with automatic reconnection
- Re-queue failed messages
- Use try/except around external API calls
- Log errors with context for debugging
