# Unity Editor Package Rules

Load this when work touches `MCPForUnity/`, C# tools/resources, Editor UI, package assets, setup/configuration, or Unity compatibility.

## Tool And Resource Handlers

- C# tools are classes marked with `[McpForUnityTool]`.
- C# resources are classes marked with `[McpForUnityResource]`.
- `CommandRegistry` is the execution registry for both and expects a public static `HandleCommand(JObject)` method, sync or async.
- `ToolDiscoveryService` owns metadata discovery for user-facing tool registration.
- Do not infer user-facing schema from handler internals alone; discovery metadata must carry it to the Python server.

## Parameter Handling

- Use `ToolParams` and existing helper APIs for `JObject` reads.
- Support snake_case/camelCase aliases only when the handler contract already accepts both or the task explicitly adds that compatibility.
- Required parameters should return clear `ErrorResponse` messages when they are user input.

## Unity Compatibility

- Keep Unity version differences in compatibility helpers under `MCPForUnity/Runtime/Helpers/` or an existing helper.
- Do not scatter Unity version `#if` blocks across tool logic when a central shim is reasonable.
- Avoid reflection except at discovery boundaries or compatibility shims where Unity API changes require it.

## Editor UI And Package Assets

- `MCPForUnity/Editor/Windows/` contains Editor UI assets and code.
- `.meta` files are package source and must be preserved for Unity assets.
- Do not edit `Library/`, generated build output, or PackageCache copies.

## Verification

- Python tests do not prove C# compilation.
- C# changes need Unity compile evidence from an imported package when feasible.
- If Unity compile cannot be checked because no consumer project is pinned to the local fork, report the exact follow-up needed.
