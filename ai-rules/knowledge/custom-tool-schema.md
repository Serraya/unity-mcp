# Custom Tool Schema Knowledge

- Project-local custom tools are non-built-in `[McpForUnityTool]` classes discovered in a Unity consumer project.
- Direct custom-tool schemas are built from Unity `ToolMetadata.Parameters`, not from C# handler bodies.
- The durable schema path is: `ToolDiscoveryService` -> `ToolStates` or `WebSocketTransportClient` -> Python `ToolDefinitionModel` -> `CustomToolService` -> FastMCP tool signature.
- `HandleCommand(JObject)` tools can expose direct-call schema through a public static `ParametersSchema` property.
- `ParametersSchema` may return `JArray`, `JObject`, `JToken`, or a JSON string.
- The nested `Parameters` class with `[ToolParameter]` properties remains a fallback metadata path.
- Python dynamic signatures for custom tools use keyword-only parameters so optional-before-required Unity metadata order is valid.
- `batch_execute` can call custom tools with known params, but it is not a substitute for discoverable direct-call schemas.
- Schema changes must replace stale global custom tool registrations or trigger a clean MCP client renegotiation.
