# Release And Packaging Knowledge

- Server package version is declared in `Server/pyproject.toml`.
- Unity package version is declared in `MCPForUnity/package.json`.
- `Server/uv.lock` may reflect local editable package metadata when `uv` resolves the project.
- Packaging and release helper scripts live under `tools/`.
- Docker surfaces include `Server/Dockerfile` and root `docker-compose.yml`.
- Consumer Unity projects pin this repo separately through their `Packages/manifest.json` and package lock.
- Consumer MCP clients may also pin the Python server through `.mcp.json`, `.codex/config.toml`, or similar client config.
