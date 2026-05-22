# Transports And Routing Knowledge

- Stdio transport runs a Python MCP server process per client and reaches Unity through the legacy TCP bridge.
- HTTP transport runs a shared Python server and reaches Unity through the WebSocket hub.
- WebSocket registration payloads are modeled by `Server/src/transport/models.py`.
- Unity-side WebSocket registration is sent by `MCPForUnity/Editor/Services/Transport/Transports/WebSocketTransportClient.cs`.
- Stdio tool visibility sync asks Unity for `get_tool_states`, implemented in `MCPForUnity/Editor/Resources/Editor/ToolStates.cs`.
- HTTP/WebSocket `register_tools` and stdio `get_tool_states` must carry equivalent tool metadata when behavior is shared.
- `set_active_instance` and inline `unity_instance` are different routing mechanisms: session default vs per-call override.
- Remote-hosted mode adds API-key validation and per-user Unity session isolation.
