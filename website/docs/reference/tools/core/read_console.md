---
title: read_console
sidebar_label: read_console
description: "Gets messages from or clears the Unity Editor console."
---

# `read_console`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.read_console`

## Description

Gets messages from or clears the Unity Editor console. Defaults to 10 most recent error/warning entries. Pass types=['all'] to include logs. Use page_size/cursor for paging; paged responses report items plus cursor/pageSize/nextCursor/truncated and 'total' — the exact number of entries matching the requested types/filter across all pages. Note: For maximum client compatibility, pass count as a quoted string (e.g., '5'). The 'get' action is read-only; 'clear' modifies ephemeral UI state (not project data).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['get', 'clear'] \| None` | — | Get or clear the Unity Editor console. Defaults to 'get' if omitted. |
| `types` | `list[Literal['error', 'warning', 'log', 'all']] \| str \| None` | — | Message types to get (accepts list or JSON string) |
| `count` | `int \| str \| None` | — | Max messages to return in non-paging mode (accepts int or string, e.g., 5 or '5'). Ignored when paging with page_size/cursor. |
| `filter_text` | `str \| None` | — | Text filter for messages |
| `page_size` | `int \| str \| None` | — | Page size for paginated console reads. Defaults to 50 when omitted. |
| `cursor` | `int \| str \| None` | — | Opaque cursor for paging (0-based offset). Defaults to 0. |
| `format` | `Literal['plain', 'detailed', 'json'] \| None` | — | Output format |
| `include_stacktrace` | `bool \| str \| None` | — | Include complete, untruncated stack traces with format='detailed' or 'json' (accepts true/false or 'true'/'false'). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Inspect a relevant causal stack

```json
{
  "action": "get",
  "types": ["error", "warning"],
  "count": "10",
  "filter_text": "ExampleComponent",
  "format": "detailed",
  "include_stacktrace": true
}
```

Replace the illustrative filter with the actual diagnostic scope. `"json"` also
returns structured entries with stacks; `"plain"` does not. The stack-limit
parameters `max_stack_frames` and `max_stack_chars` have been removed. Complete
returned stacks retain what Unity captured, not frames absent from that capture.

### Read all message types with pagination

```json
{
  "action": "get",
  "types": ["all"],
  "page_size": 50,
  "cursor": 0,
  "format": "detailed",
  "include_stacktrace": false
}
```

On success, inspect `data.items` and pass `data.nextCursor` as the next request's
`cursor`, retaining the same query, until `nextCursor` is null. This example has
no text filter and includes ordinary logs/breadcrumbs. `count` is ignored in
paging mode; `count="all"` and `count="*"` are not full-fetch recipes.

An omitted query is capped at ten errors/warnings, not a complete Console read;
do not rely on an unverified newest-first ordering. `total` describes matches
for that read, not a frozen snapshot across requests. If the Console buffer
changes between pages, qualify cross-page completeness. A targeted absence
claim covers only the selected types/filter and observed buffer.

For a causal stack, request it on the relevant entries using the first example.
If client output is cut off, narrow the entry selection or reduce `page_size`
and reread the affected page rather than discard frames. If even one entry
cannot be fully delivered, report the retrieval limit. A full Console audit
does not require stacks on every entry. Reading does not authorize clearing;
retain the consuming project's instance-routing and read/clear approval gates.
<!-- examples:end -->

