# Testing And Verification Rules

Load this when work requires tests, compile checks, runtime diagnostics, MCP client introspection, logs, or result reporting.

## Automated Test Execution

A regression test exercises the production owner and transition relevant to
its claim. Supply valid initial state and controlled external inputs; let
production code compute the outcome. A helper test proves its helper boundary,
not an unexecuted binding or lifecycle. Where safely practical, show failure
before the correction and success after it; otherwise state the limitation.
Report affected consumers covered and material gaps. Use existing infrastructure
and the execution cadence below.

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

Close each named claim when its complete required evidence route passes.
Static or deterministic checks can close claims they fully exercise; rendered,
native-input, lifecycle, build, deployment and design claims retain their
corresponding predicates. Report remaining claims separately; do not request a
human check for an unrelated layer.

A failure in the production owner under valid inputs, with an established
ordinary entry/binding path, can prove a code defect using controlled external
responses. It does not by itself prove deployment, incidence, device behavior
or the cause of an earlier report. State that boundary.

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
- Verify the code actually loaded, not the pin alone: record the imported Unity
  package/assembly and the running Python server source separately, including
  working-tree hashes for uncommitted edits. An authorized local deployment can
  verify those bytes without changing a pin; it does not qualify the published
  package or another client process. Without loaded-code evidence, report the
  specific unverified layer. This rule grants no deployment or restart authority.

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
