# Project Knowledge

## Purpose

This file is the index for durable, verified project facts that future work should not rediscover.

Read this index first for non-trivial work, then load only the relevant `ai-rules/knowledge/*.md` file(s).

Only record verified facts. Do not add hypotheses, temporary debugging notes, or transient task state.

## Loading Guide

- `ai-rules/knowledge/architecture.md` - load for repo shape, Python/C# layer responsibilities, tool/resource/CLI symmetry, and command dispatch.
- `ai-rules/knowledge/python-server.md` - load for FastMCP registration, models, middleware, resources, CLI, server startup, and tests.
- `ai-rules/knowledge/unity-editor-package.md` - load for C# Editor tools/resources, `CommandRegistry`, `ToolDiscoveryService`, helpers, UI window, package assets, and Unity compatibility shims.
- `ai-rules/knowledge/transports-and-routing.md` - load for stdio, HTTP, WebSocket hub, legacy TCP, instance targeting, project-scoped tools, remote-hosted mode, and session isolation.
- `ai-rules/knowledge/custom-tool-schema.md` - load for project-local custom tools, direct-call schemas, `ParametersSchema`, custom tool registration, and schema visibility.
- `ai-rules/knowledge/testing.md` - load for pytest layout, integration test stubs, Unity verification surfaces, and expected evidence.
- `ai-rules/knowledge/release-and-packaging.md` - load for version files, package publishing, generated artifacts, Docker/PyPI/UPM/Asset Store surfaces, and lockfile policy.

If no existing file fits a new durable fact, create a focused file under `ai-rules/knowledge/` and add it to this loading guide.

## Recording Rules

- Add facts to the most specific knowledge file.
- Replace or remove assertions invalidated by the same change; do not append a
  dated correction beneath executable-looking stale guidance. Git and task
  evidence retain chronology.
- Keep each durable assertion in one owning record, and inspect routed siblings
  that could still imply the superseded state before closing the edit.
- Keep facts short, concrete, and reusable.
- Prefer facts tied to task triggers over abstract taxonomy.
- Include exact paths when the fact depends on source layout.
