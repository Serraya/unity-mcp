# Transports And Routing Rules

Load this when work touches stdio, HTTP, WebSocket, legacy Unity connection, instance selection, tool visibility, remote-hosted mode, or multi-user behavior.

## Transport Surfaces

- Stdio starts a Python server process per MCP client and talks to Unity through the legacy TCP bridge.
- HTTP runs a shared Python server and talks to Unity plugin sessions through the WebSocket hub.
- Some features have separate stdio sync and HTTP/WebSocket push paths. Keep their metadata and behavior aligned.

## Instance Routing

- Unity instances are selected by `Name@hash`, hash prefix, or stdio port depending on context.
- `set_active_instance` stores client/session routing state.
- Inline `unity_instance` parameters route a single call and must not silently mutate global session state.
- Remote-hosted mode requires explicit user/session isolation. Do not add shortcuts that bypass user identity.

## Tool Visibility

- Unity tool states and server FastMCP visibility are separate concerns.
- `manage_tools sync` relies on Unity `get_tool_states` in stdio mode.
- HTTP/WebSocket registration receives `register_tools` messages from Unity.
- Notify MCP clients of changed tool lists only after the server-side registration state is updated.

## Diagnostics

- A group listing proves group visibility, not that custom tools are absent.
- For custom tools, verify both metadata registration and callable routing.
- Do not treat a client cache problem as a Unity discovery problem without checking server registration and `tools/list` behavior.
