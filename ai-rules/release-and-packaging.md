# Release And Packaging Rules

Load this when work touches versions, packaging, publishing, generated release artifacts, package manifests, Docker, PyPI, UPM, Asset Store, or lockfiles.

## Version Surfaces

- Python package metadata lives in `Server/pyproject.toml`.
- Server dependency resolution lives in `Server/uv.lock`.
- Unity package metadata lives in `MCPForUnity/package.json`.
- Root/project manifests may be package distribution helpers; inspect before editing.

## Generated Artifacts

- Do not update generated archives, build outputs, MCPB packages, Docker artifacts, or Asset Store upload material unless the task is a release/package task.
- Do not let local `uv` environment churn become an unrelated lockfile diff.
- If a command updates a lockfile only because of local environment setup, revert that specific unrelated lockfile change.

## Publishing Scripts

- Release helpers live under `tools/`.
- Read the script before running it. Many release scripts mutate versions, generated package contents, or remote state.
- Do not publish, tag, push, or upload without explicit user request.

## Consumer App Pins

- Updating consumer apps to a new fork commit is separate from changing this framework repo.
- Keep consumer app `.mcp.json`, Codex config, Unity manifest, and package lock updates in separate commits/tasks unless explicitly bundled.
