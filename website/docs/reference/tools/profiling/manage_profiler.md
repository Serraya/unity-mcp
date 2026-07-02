---
title: manage_profiler
sidebar_label: manage_profiler
description: "Unity Profiler session control, counter reads, CPU marker call trees, memory snapshots, and Frame Debugger."
---

# `manage_profiler`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `profiling` &nbsp;·&nbsp; **Module:** `services.tools.manage_profiler`

## Description

Unity Profiler session control, counter reads, CPU marker call trees, memory snapshots, and Frame Debugger.

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
| `action` | `Literal['ping', 'profiler_start', 'profiler_stop', 'profiler_status', 'profiler_set_areas', 'get_frame_timing', 'get_counters', 'get_object_memory', 'get_marker_calltree', 'memory_take_snapshot', 'memory_list_snapshots', 'memory_compare_snapshots', 'frame_debugger_enable', 'frame_debugger_disable', 'frame_debugger_get_events']` | yes | The profiler action to perform. |
| `category` | `str \| None` | — | Profiler category name for get_counters (e.g. Render, Scripts, Memory, Physics). |
| `counters` | `list[str] \| None` | — | Specific counter names for get_counters. Omit to read all in category. |
| `object_path` | `str \| None` | — | Scene hierarchy or asset path for get_object_memory. |
| `frame_index` | `int \| None` | — | Required for get_marker_calltree. Existing Unity Profiler frame index to inspect. |
| `marker_filter` | `str \| None` | — | Required for get_marker_calltree. Marker name or pattern to find in the recorded CPU hierarchy. |
| `thread_name` | `str \| None` | — | Optional thread name for get_marker_calltree, e.g. Main Thread. If omitted with thread_index, all available threads are searched. |
| `thread_index` | `int \| None` | — | Optional profiler thread index for get_marker_calltree. If omitted with thread_name, all available threads are searched. |
| `match_mode` | `Literal['contains', 'exact', 'regex'] \| None` | — | Marker matching mode for get_marker_calltree: contains, exact, or regex. Default: contains. |
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

