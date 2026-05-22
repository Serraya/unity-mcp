# Python Server Rules

Load this when work touches `Server/`, FastMCP tools/resources, CLI commands, middleware, models, tests, or Python transport code.

## Layer Ownership

- MCP tools live under `Server/src/services/tools/` and are registered with `@mcp_for_unity_tool`.
- Resources live under `Server/src/services/resources/` and are read-oriented.
- CLI commands live under `Server/src/cli/commands/` and are not generated from MCP tools.
- Shared data contracts live under `Server/src/models/` and transport contracts under `Server/src/transport/`.

## Tool Schema

- MCP tool signatures are the user-facing schema. Keep them typed and descriptive.
- Use `Annotated[..., Field(description=...)]` or the existing local pattern for parameter descriptions.
- Use `Literal[...]` when valid action names or enum values are known.
- Preserve aliases deliberately; do not rely on undocumented client-side normalization.
- When a Python model changes, update model characterization tests and any registration tests that serialize/deserialize that model.

## Registration

- `register_all_tools()` auto-discovers decorated tools. Do not manually add parallel registration lists.
- Tool `group` controls visibility. Validate group names through the registry instead of inventing new strings in call sites.
- Server-level visibility and session-level visibility are distinct. Transport changes must respect both.

## CLI

- CLI command behavior is a developer interface, not proof of MCP client behavior.
- If a tool contract changes and a CLI equivalent exists, update both intentionally or document why CLI is out of scope.

## Tests

- Run tests from `Server/`.
- Use `uv run --extra dev pytest ...` when dev dependencies are needed.
- Prefer targeted tests for the touched layer before broad test runs.
- Be aware that integration conftests may stub FastMCP. Tests that need real FastMCP schema output may require a standalone probe or careful fixture selection.
