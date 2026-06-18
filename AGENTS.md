# Project Core Contract

This file is the always-on engineering contract for this repository.
Load additional rule files only when the task touches those areas:

- `ai-rules/engineering-method.md` - default workflow for implementation, refactors, bug fixes, investigations, behavior changes, and cross-file work.
- `ai-rules/project-knowledge.md` - compact index for durable, verified project facts. Read it first, then load only the relevant `ai-rules/knowledge/*.md` domain file(s).
- `ai-rules/python-server.md` - FastMCP server tools, resources, middleware, CLI, tests, and Python models.
- `ai-rules/unity-editor-package.md` - Unity C# Editor package tools/resources, command registry, package assets, editor UI, and Unity compatibility.
- `ai-rules/transports-and-routing.md` - stdio, HTTP, WebSocket hub, legacy TCP bridge, Unity instance routing, project-scoped tools, and multi-user isolation.
- `ai-rules/custom-tool-schema.md` - `[McpForUnityTool]` discovery, custom-tool metadata, `ParametersSchema`, `HandleCommand(JObject)`, FastMCP registration, and schema visibility.
- `ai-rules/testing-and-verification.md` - Python tests, Unity compile/test evidence, MCP client verification, logs, and diagnostic reporting.
- `ai-rules/release-and-packaging.md` - package versions, PyPI/UPM/Asset Store/Docker packaging, lockfiles, and generated artifacts.
- `ai-rules/git-workflow.md` - staging, commit grouping, branch expectations, and git hygiene.

## Project Shape

- MCP for Unity is one product with two coupled codebases:
  - `Server/` is the Python MCP server built on FastMCP.
  - `MCPForUnity/` is the Unity package that runs inside the Editor.
- Python MCP tools, Python CLI commands, Python resources, and C# Editor handlers are related but not generated from each other. Keep their contracts aligned deliberately.
- Unity-side command execution is handler-based: `[McpForUnityTool]` and `[McpForUnityResource]` classes expose `public static HandleCommand(JObject)`, registered by `CommandRegistry`.
- The server must support both stdio and HTTP transports. Do not fix one path while silently breaking the other.
- Multi-instance and remote-hosted behavior are first-class architecture, not edge cases.
- Tool schemas are user-facing API. Empty, misleading, or stale tool schemas are product bugs.

## Coding Preferences

- Prefer explicit ownership and a single source of truth over compatibility shims scattered across layers.
- Keep Python MCP tool signatures, CLI parameters, Unity `ToolParams` keys, and docs aligned when adding or changing tool parameters.
- For Python tool schemas, use typed parameters with `Annotated`, `Literal`, and Pydantic `Field` metadata when the values are known.
- For C# Editor tools, use `ToolParams` and existing helper classes instead of ad hoc `JObject` parsing.
- For Unity API compatibility, centralize version differences in `MCPForUnity/Runtime/Helpers/Unity*Compat.cs` or an existing compatibility helper.
- Avoid reflection as a convenience. Use it only at existing discovery/compatibility boundaries or when Unity API compatibility requires it.
- Avoid broad catch-all error handling. When recovery is real, return a clear `ErrorResponse`/`MCPResponse`; otherwise let invalid development states fail visibly.
- Keep comments short and useful. Do not add boilerplate comments to obvious code.
- Do not introduce new global runtime state unless it belongs to an existing process-level service or registry.
- Preserve backwards compatibility for published MCP/CLI/tool contracts unless the task explicitly asks for a breaking change and documents migration.

## Engineering Preferences

- Do not patch from a single symptom file when the surrounding registration, transport, or handler path is available to read.
- Before changing behavior, trace the request end to end: MCP client schema -> Python FastMCP function -> transport/middleware -> Unity command dispatch -> C# handler -> response normalization.
- For server-backed behavior, inspect the actual server implementation rather than inferring from docs, CLI wrappers, or client config snippets.
- For custom tool and transport changes, check both stdio and HTTP/WebSocket paths.
- Keep edits scoped. Do not mix framework changes, app pin updates, release metadata, and generated documentation unless the task asks for that full sequence.
- Do not edit generated package/cache/build artifacts unless the release or packaging task explicitly requires it.
- If a durable architecture fact is learned, record it in the most specific `ai-rules/knowledge/*.md` file and update `ai-rules/project-knowledge.md` if a new domain file is needed.

## Task Framing

Before implementing a non-trivial change, restate the request as a verifiable goal with an observable:

- "Fix custom tools" -> "Trace C# discovery through Python/FastMCP registration, identify where schema is lost, then verify a direct tool schema includes expected parameters."
- "Fix routing" -> "Reproduce or simulate the selected instance/session state, then confirm the command routes to the intended Unity instance."
- "Add a tool" -> "Expose typed MCP schema, route to Unity, validate handler behavior, and test the Python layer."
- "Refactor transport" -> "Confirm stdio and HTTP/WebSocket behavior before and after with targeted tests."

Skip this ceremony only for tiny mechanical edits such as typo fixes or local comments.

## Verification Expectations

- Python changes require targeted pytest coverage when tests exist for the touched area.
- Transport, schema, middleware, and model changes need tests at the layer that owns the behavior.
- C# Unity package changes require Unity compile evidence when the package is imported into a Unity project. If that is not possible in the current turn, say exactly why and what evidence is still needed.
- Do not claim MCP client schema visibility unless it was verified through `tools/list`, `tool_search`, FastMCP schema output, or equivalent client-visible introspection.
- Do not shell-launch Unity for routine compile verification unless the user explicitly asks for that environment-level operation. Prefer an already-running Unity Editor plus MCP/Console evidence, or ask for user-run Unity Test Runner output.
- If a task cannot be fully verified because app/package pins were not updated, report that as a remaining verification gap instead of implying success.

## Repository Boundaries

- Keep MCP fork changes separate from consumer app changes.
- Local private notes may exist under `.private/`. That directory is intentionally git-ignored for internal project names, local todo, and consumer-app context; never stage, commit, or push it.
- Do not update consumer app `.mcp.json`, Codex config, `Packages/manifest.json`, or lockfiles unless the user asks to repin that app.
- Do not create commits unless explicitly requested.
- Do not revert user changes. If unrelated dirty files exist, leave them alone.
