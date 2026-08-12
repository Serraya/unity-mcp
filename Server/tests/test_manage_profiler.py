from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_profiler import (
    manage_profiler,
    ALL_ACTIONS,
    SESSION_ACTIONS,
    COUNTER_ACTIONS,
    MEMORY_SNAPSHOT_ACTIONS,
    FRAME_DEBUGGER_ACTIONS,
    UTILITY_ACTIONS,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_profiler_actions_count():
    assert len(ALL_ACTIONS) == 23


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_session_actions():
    expected = {"profiler_start", "profiler_stop", "profiler_status", "profiler_set_areas"}
    assert set(SESSION_ACTIONS) == expected


def test_counter_actions():
    expected = {
        "get_frame_timing", "get_counters", "get_object_memory", "get_marker_calltree",
        "get_frame_summary", "get_hot_markers", "find_marker", "export_profile_tables",
        "profiler_job_status", "profiler_job_cancel",
    }
    assert set(COUNTER_ACTIONS) == expected


def test_memory_snapshot_actions():
    expected = {"memory_take_snapshot", "memory_list_snapshots", "memory_compare_snapshots"}
    assert set(MEMORY_SNAPSHOT_ACTIONS) == expected


def test_frame_debugger_actions():
    expected = {
        "frame_debugger_enable",
        "frame_debugger_disable",
        "frame_debugger_get_events",
        "frame_debugger_get_event_details",
        "frame_debugger_capture_event_output",
    }
    assert set(FRAME_DEBUGGER_ACTIONS) == expected


def test_utility_actions():
    assert UTILITY_ACTIONS == ["ping"]


def test_all_actions_is_union():
    expected = set(UTILITY_ACTIONS + SESSION_ACTIONS + COUNTER_ACTIONS + MEMORY_SNAPSHOT_ACTIONS + FRAME_DEBUGGER_ACTIONS)
    assert set(ALL_ACTIONS) == expected


# ---------------------------------------------------------------------------
# Invalid / missing action
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


# ---------------------------------------------------------------------------
# Each action forwards correctly
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("action_name", [
    "ping",
    "profiler_start", "profiler_stop", "profiler_status", "profiler_set_areas",
    "get_frame_timing", "get_counters", "get_object_memory", "get_marker_calltree",
    "get_frame_summary", "get_hot_markers", "find_marker", "export_profile_tables",
    "profiler_job_status", "profiler_job_cancel",
    "memory_take_snapshot", "memory_list_snapshots", "memory_compare_snapshots",
    "frame_debugger_enable", "frame_debugger_disable", "frame_debugger_get_events",
    "frame_debugger_get_event_details", "frame_debugger_capture_event_output",
])
def test_every_action_forwards_to_unity(mock_unity, action_name):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action=action_name)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_profiler"
    assert mock_unity["params"]["action"] == action_name


def test_uses_unity_instance_from_context(mock_unity):
    asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_frame_timing")
    )
    assert mock_unity["unity_instance"] == "unity-instance-1"


# ---------------------------------------------------------------------------
# Param forwarding
# ---------------------------------------------------------------------------

def test_get_counters_forwards_category(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_counters", category="Render")
    )
    assert result["success"] is True
    assert mock_unity["params"]["category"] == "Render"


def test_get_counters_forwards_counter_names(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="get_counters",
            category="Render", counters=["Draw Calls Count", "Batches Count"],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["counters"] == ["Draw Calls Count", "Batches Count"]


def test_get_counters_omits_none_counters(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_counters", category="Memory")
    )
    assert result["success"] is True
    assert "counters" not in mock_unity["params"]


def test_profiler_start_forwards_log_file(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_start", log_file="/tmp/profile.raw")
    )
    assert result["success"] is True
    assert mock_unity["params"]["log_file"] == "/tmp/profile.raw"


def test_profiler_start_forwards_callstacks(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_start", enable_callstacks=True)
    )
    assert result["success"] is True
    assert mock_unity["params"]["enable_callstacks"] is True


def test_profiler_set_areas_forwards_areas(mock_unity):
    areas = {"CPU": True, "Audio": False}
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_set_areas", areas=areas)
    )
    assert result["success"] is True
    assert mock_unity["params"]["areas"] == areas


def test_get_object_memory_forwards_path(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_object_memory", object_path="/Player/Mesh")
    )
    assert result["success"] is True
    assert mock_unity["params"]["object_path"] == "/Player/Mesh"


def test_get_marker_calltree_forwards_all_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="get_marker_calltree",
            frame_index=1379,
            marker_filter="Panel.PerformPick",
            thread_name="Main Thread",
            thread_index=0,
            match_mode="contains",
            max_depth=8,
            max_rows=200,
            include_parents=True,
            include_children=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "get_marker_calltree",
        "frame_index": 1379,
        "marker_filter": "Panel.PerformPick",
        "thread_name": "Main Thread",
        "thread_index": 0,
        "match_mode": "contains",
        "max_depth": 8,
        "max_rows": 200,
        "include_parents": True,
        "include_children": True,
    }


def test_get_marker_calltree_omits_none_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="get_marker_calltree",
            frame_index=1379,
            marker_filter="Panel.PerformPick",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "get_marker_calltree",
        "frame_index": 1379,
        "marker_filter": "Panel.PerformPick",
    }


def test_get_frame_summary_forwards_recorded_frame_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="get_frame_summary",
            start_frame=1000,
            end_frame=1300,
            thread_name="Main Thread",
            thread_index=0,
            top_n=20,
            max_frames=500,
            execution_mode="sync",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "get_frame_summary",
        "start_frame": 1000,
        "end_frame": 1300,
        "thread_name": "Main Thread",
        "thread_index": 0,
        "top_n": 20,
        "max_frames": 500,
        "execution_mode": "sync",
    }


def test_get_hot_markers_forwards_recorded_marker_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="get_hot_markers",
            start_frame=1000,
            end_frame=1300,
            thread_name="Main Thread",
            thread_index=0,
            marker_filter="Panel.",
            match_mode="contains",
            sort_by="max_self_time",
            top_n=50,
            max_frames=500,
            execution_mode="async",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "get_hot_markers",
        "start_frame": 1000,
        "end_frame": 1300,
        "thread_name": "Main Thread",
        "thread_index": 0,
        "marker_filter": "Panel.",
        "match_mode": "contains",
        "sort_by": "max_self_time",
        "top_n": 50,
        "max_frames": 500,
        "execution_mode": "async",
    }


def test_find_marker_forwards_recorded_marker_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="find_marker",
            start_frame=1000,
            end_frame=1300,
            thread_name="Main Thread",
            thread_index=0,
            marker_filter="Panel.PerformPick",
            match_mode="exact",
            top_n=25,
            max_frames=250,
            execution_mode="auto",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "find_marker",
        "start_frame": 1000,
        "end_frame": 1300,
        "thread_name": "Main Thread",
        "thread_index": 0,
        "marker_filter": "Panel.PerformPick",
        "match_mode": "exact",
        "top_n": 25,
        "max_frames": 250,
        "execution_mode": "auto",
    }


def test_recorded_marker_queries_omit_none_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="get_hot_markers",
            start_frame=None,
            end_frame=None,
            marker_filter=None,
            match_mode=None,
            sort_by=None,
            top_n=None,
            max_frames=None,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_hot_markers"}


def test_export_profile_tables_forwards_all_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="export_profile_tables",
            output_dir="/tmp/unity-profiler-export",
            start_frame=1000,
            end_frame=1300,
            thread_name="Main Thread",
            thread_index=0,
            include_frame_table=True,
            include_marker_table=False,
            max_frames=2000,
            max_marker_rows=20000,
            overwrite=True,
            execution_mode="async",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "export_profile_tables",
        "output_dir": "/tmp/unity-profiler-export",
        "start_frame": 1000,
        "end_frame": 1300,
        "thread_name": "Main Thread",
        "thread_index": 0,
        "include_frame_table": True,
        "include_marker_table": False,
        "max_frames": 2000,
        "max_marker_rows": 20000,
        "overwrite": True,
        "execution_mode": "async",
    }


def test_export_profile_tables_omits_none_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="export_profile_tables",
            output_dir=None,
            start_frame=None,
            end_frame=None,
            thread_name=None,
            thread_index=None,
            include_frame_table=None,
            include_marker_table=None,
            max_frames=None,
            max_marker_rows=None,
            overwrite=None,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "export_profile_tables"}


def test_profiler_job_status_forwards_job_id(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_job_status", job_id="job-123")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "profiler_job_status", "job_id": "job-123"}


def test_profiler_job_cancel_forwards_job_id(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_job_cancel", job_id="job-123")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "profiler_job_cancel", "job_id": "job-123"}


def test_memory_take_snapshot_forwards_path(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="memory_take_snapshot", snapshot_path="/tmp/snap.snap")
    )
    assert result["success"] is True
    assert mock_unity["params"]["snapshot_path"] == "/tmp/snap.snap"


def test_memory_compare_forwards_both_paths(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="memory_compare_snapshots",
            snapshot_a="/tmp/a.snap", snapshot_b="/tmp/b.snap",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["snapshot_a"] == "/tmp/a.snap"
    assert mock_unity["params"]["snapshot_b"] == "/tmp/b.snap"


def test_frame_debugger_get_events_forwards_paging(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="frame_debugger_get_events",
            page_size=25, cursor=50,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["page_size"] == 25
    assert mock_unity["params"]["cursor"] == 50


def test_frame_debugger_get_event_details_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="frame_debugger_get_event_details",
            event_index=11,
            include_shader_properties=True,
            max_shader_properties=128,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "frame_debugger_get_event_details",
        "event_index": 11,
        "include_shader_properties": True,
        "max_shader_properties": 128,
    }


def test_frame_debugger_capture_event_output_forwards_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="frame_debugger_capture_event_output",
            event_index=11,
            output_path="/tmp/unity-frame-debugger/event-11.png",
            include_base64=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "frame_debugger_capture_event_output",
        "event_index": 11,
        "output_path": "/tmp/unity-frame-debugger/event-11.png",
        "include_base64": True,
    }


def test_frame_debugger_detail_and_output_omit_none_params(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(),
            action="frame_debugger_get_event_details",
            event_index=11,
            include_shader_properties=None,
            max_shader_properties=None,
            output_path=None,
            include_base64=None,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"] == {
        "action": "frame_debugger_get_event_details",
        "event_index": 11,
    }


def test_action_only_params_no_extras(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_stop")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "profiler_stop"}


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="Get_Frame_Timing")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_frame_timing"


def test_action_uppercase(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="PROFILER_STATUS")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "profiler_status"


# ---------------------------------------------------------------------------
# Non-dict response wrapped
# ---------------------------------------------------------------------------

def test_non_dict_response_wrapped(monkeypatch):
    monkeypatch.setattr(
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_frame_timing")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]


# ---------------------------------------------------------------------------
# Tool registration
# ---------------------------------------------------------------------------

def test_tool_registered_with_profiling_group():
    from services.registry.tool_registry import _tool_registry

    profiler_tools = [
        t for t in _tool_registry if t.get("name") == "manage_profiler"
    ]
    assert len(profiler_tools) == 1
    assert profiler_tools[0]["group"] == "profiling"


def test_tool_description_mentions_recorded_frame_actions():
    from services.registry.tool_registry import _tool_registry

    profiler_tool = next(t for t in _tool_registry if t.get("name") == "manage_profiler")
    description = profiler_tool["description"]
    assert "get_frame_summary" in description
    assert "get_hot_markers" in description
    assert "find_marker" in description
    assert "export_profile_tables" in description
    assert "profiler_job_status" in description
    assert "profiler_job_cancel" in description
    assert "execution_mode" in description
    assert "frameTime.csv" in description
    assert "markerTable.csv" in description
    assert "not CSV contents" in description
    assert "frame_debugger_get_events" in description
    assert "frame_debugger_get_event_details" in description
    assert "frame_debugger_capture_event_output" in description
    assert "event_index" in description
    assert "Frame Debugger Details screenshots" in description
    assert "outside Assets" in description
