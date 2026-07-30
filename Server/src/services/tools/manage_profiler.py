from typing import Annotated, Any, Literal, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

SESSION_ACTIONS = [
    "profiler_start", "profiler_stop", "profiler_status", "profiler_set_areas",
]

COUNTER_ACTIONS = [
    "get_frame_timing", "get_counters", "get_object_memory", "get_marker_calltree",
    "get_frame_summary", "get_hot_markers", "find_marker", "export_profile_tables",
    "profiler_job_status", "profiler_job_cancel",
]

MEMORY_SNAPSHOT_ACTIONS = [
    "memory_take_snapshot", "memory_list_snapshots", "memory_compare_snapshots",
]

FRAME_DEBUGGER_ACTIONS = [
    "frame_debugger_enable", "frame_debugger_disable", "frame_debugger_get_events",
]

UTILITY_ACTIONS = ["ping"]

ALL_ACTIONS = (
    UTILITY_ACTIONS + SESSION_ACTIONS + COUNTER_ACTIONS
    + MEMORY_SNAPSHOT_ACTIONS + FRAME_DEBUGGER_ACTIONS
)

ProfilerAction = Literal[
    "ping",
    "profiler_start",
    "profiler_stop",
    "profiler_status",
    "profiler_set_areas",
    "get_frame_timing",
    "get_counters",
    "get_object_memory",
    "get_marker_calltree",
    "get_frame_summary",
    "get_hot_markers",
    "find_marker",
    "export_profile_tables",
    "profiler_job_status",
    "profiler_job_cancel",
    "memory_take_snapshot",
    "memory_list_snapshots",
    "memory_compare_snapshots",
    "frame_debugger_enable",
    "frame_debugger_disable",
    "frame_debugger_get_events",
]

MarkerMatchMode = Literal["contains", "exact", "regex"]
ProfilerMarkerSortBy = Literal[
    "total_time", "self_time", "max_total_time", "max_self_time", "call_count", "frame_count",
]


@mcp_for_unity_tool(
    group="profiling",
    description=(
        "Unity Profiler session control, counter reads, recorded CPU frame/marker analysis, "
        "memory snapshots, and Frame Debugger.\n\n"
        "SESSION:\n"
        "- profiler_start: Enable profiler, optionally record to .raw file (log_file, enable_callstacks)\n"
        "- profiler_stop: Disable profiler, stop recording\n"
        "- profiler_status: Get enabled state, active areas, recording path\n"
        "- profiler_set_areas: Toggle ProfilerAreas on/off (areas dict)\n\n"
        "COUNTERS:\n"
        "- get_frame_timing: FrameTimingManager data (12 fields, synchronous)\n"
        "- get_counters: Generic counter read by category + optional counter names (async, 1-frame wait)\n"
        "- get_object_memory: Memory size of a specific object by path\n"
        "- get_marker_calltree: Recorded CPU hierarchy for a marker in an existing profiler frame "
        "(frame_index, marker_filter; optional thread_name/thread_index, match_mode, max_depth, max_rows)\n\n"
        "RECORDED CPU ANALYSIS:\n"
        "- get_frame_summary: Recorded-frame timing statistics and worst frames "
        "(optional start_frame/end_frame, thread_name/thread_index, top_n, max_frames)\n"
        "- get_hot_markers: Ranked marker statistics across recorded frames "
        "(optional start_frame/end_frame, thread_name/thread_index, marker_filter, match_mode, sort_by, top_n, max_frames, execution_mode)\n"
        "- find_marker: Recorded frames/threads where a marker appears "
        "(marker_filter; optional start_frame/end_frame, thread_name/thread_index, match_mode, top_n, max_frames, execution_mode)\n"
        "- export_profile_tables: Export Profile Analyzer-style CSV files from the recorded Profiler buffer "
        "(frameTime.csv and markerTable.csv; optional output_dir, start_frame/end_frame, thread_name/thread_index, "
        "include_frame_table/include_marker_table, max_frames, max_marker_rows, overwrite, execution_mode). "
        "Returns paths, columns, row counts, ranges, thread filters, and truncation flags, not CSV contents.\n"
        "- profiler_job_status: Poll a pending broad profiler analysis/export job by job_id.\n"
        "- profiler_job_cancel: Cancel a queued/running profiler analysis/export job by job_id.\n"
        "For get_hot_markers, find_marker, and export_profile_tables, execution_mode='auto' starts a Unity-side "
        "profiler job for broad scans. If a response has _mcp_status='pending', poll with action='profiler_job_status' "
        "and the returned job_id.\n\n"
        "MEMORY SNAPSHOT (requires com.unity.memoryprofiler):\n"
        "- memory_take_snapshot: Capture memory snapshot to file\n"
        "- memory_list_snapshots: List available .snap files\n"
        "- memory_compare_snapshots: Compare two snapshot files\n\n"
        "FRAME DEBUGGER:\n"
        "- frame_debugger_enable: Turn on Frame Debugger, report event count\n"
        "- frame_debugger_disable: Turn off Frame Debugger\n"
        "- frame_debugger_get_events: Get draw call events (paged, best-effort via reflection)"
    ),
    annotations=ToolAnnotations(
        title="Manage Profiler",
        destructiveHint=False,
        readOnlyHint=False,
    ),
)
async def manage_profiler(
    ctx: Context,
    action: Annotated[ProfilerAction, "The profiler action to perform."],
    category: Annotated[Optional[str], "Profiler category name for get_counters (e.g. Render, Scripts, Memory, Physics)."] = None,
    counters: Annotated[Optional[list[str]], "Specific counter names for get_counters. Omit to read all in category."] = None,
    object_path: Annotated[Optional[str], "Scene hierarchy or asset path for get_object_memory."] = None,
    frame_index: Annotated[Optional[int], "Required for get_marker_calltree. Existing Unity Profiler frame index to inspect."] = None,
    start_frame: Annotated[Optional[int], "Optional first recorded Profiler frame for get_frame_summary, get_hot_markers, find_marker, or export_profile_tables. Defaults to the first available recorded frame."] = None,
    end_frame: Annotated[Optional[int], "Optional last recorded Profiler frame for get_frame_summary, get_hot_markers, find_marker, or export_profile_tables. Defaults to the last available recorded frame."] = None,
    marker_filter: Annotated[Optional[str], "Marker name or pattern. Required for get_marker_calltree and find_marker; optional for get_hot_markers."] = None,
    thread_name: Annotated[Optional[str], "Optional thread name for recorded CPU hierarchy analysis and marker-table export, e.g. Main Thread. If omitted with thread_index, all available threads are searched."] = None,
    thread_index: Annotated[Optional[int], "Optional profiler thread index for recorded CPU hierarchy analysis and marker-table export. If omitted with thread_name, all available threads are searched."] = None,
    output_dir: Annotated[Optional[str], "Optional directory for export_profile_tables CSV output. Defaults to a unique OS temp directory outside the Unity project and Assets. Directories inside Assets are rejected."] = None,
    include_frame_table: Annotated[Optional[bool], "Whether export_profile_tables writes frameTime.csv. Default: true."] = None,
    include_marker_table: Annotated[Optional[bool], "Whether export_profile_tables writes markerTable.csv. Default: true."] = None,
    max_marker_rows: Annotated[Optional[int], "Maximum marker rows to write for export_profile_tables. Unity truncates and reports truncation when exceeded. Default: 20000."] = None,
    overwrite: Annotated[Optional[bool], "Whether export_profile_tables overwrites existing frameTime.csv/markerTable.csv in output_dir. Default: false; existing files cause a suffixed output directory."] = None,
    match_mode: Annotated[Optional[MarkerMatchMode], "Marker matching mode for recorded CPU marker queries: contains, exact, or regex. Default: contains. Regex patterns are evaluated with a bounded per-match timeout; a pattern that exceeds it fails with an error instead of stalling the Editor."] = None,
    sort_by: Annotated[Optional[ProfilerMarkerSortBy], "Sort for get_hot_markers: total_time, self_time, max_total_time, max_self_time, call_count, or frame_count. Default: total_time."] = None,
    top_n: Annotated[Optional[int], "Maximum rows to return for get_frame_summary worst frames, get_hot_markers, or find_marker. Unity clamps to a sane upper bound."] = None,
    max_frames: Annotated[Optional[int], "Maximum recorded frames to scan for broad profiler queries and export_profile_tables. Unity clamps to a sane upper bound and reports truncated=true when reached."] = None,
    execution_mode: Annotated[Optional[Literal["auto", "sync", "async"]], "Execution mode for broad recorded-frame scans. auto queues a Unity-side job when the scan is broad; sync forces a single response; async always returns a job_id to poll with profiler_job_status."] = None,
    job_id: Annotated[Optional[str], "Profiler analysis job id returned by a pending get_hot_markers, find_marker, or export_profile_tables call. Required for profiler_job_status and profiler_job_cancel."] = None,
    max_depth: Annotated[Optional[int], "Maximum child subtree depth for get_marker_calltree. Default: 8; Unity clamps to a sane upper bound."] = None,
    max_rows: Annotated[Optional[int], "Maximum returned marker rows for get_marker_calltree. Default: 200; Unity clamps to a sane upper bound."] = None,
    include_parents: Annotated[Optional[bool], "Whether get_marker_calltree includes the marker parent chain. Default: true."] = None,
    include_children: Annotated[Optional[bool], "Whether get_marker_calltree includes the child subtree. Default: true."] = None,
    log_file: Annotated[Optional[str], "Path to .raw file for profiler_start recording."] = None,
    enable_callstacks: Annotated[Optional[bool], "Enable allocation callstacks for profiler_start."] = None,
    areas: Annotated[Optional[dict[str, bool]], "Dict of area name to bool for profiler_set_areas."] = None,
    snapshot_path: Annotated[Optional[str], "Output path for memory_take_snapshot."] = None,
    search_path: Annotated[Optional[str], "Search directory for memory_list_snapshots."] = None,
    snapshot_a: Annotated[Optional[str], "First snapshot path for memory_compare_snapshots."] = None,
    snapshot_b: Annotated[Optional[str], "Second snapshot path for memory_compare_snapshots."] = None,
    page_size: Annotated[Optional[int], "Page size for frame_debugger_get_events (default 50)."] = None,
    cursor: Annotated[Optional[int], "Cursor offset for frame_debugger_get_events."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_lower}

    param_map = {
        "category": category, "counters": counters,
        "object_path": object_path,
        "frame_index": frame_index,
        "start_frame": start_frame, "end_frame": end_frame,
        "marker_filter": marker_filter,
        "thread_name": thread_name, "thread_index": thread_index,
        "output_dir": output_dir,
        "include_frame_table": include_frame_table, "include_marker_table": include_marker_table,
        "max_marker_rows": max_marker_rows, "overwrite": overwrite,
        "match_mode": match_mode, "sort_by": sort_by, "top_n": top_n, "max_frames": max_frames,
        "execution_mode": execution_mode, "job_id": job_id,
        "max_depth": max_depth, "max_rows": max_rows,
        "include_parents": include_parents, "include_children": include_children,
        "log_file": log_file, "enable_callstacks": enable_callstacks,
        "areas": areas,
        "snapshot_path": snapshot_path, "search_path": search_path,
        "snapshot_a": snapshot_a, "snapshot_b": snapshot_b,
        "page_size": page_size, "cursor": cursor,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_profiler", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
