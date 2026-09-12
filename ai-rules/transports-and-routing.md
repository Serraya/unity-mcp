# Transports And Routing Rules

Load this when work touches stdio, HTTP, WebSocket, legacy Unity connection, instance selection, tool visibility, remote-hosted mode, or multi-user behavior.

## Transport Surfaces

- Stdio starts a Python server process per MCP client and talks to Unity through the legacy TCP bridge.
- HTTP runs a shared Python server and talks to Unity plugin sessions through the WebSocket hub.
- Some features have separate stdio sync and HTTP/WebSocket push paths. Keep their metadata and behavior aligned.

## Instance Routing

- Unity instances are selected by `Name@hash`, hash prefix, or stdio port depending on context.
- A project-local stdio client should pass `--project-path`. That scope is a hard transport boundary: discovery must filter status metadata before opening any probe socket, and calls must not escape to another project.
- Do not pair project-local Unity MCP configuration with an unscoped global Unity MCP server, or expose overlapping Unity MCP implementations to the same client by default. One client/project pair has one capability owner.
- `set_active_instance` stores client/session routing state.
- Inline `unity_instance` parameters route a single call and must not silently mutate global session state.
- Remote-hosted mode requires explicit user/session isolation. Do not add shortcuts that bypass user identity.

## Tool Visibility

- Unity tool states and server FastMCP visibility are separate concerns.
- `manage_tools sync` relies on Unity `get_tool_states` in stdio mode.
- HTTP/WebSocket registration receives `register_tools` messages from Unity.
- Notify MCP clients of changed tool lists only after the server-side registration state is updated.

## Diagnostics

- Keep process presence, transport reachability, current state and operation permission separate. `unity_status.success` means diagnostic discovery succeeded; require a successful nested Editor query and fresh, identity-verified `advice.ready_for_tools=true` before proceeding. Missing/null, malformed, stale or wrong-project state cannot authorize tools. Preserve failures; do not promote cached healthy state to current.
- Empty MCP/Pipeline discovery is not proof the Editor is closed. First use the installed CLI's supported `unity editors running --json`, independently of Pipeline endpoint discovery. Match the canonical exact `projectPath` and PID, not a display name or a sibling checkout. If process inspection fails, report presence as unknown. If it finds the target, report: "The target Editor is running, but its automation connection is unavailable; current readiness is unknown."
- Retry discovery/state reads at most once after a bounded wait using existing mechanisms. State compilation/import/reload only when observed, not as an explanation for every timeout. Do not start/restart an Editor, change Play Mode, fabricate a discovery entry or reset configuration. Ask for intervention only for a named unresolved step. An absent target requires a successful independent process query before asking the user to open it.
- Keep MCP and Pipeline health distinct: a recovered exact-project MCP operation is not blocked by Pipeline discovery, and does not close Pipeline recovery. A timed-out mutation has an unknown outcome; inspect the existing job/result before any resubmission. Discovery retries are not mutation retries.
- A group listing proves group visibility, not that custom tools are absent.
- For custom tools, verify both metadata registration and callable routing.
- Do not treat a client cache problem as a Unity discovery problem without checking server registration and `tools/list` behavior.
