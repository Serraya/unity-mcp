# Custom Tool Schema Rules

Load this when work touches `[McpForUnityTool]` discovery, custom tool metadata, project-local tools, `ToolDefinitionModel`, `CustomToolService`, or FastMCP direct-call schemas.

## Invariant

Project-local custom tools should be discoverable as direct MCP tools with useful parameter schemas. `batch_execute` is a compatibility path, not the discoverability contract.

## Schema Ownership

- Unity discovery owns the initial custom-tool metadata.
- `ToolDiscoveryService` must emit name, description, group, polling flags, built-in status, and parameter metadata.
- `ToolStates` carries that metadata for stdio sync.
- `WebSocketTransportClient` carries that metadata for HTTP/WebSocket `register_tools`.
- Python `ToolDefinitionModel` preserves the metadata.
- `CustomToolService` turns metadata into FastMCP direct-call signatures.
- Dynamic custom-tool parameters must be keyword-only so Unity metadata ordering cannot make optional parameters precede required positional parameters.

## ParametersSchema Convention

For `HandleCommand(JObject)` tools, prefer a public static `ParametersSchema` property on the tool class.

Accepted return types:

- `JArray`
- `JObject`
- `JToken`
- JSON string

Accepted shapes:

- an array of parameter definitions;
- an object with a `parameters` array;
- a JSON-schema-style object with `properties` and optional root `required`.

Supported metadata:

- `name`
- `description`
- `type`
- `required`
- `default` / `default_value`
- `enum` / `enum_values`
- `aliases`
- `nullable`
- `items.type` / `items_type`

Keep the nested `Parameters` class path as fallback compatibility, but do not use it as the primary convention for new JObject-dispatch tools when enum/action schemas matter.

## Verification

- Verify schema visibility at the FastMCP/client-facing layer, not only C# metadata extraction.
- For changed schemas, verify stale global custom tool registrations are replaced or the client is forced to renegotiate.
- Include a regression test where an optional parameter appears before a required parameter in Unity metadata.
- Existing `batch_execute` calls must keep working after direct schema improvements.
