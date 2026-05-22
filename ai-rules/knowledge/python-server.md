# Python Server Knowledge

- Python package metadata is in `Server/pyproject.toml`.
- Python dependency lockfile is `Server/uv.lock`.
- MCP server startup is in `Server/src/main.py`.
- MCP tools are auto-discovered from `Server/src/services/tools/`.
- FastMCP resources are registered from `Server/src/services/resources/`.
- Shared response/tool models live in `Server/src/models/models.py`.
- WebSocket hub/session management lives in `Server/src/transport/plugin_hub.py`.
- Legacy stdio/TCP Unity connection code lives in `Server/src/transport/legacy/unity_connection.py`.
- The integration test suite may stub `fastmcp` in `Server/tests/integration/conftest.py`; real FastMCP schema behavior may need a non-integration unit test or standalone probe.
