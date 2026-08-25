# Transports And Routing Knowledge

- Stdio transport runs a Python MCP server process per client and reaches Unity through the legacy TCP bridge.
- Stdio health probes are ping-only clients and must not replace the active command client. Project-scoped discovery filters status files before probing so one project's agent cannot touch another project's Editor socket.
- HTTP transport runs a shared Python server and reaches Unity through the WebSocket hub.
- WebSocket registration payloads are modeled by `Server/src/transport/models.py`.
- Unity-side WebSocket registration is sent by `MCPForUnity/Editor/Services/Transport/Transports/WebSocketTransportClient.cs`.
- Stdio tool visibility sync asks Unity for `get_tool_states`, implemented in `MCPForUnity/Editor/Resources/Editor/ToolStates.cs`.
- `StdioPortRegistry` is the authoritative stdio discovery cache used by instance listing, pinning, per-call resolution, and command connection lookup.
- `unity_status` mirrors instance routing and editor-readiness resources for MCP clients that do not expose protocol resource reads.
- HTTP/WebSocket `register_tools` and stdio `get_tool_states` must carry equivalent tool metadata when behavior is shared.
- `set_active_instance` and inline `unity_instance` are different routing mechanisms: session default vs per-call override.
- Remote-hosted mode adds API-key validation and per-user Unity session isolation.
