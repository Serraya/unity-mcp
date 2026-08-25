from types import SimpleNamespace

import pytest

from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_unity_status_exposes_resource_data_as_a_read_only_tool(monkeypatch):
    import services.tools.unity_status as status_module

    async def fake_instances(ctx):
        return {
            "success": True,
            "transport": "stdio",
            "instance_count": 1,
            "instances": [{"id": "Proj@abc123", "status": "running"}],
        }

    async def fake_editor_state(ctx):
        return SimpleNamespace(
            model_dump=lambda: {
                "success": True,
                "data": {"advice": {"ready_for_tools": True}},
            }
        )

    class FakeMiddleware:
        async def get_active_instance(self, ctx):
            return "Proj@abc123"

    monkeypatch.setattr(status_module, "unity_instances", fake_instances)
    monkeypatch.setattr(status_module, "get_editor_state", fake_editor_state)
    monkeypatch.setattr(
        status_module,
        "get_unity_instance_middleware",
        lambda: FakeMiddleware(),
    )

    result = await status_module.unity_status(DummyContext())

    assert result == {
        "success": True,
        "transport": "stdio",
        "active_instance": "Proj@abc123",
        "instance_count": 1,
        "instances": [{"id": "Proj@abc123", "status": "running"}],
        "editor_state": {
            "success": True,
            "data": {"advice": {"ready_for_tools": True}},
        },
    }


@pytest.mark.asyncio
async def test_unity_status_guides_selection_without_reading_editor_state(monkeypatch):
    import services.tools.unity_status as status_module

    async def fake_instances(ctx):
        return {
            "success": True,
            "transport": "stdio",
            "instance_count": 2,
            "instances": [
                {"id": "ProjA@aaaa", "status": "running"},
                {"id": "ProjB@bbbb", "status": "running"},
            ],
        }

    async def unexpected_editor_state(ctx):
        raise AssertionError("Editor state is ambiguous without an active instance")

    class FakeMiddleware:
        async def get_active_instance(self, ctx):
            return None

    monkeypatch.setattr(status_module, "unity_instances", fake_instances)
    monkeypatch.setattr(status_module, "get_editor_state", unexpected_editor_state)
    monkeypatch.setattr(
        status_module,
        "get_unity_instance_middleware",
        lambda: FakeMiddleware(),
    )

    result = await status_module.unity_status(DummyContext())

    assert result["success"] is True
    assert result["active_instance"] is None
    assert result["editor_state"] is None
    assert "set_active_instance" in result["message"]
