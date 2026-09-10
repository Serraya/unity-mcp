# Testing And Verification Rules

Load this when work requires tests, compile checks, runtime diagnostics, MCP client introspection, logs, or result reporting.

## Automated Test Execution

- During implementation, run affected test cases selected from the changed
  behavior and affected consumers, including shared-contract paths. Use explicit
  case filters; widen to a small fixture when filtering is unavailable or shared
  setup requires it. Do not default to a whole module, namespace, or suite for a
  local edit.
- After a failure or review correction, rerun failed and newly affected cases.
  A small fix does not invalidate unrelated verification or justify another
  broad suite.
- Reserve a full suite, when needed, for one final integration check after the
  task's changes are assembled and affected cases pass. Do not run it per edit,
  worker, intermediate milestone, or commit. Diagnose final failures with
  targeted cases. A repeat needs an explicit owner request, a required CI/release
  gate, or a material code/dependency/environment change that invalidates the
  previous integration result; state the reason before running.
- Use existing test infrastructure and verify that the selected cases actually
  ran against current sources. Report the filter/boundary, executed count and
  result; a green empty selection or stale assembly is not evidence.
- This cadence preserves test coverage and required compile, lint, build and
  runtime checks. Declared flake/soak/statistical experiments keep their bounded
  sample and stop policy. Do not create test infrastructure for this rule.

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
