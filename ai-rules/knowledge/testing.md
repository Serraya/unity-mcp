# Testing Knowledge

- Python tests live under `Server/tests/`.
- Run Python tests from `Server/` with `uv run --extra dev pytest ...` when dev dependencies are needed.
- Transport behavior is covered heavily by `Server/tests/test_transport_characterization.py` and integration tests under `Server/tests/integration/`.
- Model defaults/serialization are characterized in `Server/tests/test_models_characterization.py`.
- Unity package C# compilation is not proven by Python tests.
- Unity compile verification requires a Unity project importing the package revision under test.
- MCP client schema visibility requires client-facing introspection such as `tools/list`, `tool_search`, or FastMCP `list_tools()` output.
