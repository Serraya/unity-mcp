"""Status and proceed decisions must use current, correctly routed evidence."""
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from models import MCPResponse
from .test_helpers import DummyContext


NOW = 1800000000000
TARGET = "Project@12345678"


def healthy_state():
    return {
        "schema_version": "unity-mcp/editor_state@2",
        "observed_at_unix_ms": NOW,
        "sequence": 7,
        "unity": {"instance_id": TARGET, "unity_version": "6000.6.0f1"},
        "editor": {"play_mode": {"is_playing": False, "is_paused": False, "is_changing": False}},
        "activity": {"phase": "idle"},
        "compilation": {"is_compiling": False, "is_domain_reload_pending": False},
        "assets": {"is_updating": False, "refresh": {"is_refresh_in_progress": False}},
        "tests": {"is_running": False},
    }


@pytest.fixture
def status_boundary(monkeypatch):
    import services.resources.editor_state as mod
    import services.resources.project_info as project
    from core.config import config

    ctx = DummyContext()
    ctx._state["unity_instance"] = TARGET
    monkeypatch.setattr(config, "project_path", None)
    monkeypatch.setattr(mod, "_now_unix_ms", lambda: NOW)
    monkeypatch.setattr(project, "get_project_info", AsyncMock(return_value=MCPResponse(
        success=True, data={"projectRoot": "/projects/project"})))
    monkeypatch.setattr(mod.external_changes_scanner, "set_project_root", lambda *a: None)
    monkeypatch.setattr(mod.external_changes_scanner, "update_and_get", lambda *a: {})
    send = AsyncMock()
    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", send)
    return mod, ctx, send


@pytest.mark.asyncio
@pytest.mark.parametrize("as_model", [True, False])
@pytest.mark.parametrize("reason", ["not_found", "connection_refused", "timeout", "reloading"])
async def test_transport_failure_remains_failure(status_boundary, as_model, reason):
    mod, ctx, send = status_boundary
    failure = MCPResponse(success=False, error=reason, message="Cannot query Editor",
                          hint="retry", data={"reason": reason})
    send.return_value = failure if as_model else failure.model_dump()
    result = await mod.get_editor_state(ctx)
    assert result.model_dump() == failure.model_dump()


@pytest.mark.asyncio
async def test_transport_exception_is_unknown_not_ready(status_boundary):
    mod, ctx, send = status_boundary
    send.side_effect = TimeoutError("status timed out")
    result = await mod.get_editor_state(ctx)
    assert result.success is False
    assert "status timed out" in result.error
    assert result.data is None


@pytest.mark.asyncio
@pytest.mark.parametrize("response", [None, [], "bad", {}, {"success": True},
                                      {"success": True, "data": []},
                                      {"success": "true", "data": healthy_state()}])
async def test_malformed_response_does_not_manufacture_a_snapshot(status_boundary, response):
    mod, ctx, send = status_boundary
    send.return_value = response
    result = await mod.get_editor_state(ctx)
    assert result.success is False
    assert result.data is None


@pytest.mark.asyncio
@pytest.mark.parametrize("section", ["editor", "compilation", "assets", "tests", "activity", "unity"])
async def test_missing_state_is_unknown(status_boundary, section):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data[section] = None
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.data[section] is None
    assert result.data["advice"]["ready_for_tools"] is None


@pytest.mark.asyncio
@pytest.mark.parametrize("field,value", [("observed_at_unix_ms", None),
                                         ("observed_at_unix_ms", NOW + 10000),
                                         ("schema_version", "unknown")])
async def test_invalid_snapshot_metadata_is_not_current(status_boundary, field, value):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data[field] = value
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.success is False


@pytest.mark.asyncio
@pytest.mark.parametrize("section,field,reason", [
    ("compilation", "is_compiling", "compiling"),
    ("compilation", "is_domain_reload_pending", "domain_reload"),
    ("assets", "is_updating", "asset_import"),
    ("tests", "is_running", "running_tests"),
])
async def test_confirmed_busy_blocks(status_boundary, section, field, reason):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data[section][field] = True
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.success is True
    assert result.data["advice"]["ready_for_tools"] is False
    assert reason in result.data["advice"]["blocking_reasons"]


@pytest.mark.asyncio
async def test_stale_healthy_data_is_not_restamped_or_ready(status_boundary):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data["observed_at_unix_ms"] -= 10000
    data["advice"] = {"ready_for_tools": True}
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.data["observed_at_unix_ms"] == NOW - 10000
    assert result.data["staleness"]["is_stale"] is True
    assert result.data["advice"]["ready_for_tools"] is None


@pytest.mark.asyncio
async def test_same_display_name_wrong_checkout_is_rejected(status_boundary):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data["unity"]["instance_id"] = "Project@87654321"
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.success is False
    assert result.error == "editor_instance_mismatch"
    assert result.data is None


@pytest.mark.asyncio
async def test_wrong_project_root_is_rejected(status_boundary, monkeypatch):
    mod, ctx, send = status_boundary
    from core.config import config
    monkeypatch.setattr(config, "project_path", "/projects/another-checkout")
    send.return_value = {"success": True, "data": healthy_state()}
    result = await mod.get_editor_state(ctx)
    assert result.success is False
    assert result.error == "editor_project_mismatch"


@pytest.mark.asyncio
async def test_recovery_uses_new_state_not_last_healthy_snapshot(status_boundary):
    mod, ctx, send = status_boundary
    fresh = {"success": True, "data": healthy_state()}
    send.side_effect = [deepcopy(fresh), MCPResponse(success=False, error="offline"), deepcopy(fresh)]
    first, failed, recovered = [await mod.get_editor_state(ctx) for _ in range(3)]
    assert first.data["advice"]["ready_for_tools"] is True
    assert failed.success is False and failed.data is None
    assert recovered.data["advice"]["ready_for_tools"] is True
    assert send.await_count == 3


@pytest.mark.asyncio
async def test_full_producer_identity_matches_stdio_short_hash(status_boundary):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data["unity"]["instance_id"] = TARGET + "90abcdef"
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.success and result.data["advice"]["ready_for_tools"] is True
    assert result.data["unity"]["instance_id"] == TARGET


@pytest.mark.asyncio
@pytest.mark.parametrize("transport", ["stdio", "http"])
async def test_empty_routing_consumer_requires_independent_process_check(monkeypatch, transport):
    import services.tools.set_active_instance as select
    from core.config import config
    monkeypatch.setattr(config, "transport_mode", transport)
    monkeypatch.setattr(select, "get_unity_connection_pool", lambda: SimpleNamespace(
        discover_all_instances=lambda **kw: []))
    monkeypatch.setattr(select.PluginHub, "get_sessions", AsyncMock(return_value=SimpleNamespace(sessions={})))
    result = await select.set_active_instance(DummyContext(), TARGET)
    assert result["success"] is False
    assert "currently connected" in result["error"]
    assert "unity editors running --json" in result["message"]
    assert "start unity" not in result["message"].lower()


@pytest.mark.asyncio
async def test_old_producer_without_identity_stays_unknown(status_boundary):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data["unity"]["instance_id"] = None
    send.return_value = {"success": True, "data": data}
    result = await mod.get_editor_state(ctx)
    assert result.data["unity"]["instance_id"] is None
    assert result.data["advice"]["ready_for_tools"] is None


@pytest.mark.asyncio
async def test_failure_cannot_carry_cached_ready_advice(status_boundary):
    mod, ctx, send = status_boundary
    data = healthy_state()
    data["advice"] = {"ready_for_tools": True}
    send.return_value = MCPResponse(success=False, error="offline", data=data)
    result = await mod.get_editor_state(ctx)
    assert result.error == "offline" and not result.success
    assert result.data["advice"]["ready_for_tools"] is None


@pytest.mark.asyncio
async def test_status_tool_does_not_tell_agent_to_open_editor_from_empty_discovery(status_boundary, monkeypatch):
    mod, ctx, send = status_boundary
    import services.tools.unity_status as status
    send.return_value = MCPResponse(success=False, error="not_found")
    monkeypatch.setattr(status, "unity_instances", AsyncMock(return_value={
        "success": True, "transport": "stdio", "instance_count": 0, "instances": []}))
    monkeypatch.setattr(status, "get_unity_instance_middleware", lambda: SimpleNamespace(
        get_active_instance=AsyncMock(return_value=TARGET)))
    result = await status.unity_status(ctx)
    assert result["success"] is True  # Discovery succeeded, not the Editor query.
    assert result["editor_state"]["success"] is False
    assert "unknown" in result["message"].lower()
    assert "unity editors running --json" in result["message"]
    assert "start unity" not in result["message"].lower()


@pytest.mark.asyncio
@pytest.mark.parametrize("response", [
    MCPResponse(success=False, error="offline"),
    {"success": True, "data": {**healthy_state(), "editor": None}},
    {"success": True, "data": {**healthy_state(), "observed_at_unix_ms": NOW - 10000}},
])
async def test_proceed_consumer_waits_for_actual_recovery(status_boundary, monkeypatch, response):
    mod, ctx, send = status_boundary
    import services.tools.refresh_unity as refresh
    monkeypatch.setattr(refresh, "_in_pytest", lambda: False)
    send.side_effect = [response, {"success": True, "data": healthy_state()}]
    ready, _ = await refresh.wait_for_editor_ready(ctx, timeout_s=2)
    assert ready is True
    assert send.await_count == 2


@pytest.mark.asyncio
async def test_mutation_rejection_is_not_replayed_when_readiness_unknown(monkeypatch):
    import services.tools.refresh_unity as refresh
    rejected = {"success": False, "data": {"reason": "reloading"}, "hint": "retry"}
    send = AsyncMock(return_value=rejected)
    monkeypatch.setattr(refresh.unity_transport, "send_with_unity_instance", send)
    monkeypatch.setattr(refresh, "wait_for_editor_ready", AsyncMock(return_value=(False, 30)))
    result = await refresh.send_mutation(DummyContext(), TARGET, "manage_script", {"action": "create"})
    assert result == rejected
    assert send.await_count == 1


@pytest.mark.asyncio
async def test_real_mixed_edit_consumer_stops_after_acknowledged_mutation(monkeypatch):
    import services.tools.refresh_unity as refresh
    import services.tools.script_apply_edits as edits
    monkeypatch.setattr(edits, "async_send_command_with_retry", AsyncMock(return_value={
        "success": True, "data": {"contents": "class Example { void A() {} }"}}))
    send = AsyncMock(return_value={"success": True, "message": "Applied"})
    monkeypatch.setattr(refresh.unity_transport, "send_with_unity_instance", send)
    monkeypatch.setattr(refresh, "wait_for_editor_ready", AsyncMock(return_value=(False, 30)))
    result = await edits.script_apply_edits(DummyContext(), name="Example", path="Assets", edits=[
        {"op": "prepend", "text": "// header\n"},
        {"op": "replace_method", "className": "Example", "methodName": "A",
         "replacement": "void A() { return; }"},
    ])
    assert result["success"] is False
    assert result["data"]["operation_acknowledged"] is True
    assert send.await_count == 1
    assert send.call_args.args[3]["action"] == "apply_text_edits"


@pytest.mark.asyncio
@pytest.mark.parametrize("wait,ready", [(False, False), (True, False), (True, True)])
async def test_refresh_timeout_preserves_failure_and_never_replays(monkeypatch, wait, ready):
    import services.tools.refresh_unity as refresh
    failure = MCPResponse(success=False, error="timeout", hint="retry")
    send = AsyncMock(return_value=failure)
    poll = AsyncMock(return_value=(ready, 0.1))
    clear = AsyncMock()
    monkeypatch.setattr(refresh.unity_transport, "send_with_unity_instance", send)
    monkeypatch.setattr(refresh, "wait_for_editor_ready", poll)
    monkeypatch.setattr(refresh.external_changes_scanner, "clear_dirty", clear)
    result = await refresh.refresh_unity(DummyContext(), compile="request", wait_for_ready=wait)
    assert result.success is False and result.error == "timeout"
    assert result.data["original_response"] == failure.model_dump()
    assert result.data["ready_for_tools"] is (True if wait and ready else None)
    assert send.await_count == 1 and poll.await_count == int(wait)
    clear.assert_not_called()


@pytest.mark.asyncio
@pytest.mark.parametrize("response", [None, {}, {"success": "false"}, {"success": "true"}])
async def test_malformed_refresh_ack_does_not_clear_dirty(monkeypatch, response):
    import services.tools.refresh_unity as refresh
    monkeypatch.setattr(refresh.unity_transport, "send_with_unity_instance", AsyncMock(return_value=response))
    clear = AsyncMock()
    monkeypatch.setattr(refresh.external_changes_scanner, "clear_dirty", clear)
    result = await refresh.refresh_unity(DummyContext(), wait_for_ready=False)
    assert result.success is False
    clear.assert_not_called()


@pytest.mark.asyncio
async def test_readiness_budget_bounds_a_stalled_state_query(monkeypatch):
    import asyncio
    import services.tools.refresh_unity as refresh
    monkeypatch.setattr(refresh, "_in_pytest", lambda: False)
    async def stalled(*args):
        await asyncio.sleep(60)
    monkeypatch.setattr(refresh.editor_state, "get_editor_state", stalled)
    ready, elapsed = await refresh.wait_for_editor_ready(DummyContext(), timeout_s=0.02)
    assert ready is False and elapsed < 1
