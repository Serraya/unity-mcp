# Architecture Knowledge

- The product has two coupled codebases: `Server/` for the Python FastMCP server and `MCPForUnity/` for the Unity Editor package.
- Python MCP tools, Python CLI commands, Python resources, and C# Editor handlers are distinct layers. Similar names do not imply generated code.
- Python MCP tools call Unity through transport helpers; CLI commands call Unity through CLI connection helpers.
- C# command handlers are registered by `MCPForUnity/Editor/Tools/CommandRegistry.cs`.
- C# metadata discovery for tool listing/visibility is owned by `MCPForUnity/Editor/Services/ToolDiscoveryService.cs`.
- Built-in Python tool registration metadata lives in `Server/src/services/registry/tool_registry.py`.
- `.meta` files under `MCPForUnity/` are Unity package source files and should be preserved with asset moves/additions.
