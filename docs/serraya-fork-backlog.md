# Serraya Fork Backlog

Last updated: 2026-05-22

This backlog tracks fork-level work for `Serraya/unity-mcp`. It is separate from consumer app task packets in 12link or AdPhenom.

Use this file for MCP framework/tooling follow-up that belongs in the fork itself. Consumer apps should only pin accepted fork commits and add project-local custom tools.

## Active / Next

- Add an authoritative tool availability view.
  - Current problem: agents have to combine client tool lists, `tool_search`, direct calls, and `manage_tools list_groups` interpretation to understand whether a tool is available.
  - Desired behavior: expose one clear availability/introspection action, for example `manage_tools action=list_available`, that reports built-in and project-local tools together.
  - Include at least: tool name, source (`built_in` or `project_custom`), group if any, enabled/visible state, schema presence/parameter count, and stale/sync diagnostic if detectable.
  - Clarify `manage_tools list_groups` as group-toggle state only, or include project-local tools in a dedicated custom/ungrouped section.

- Verify or harden stdio custom-tool resync only if the visibility gap reproduces.
  - Clean restart evidence showed custom tools can appear when Unity is already open and connected.
  - Suspected failure modes: startup race, stale tool list after domain reload, Unity reconnect without tool-list update, or client-specific `tools/list_changed` behavior.
  - Candidate fixes: resync on Unity reconnect, make `manage_tools sync` report custom-tool registration state, and send `tools/list_changed` after custom-tool registration changes.

- Add C# tool group validation and update stale docs.
  - C# docs previously listed `menu`, while Python supports `core`, `docs`, `vfx`, `animation`, `ui`, `scripting_ext`, `testing`, `probuilder`, and `profiling`.
  - Unknown groups should fail clearly on the C# side instead of silently missing intended visibility behavior.

## Packaging / Installer

- Clean up Roslyn runtime compilation packaging.
  - `CustomTools/RoslynRuntimeCompilation` is outside the UPM package, but `RoslynInstaller` claims the runtime compilation tool is available after installing dependencies.
  - Either package the tool properly or change installer copy so it only promises installed dependencies.
  - Replace the main-thread busy-wait download loop with an editor-safe async/progress path before encouraging routine use.

- Keep fork pinning reproducible for consumer apps.
  - After each accepted fork commit, update app `.mcp.json`, `.codex/config.toml`, Unity `Packages/manifest.json`, and package lock files in separate app commits.
  - Do not mix fork framework commits with app pin updates.

## Cleanup

- Remove or fix stale/dead code in the fork.
  - Delete or update the stale `ParamNormalizerMiddleware` comment.
  - Remove deprecated `compute_project_id` if it is truly unused.
  - Simplify unreachable `_normalize_response` branches.

- Track resource drift.
  - Python exposes `prefab` and `prefab_stage` resources without a clearly matched C# backend surface.
  - C# has singular/plural GameObject component resource shapes that are not clearly mirrored on the Python side.
  - Agents should receive clear errors instead of confusing missing-backend failures.

- Backlog paging for list-heavy tools.
  - Review `manage_components`, `manage_gameobject`, `manage_prefabs`, `manage_animation`, and `manage_editor` for list actions that can return unbounded payloads.
  - Match the paging discipline already used by `find_gameobjects` and the read-console truncation work.

- Backlog Unity-version shim cleanup.
  - Move obvious Unity API rename/version guards, such as `PhysicsMaterial` vs `PhysicMaterial`, into compatibility shims instead of scattering inline `#if UNITY_*` blocks across tools.

## External Ideas To Revisit

- The1Studio fork ideas to inspect, not wholesale adopt.
  - Separate project/studio tool layer.
  - Targeted asset/folder reimport workflows.
  - Auto-start and multi-instance port-management ideas.
  - DOTS tools only if a consumer project actually needs Entities/DOTS.

- Upstream PR ideas to inspect.
  - Command gateway for multi-agent concurrent access.
  - Component reference wiring: `get_referenceable`, `set_reference`, `batch_wire`.
  - `manage_editor` / compile-loop wait action.
  - Codex setup alignment with native CLI.
  - Reliable auto-start and multi-instance connection support.
  - Optional environment tools such as Unity Hub or Asset Store automation only if a consumer workflow needs them.

## Decisions

- Do not use another fork as the base. Keep `Serraya/unity-mcp` close to upstream and cherry-pick small, reviewed fixes or ideas.
- Do not move consumer project policy into the fork unless the problem is generic MCP lifecycle, transport, schema, or tooling behavior.
- Do not use `execute_code` as a routine workaround for missing schemas, missing tools, or lifecycle gaps.
- Do not treat `batch_execute` as the custom-tool discoverability contract. Direct tool schemas should be usable when a tool supports parameters.
