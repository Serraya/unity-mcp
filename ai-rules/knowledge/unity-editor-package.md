# Unity Editor Package Knowledge

- Unity package metadata is in `MCPForUnity/package.json`.
- Editor assembly definition is `MCPForUnity/Editor/MCPForUnity.Editor.asmdef`.
- Runtime assembly definition is `MCPForUnity/Runtime/MCPForUnity.Runtime.asmdef`.
- `[McpForUnityTool]` is defined in `MCPForUnity/Editor/Tools/McpForUnityToolAttribute.cs`.
- `[McpForUnityResource]` is defined in `MCPForUnity/Editor/Resources/McpForUnityResourceAttribute.cs`.
- Tool/resource classes must expose public static `HandleCommand(JObject)` for `CommandRegistry` discovery.
- `ToolParams` in `MCPForUnity/Editor/Helpers/ToolParams.cs` is the standard helper for command parameter extraction.
- Unity API compatibility shims live under `MCPForUnity/Runtime/Helpers/Unity*Compat.cs`.
- Editor window UI assets and code live under `MCPForUnity/Editor/Windows/`.
