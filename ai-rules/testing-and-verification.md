# Testing And Verification Rules

Load this when work requires tests, compile checks, runtime diagnostics, MCP client introspection, logs, or result reporting.

## Python Verification

- Run Python tests from `Server/`.
- Use `uv run --extra dev pytest ...` for tests that need dev dependencies.
- Target tests to the changed layer first:
  - models -> model characterization tests;
  - tool registration/schema -> custom tool service and sync tests;
  - transport/routing -> transport characterization and integration tests;
  - individual tool wrappers -> domain-specific tests.

## Unity Verification

- Python tests do not prove C# compile.
- Unity compile evidence should come from a Unity project importing the package.
- Prefer an already-running Editor and Console/MCP evidence when available.
- Do not shell-launch Unity for routine compile checks unless explicitly requested.
- If package pins still point at an old commit, say that Unity compile/direct MCP verification for local package edits is not yet possible.

## MCP Client Verification

- Tool implementation success is not the same as schema visibility.
- For schema tasks, use one of:
  - MCP `tools/list` output;
  - `tool_search` output in the active client;
  - FastMCP `list_tools()` schema output;
  - another client-visible introspection path.
- For custom tools, verify both a direct-call schema and a successful call if the current client supports it.

## Logs And Reporting

- Report concise evidence, not full noisy logs.
- Distinguish current-app evidence from local-fork evidence.
- Do not fabricate test results. If verification is blocked, state the blocker and exact command/action needed next.
