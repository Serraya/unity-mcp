---
title: manage_profiler
sidebar_label: manage_profiler
description: "Unity Profiler session control, counter reads, recorded CPU frame/marker analysis, memory snapshots, and Frame Debugger."
---

# `manage_profiler`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `profiling` &nbsp;·&nbsp; **Module:** `services.tools.manage_profiler`

## Description

Unity Profiler session control, counter reads, recorded CPU frame/marker analysis, memory snapshots, and Frame Debugger.

SESSION:
- profiler_start: Enable profiler, optionally record to .raw file (log_file, enable_callstacks)
- profiler_stop: Disable profiler, stop recording
- profiler_status: Get enabled state, active areas, recording path
- profiler_set_areas: Toggle ProfilerAreas on/off (areas dict)

COUNTERS:
- get_frame_timing: FrameTimingManager data (12 fields, synchronous)
- get_counters: Generic counter read by category + optional counter names (async, 1-frame wait)
- get_object_memory: Memory size of a specific object by path
- get_marker_calltree: Recorded CPU hierarchy for a marker in an existing profiler frame (frame_index, marker_filter; optional thread_name/thread_index, match_mode, max_depth, max_rows)

RECORDED CPU ANALYSIS:
- get_frame_summary: Recorded-frame timing statistics and worst frames (optional start_frame/end_frame, thread_name/thread_index, top_n, max_frames)
- get_hot_markers: Ranked marker statistics across recorded frames (optional start_frame/end_frame, thread_name/thread_index, marker_filter, match_mode, sort_by, top_n, max_frames)
- find_marker: Recorded frames/threads where a marker appears (marker_filter; optional start_frame/end_frame, thread_name/thread_index, match_mode, top_n, max_frames)
- export_profile_tables: Export Profile Analyzer-style CSV files from the recorded Profiler buffer (frameTime.csv and markerTable.csv; optional output_dir, start_frame/end_frame, thread_name/thread_index, include_frame_table/include_marker_table, max_frames, max_marker_rows, overwrite). Returns paths, columns, row counts, ranges, thread filters, and truncation flags, not CSV contents.

MEMORY SNAPSHOT (requires com.unity.memoryprofiler):
- memory_take_snapshot: Capture memory snapshot to file
- memory_list_snapshots: List available .snap files
- memory_compare_snapshots: Compare two snapshot files

FRAME DEBUGGER:
- frame_debugger_enable: Turn on Frame Debugger, report event count
- frame_debugger_disable: Turn off Frame Debugger
- frame_debugger_get_events: Get draw call events (paged, best-effort via reflection)

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['ping', 'profiler_start', 'profiler_stop', 'profiler_status', 'profiler_set_areas', 'get_frame_timing', 'get_counters', 'get_object_memory', 'get_marker_calltree', 'get_frame_summary', 'get_hot_markers', 'find_marker', 'export_profile_tables', 'memory_take_snapshot', 'memory_list_snapshots', 'memory_compare_snapshots', 'frame_debugger_enable', 'frame_debugger_disable', 'frame_debugger_get_events']` | yes | The profiler action to perform. |
| `category` | `str \| None` | — | Profiler category name for get_counters (e.g. Render, Scripts, Memory, Physics). |
| `counters` | `list[str] \| None` | — | Specific counter names for get_counters. Omit to read all in category. |
| `object_path` | `str \| None` | — | Scene hierarchy or asset path for get_object_memory. |
| `frame_index` | `int \| None` | — | Required for get_marker_calltree. Existing Unity Profiler frame index to inspect. |
| `start_frame` | `int \| None` | — | Optional first recorded Profiler frame for get_frame_summary, get_hot_markers, find_marker, or export_profile_tables. Defaults to the first available recorded frame. |
| `end_frame` | `int \| None` | — | Optional last recorded Profiler frame for get_frame_summary, get_hot_markers, find_marker, or export_profile_tables. Defaults to the last available recorded frame. |
| `marker_filter` | `str \| None` | — | Marker name or pattern. Required for get_marker_calltree and find_marker; optional for get_hot_markers. |
| `thread_name` | `str \| None` | — | Optional thread name for recorded CPU hierarchy analysis and marker-table export, e.g. Main Thread. If omitted with thread_index, all available threads are searched. |
| `thread_index` | `int \| None` | — | Optional profiler thread index for recorded CPU hierarchy analysis and marker-table export. If omitted with thread_name, all available threads are searched. |
| `output_dir` | `str \| None` | — | Optional directory for export_profile_tables CSV output. Defaults to a unique OS temp directory outside the Unity project and Assets. Directories inside Assets are rejected. |
| `include_frame_table` | `bool \| None` | — | Whether export_profile_tables writes frameTime.csv. Default: true. |
| `include_marker_table` | `bool \| None` | — | Whether export_profile_tables writes markerTable.csv. Default: true. |
| `max_marker_rows` | `int \| None` | — | Maximum marker rows to write for export_profile_tables. Unity truncates and reports truncation when exceeded. Default: 20000. |
| `overwrite` | `bool \| None` | — | Whether export_profile_tables overwrites existing frameTime.csv/markerTable.csv in output_dir. Default: false; existing files cause a suffixed output directory. |
| `match_mode` | `Literal['contains', 'exact', 'regex'] \| None` | — | Marker matching mode for recorded CPU marker queries: contains, exact, or regex. Default: contains. |
| `sort_by` | `Literal['total_time', 'self_time', 'max_total_time', 'max_self_time', 'call_count', 'frame_count'] \| None` | — | Sort for get_hot_markers: total_time, self_time, max_total_time, max_self_time, call_count, or frame_count. Default: total_time. |
| `top_n` | `int \| None` | — | Maximum rows to return for get_frame_summary worst frames, get_hot_markers, or find_marker. Unity clamps to a sane upper bound. |
| `max_frames` | `int \| None` | — | Maximum recorded frames to scan for broad profiler queries and export_profile_tables. Unity clamps to a sane upper bound and reports truncated=true when reached. |
| `max_depth` | `int \| None` | — | Maximum child subtree depth for get_marker_calltree. Default: 8; Unity clamps to a sane upper bound. |
| `max_rows` | `int \| None` | — | Maximum returned marker rows for get_marker_calltree. Default: 200; Unity clamps to a sane upper bound. |
| `include_parents` | `bool \| None` | — | Whether get_marker_calltree includes the marker parent chain. Default: true. |
| `include_children` | `bool \| None` | — | Whether get_marker_calltree includes the child subtree. Default: true. |
| `log_file` | `str \| None` | — | Path to .raw file for profiler_start recording. |
| `enable_callstacks` | `bool \| None` | — | Enable allocation callstacks for profiler_start. |
| `areas` | `dict[str, bool] \| None` | — | Dict of area name to bool for profiler_set_areas. |
| `snapshot_path` | `str \| None` | — | Output path for memory_take_snapshot. |
| `search_path` | `str \| None` | — | Search directory for memory_list_snapshots. |
| `snapshot_a` | `str \| None` | — | First snapshot path for memory_compare_snapshots. |
| `snapshot_b` | `str \| None` | — | Second snapshot path for memory_compare_snapshots. |
| `page_size` | `int \| None` | — | Page size for frame_debugger_get_events (default 50). |
| `cursor` | `int \| None` | — | Cursor offset for frame_debugger_get_events. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

