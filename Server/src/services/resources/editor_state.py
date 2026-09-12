import os
import time
from copy import deepcopy
from typing import Any

from fastmcp import Context
from pydantic import BaseModel, ValidationError

from core.config import config
from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from services.state.external_changes_scanner import external_changes_scanner
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.legacy.port_discovery import PortDiscovery
from transport.plugin_hub import PluginHub


class EditorStateUnity(BaseModel):
    instance_id: str | None = None
    unity_version: str | None = None
    project_id: str | None = None
    platform: str | None = None
    is_batch_mode: bool | None = None


class EditorStatePlayMode(BaseModel):
    is_playing: bool | None = None
    is_paused: bool | None = None
    is_changing: bool | None = None


class EditorStateActiveScene(BaseModel):
    path: str | None = None
    guid: str | None = None
    name: str | None = None


class EditorStateEditor(BaseModel):
    is_focused: bool | None = None
    play_mode: EditorStatePlayMode | None = None
    active_scene: EditorStateActiveScene | None = None


class EditorStateActivity(BaseModel):
    phase: str | None = None
    since_unix_ms: int | None = None
    reasons: list[str] | None = None


class EditorStateCompilation(BaseModel):
    is_compiling: bool | None = None
    is_domain_reload_pending: bool | None = None
    last_compile_started_unix_ms: int | None = None
    last_compile_finished_unix_ms: int | None = None
    last_domain_reload_before_unix_ms: int | None = None
    last_domain_reload_after_unix_ms: int | None = None


class EditorStateRefresh(BaseModel):
    is_refresh_in_progress: bool | None = None
    last_refresh_requested_unix_ms: int | None = None
    last_refresh_finished_unix_ms: int | None = None


class EditorStateAssets(BaseModel):
    is_updating: bool | None = None
    external_changes_dirty: bool | None = None
    external_changes_last_seen_unix_ms: int | None = None
    external_changes_dirty_since_unix_ms: int | None = None
    external_changes_last_cleared_unix_ms: int | None = None
    refresh: EditorStateRefresh | None = None


class EditorStateLastRun(BaseModel):
    finished_unix_ms: int | None = None
    result: str | None = None
    counts: Any | None = None


class EditorStateTests(BaseModel):
    is_running: bool | None = None
    mode: str | None = None
    current_job_id: str | None = None
    started_unix_ms: int | None = None
    started_by: str | None = None
    last_run: EditorStateLastRun | None = None


class EditorStateTransport(BaseModel):
    unity_bridge_connected: bool | None = None
    last_message_unix_ms: int | None = None


class EditorStateSettings(BaseModel):
    batch_execute_max_commands: int | None = None


class EditorStateAdvice(BaseModel):
    ready_for_tools: bool | None = None
    blocking_reasons: list[str] | None = None
    recommended_retry_after_ms: int | None = None
    recommended_next_action: str | None = None


class EditorStateStaleness(BaseModel):
    age_ms: int | None = None
    is_stale: bool | None = None


class EditorStateData(BaseModel):
    schema_version: str
    observed_at_unix_ms: int
    sequence: int
    unity: EditorStateUnity | None = None
    editor: EditorStateEditor | None = None
    activity: EditorStateActivity | None = None
    compilation: EditorStateCompilation | None = None
    assets: EditorStateAssets | None = None
    tests: EditorStateTests | None = None
    transport: EditorStateTransport | None = None
    settings: EditorStateSettings | None = None
    advice: EditorStateAdvice | None = None
    staleness: EditorStateStaleness | None = None


def _now_unix_ms() -> int:
    return int(time.time() * 1000)


def _in_pytest() -> bool:
    # Avoid instance-discovery side effects during the Python integration test suite.
    return bool(os.environ.get("PYTEST_CURRENT_TEST"))


async def infer_single_instance_id(ctx: Context) -> str | None:
    """
    Best-effort: if exactly one Unity instance is connected, return its Name@hash id.
    This makes editor_state outputs self-describing even when no explicit active instance is set.
    """
    await ctx.info("If exactly one Unity instance is connected, return its Name@hash id.")

    transport = (config.transport_mode or "stdio").lower()

    if transport == "http":
        # HTTP/WebSocket transport: derive from PluginHub sessions.
        try:
            # In remote-hosted mode, filter sessions by user_id
            user_id = (await ctx.get_state(
                "user_id")) if config.http_remote_hosted else None
            sessions_data = await PluginHub.get_sessions(user_id=user_id)
            sessions = sessions_data.sessions if hasattr(
                sessions_data, "sessions") else {}
            if isinstance(sessions, dict) and len(sessions) == 1:
                session = next(iter(sessions.values()))
                project = getattr(session, "project", None)
                project_hash = getattr(session, "hash", None)
                if project and project_hash:
                    return f"{project}@{project_hash}"
        except Exception:
            return None
        return None

    # Stdio/TCP transport: derive from connection pool discovery.
    try:
        from transport.legacy.unity_connection import get_unity_connection_pool

        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances(force_refresh=False)
        if isinstance(instances, list) and len(instances) == 1:
            inst = instances[0]
            inst_id = getattr(inst, "id", None)
            return str(inst_id) if inst_id else None
    except Exception:
        return None
    return None


def _enrich_advice_and_staleness(
    state_v2: dict[str, Any], *, identity_verified: bool = True,
) -> dict[str, Any]:
    now_ms = _now_unix_ms()
    observed_ms = state_v2["observed_at_unix_ms"]

    age_ms = max(0, now_ms - observed_ms)
    # Conservative default: treat >2s as stale (covers common unfocused-editor throttling).
    is_stale = age_ms > 2000

    compilation = state_v2.get("compilation") or {}
    tests = state_v2.get("tests") or {}
    assets = state_v2.get("assets") or {}
    refresh = (assets.get("refresh") or {}) if isinstance(assets, dict) else {}
    editor = state_v2.get("editor") or {}
    play_mode = editor.get("play_mode") or {}
    phase = (state_v2.get("activity") or {}).get("phase")
    unity = state_v2.get("unity") or {}

    # Unknown is not False: these observations are required before granting readiness.
    required_flags = (
        compilation.get("is_compiling"), compilation.get("is_domain_reload_pending"),
        assets.get("is_updating"), refresh.get("is_refresh_in_progress"),
        tests.get("is_running"), play_mode.get("is_playing"),
        play_mode.get("is_paused"), play_mode.get("is_changing"),
    )
    known_phases = {"idle", "compiling", "domain_reload", "running_tests",
                    "asset_import", "playmode_transition"}
    missing_state = (any(type(flag) is not bool for flag in required_flags)
                     or phase not in known_phases or not unity.get("instance_id"))

    blocking: list[str] = []
    if not identity_verified:
        blocking.append("unknown_editor_identity")
    if missing_state:
        blocking.append("unknown_editor_state")
    if compilation.get("is_compiling") is True:
        blocking.append("compiling")
    if compilation.get("is_domain_reload_pending") is True:
        blocking.append("domain_reload")
    if tests.get("is_running") is True:
        blocking.append("running_tests")
    if refresh.get("is_refresh_in_progress") is True:
        blocking.append("asset_refresh")
    if assets.get("is_updating") is True:
        blocking.append("asset_import")
    if play_mode.get("is_changing") is True:
        blocking.append("playmode_transition")
    if phase in known_phases and phase != "idle" and phase not in blocking:
        blocking.append(phase)
    if is_stale:
        blocking.append("stale_status")

    ready_for_tools = None if missing_state or not identity_verified or is_stale else not blocking

    state_v2["advice"] = {
        "ready_for_tools": ready_for_tools,
        "blocking_reasons": blocking,
        "recommended_retry_after_ms": 0 if ready_for_tools else 500,
        "recommended_next_action": "none" if ready_for_tools else "retry_later",
    }
    state_v2["staleness"] = {"age_ms": age_ms, "is_stale": is_stale}
    return state_v2


@mcp_for_unity_resource(
    uri="mcpforunity://editor/state",
    name="editor_state",
    description="Canonical Editor readiness snapshot. success means the Editor query returned a valid snapshot, not that tools may run. Require advice.ready_for_tools=true; null means state/identity/freshness is unknown. Failed queries retain their failure and never authorize tools.\n\nURI: mcpforunity://editor/state",
)
async def get_editor_state(ctx: Context) -> MCPResponse:
    unity_instance = await get_unity_instance_from_context(ctx)
    if not unity_instance:
        unity_instance = await infer_single_instance_id(ctx)

    try:
        response = await unity_transport.send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "get_editor_state", {},
        )
    except Exception as exc:
        # Transport implementations may raise or return MCPResponse on disconnect.
        return MCPResponse(success=False, error=str(exc) or type(exc).__name__, hint="retry")

    if isinstance(response, MCPResponse):
        response = response.model_dump()
    if not isinstance(response, dict) or type(response.get("success")) is not bool:
        return MCPResponse(success=False, error="invalid_editor_state",
                           message="Editor query did not return a valid response; readiness is unknown.")
    if response["success"] is False:
        try:
            failure = MCPResponse.model_validate(response, strict=True)
        except ValidationError:
            return MCPResponse(success=False, error="invalid_editor_state",
                               message="Malformed Editor failure response; readiness is unknown.",
                               data={"original_response": response})
        # Preserve the error/hint and diagnostic data, but never carry a healthy
        # readiness assertion inside a failed query (including a cached snapshot).
        if isinstance(failure.data, dict) and "advice" in failure.data:
            failure.data = deepcopy(failure.data)
            failure.data["advice"] = EditorStateAdvice(
                ready_for_tools=None, blocking_reasons=["editor_query_failed"],
                recommended_next_action="retry_later", recommended_retry_after_ms=500,
            ).model_dump()
        return failure

    try:
        validated = EditorStateData.model_validate(response.get("data"), strict=True)
        if (validated.schema_version != "unity-mcp/editor_state@2"
                or validated.observed_at_unix_ms <= 0
                or validated.observed_at_unix_ms > _now_unix_ms() + 2000
                or validated.sequence < 0):
            raise ValueError("Invalid Editor snapshot schema, timestamp or sequence.")
    except (ValidationError, ValueError) as exc:
        return MCPResponse(success=False, error="invalid_editor_state",
                           message=f"Editor state payload failed validation: {exc}")
    state_v2 = validated.model_dump()

    # Routing intent cannot stand in for identity actually returned by the Editor.
    instance_id = (state_v2.get("unity") or {}).get("instance_id")
    # Stdio discovery advertises 8 hash digits; the shared Unity identity uses 16.
    # The name and full advertised hash must match, not merely the display name.
    expected_name, _, expected_hash = (unity_instance or "").rpartition("@")
    actual_name, _, actual_hash = (instance_id or "").rpartition("@")
    identity_verified = bool(unity_instance and instance_id and (
        instance_id == unity_instance or (
            expected_name == actual_name and len(expected_hash) == 8
            and len(actual_hash) == 16 and actual_hash.startswith(expected_hash))))
    if unity_instance and instance_id and not identity_verified:
        return MCPResponse(success=False, error="editor_instance_mismatch",
                           message=f"Requested {unity_instance}, but Editor reported {instance_id}; readiness is unknown.")
    if identity_verified:
        # Keep the transport's canonical ID usable by set_active_instance and the
        # per-instance dirty tracker, but only after verifying the producer ID.
        instance_id = unity_instance
        state_v2["unity"]["instance_id"] = instance_id

    # External change detection (server-side): compute per instance based on project root path.
    try:
        if identity_verified:
            from services.resources.project_info import get_project_info

            proj_resp = await get_project_info(ctx)
            proj = proj_resp.model_dump() if hasattr(
                proj_resp, "model_dump") else proj_resp
            proj_data = proj.get("data") if isinstance(proj, dict) else None
            project_root = proj_data.get("projectRoot") if isinstance(
                proj_data, dict) and proj.get("success") is True else None
            if config.project_path:
                if not isinstance(project_root, str) or not project_root.strip():
                    identity_verified = False
                elif not PortDiscovery._matches_project_scope(project_root):
                    return MCPResponse(success=False, error="editor_project_mismatch",
                                       message="Editor project root does not match this server's project scope; readiness is unknown.")
            if identity_verified and isinstance(project_root, str) and project_root.strip():
                external_changes_scanner.set_project_root(
                    instance_id, project_root)

            ext = external_changes_scanner.update_and_get(instance_id)

            assets = state_v2.get("assets")
            if isinstance(assets, dict):
                assets["external_changes_dirty"] = bool(ext.get("external_changes_dirty", False))
                assets["external_changes_last_seen_unix_ms"] = ext.get("external_changes_last_seen_unix_ms")
                assets["external_changes_dirty_since_unix_ms"] = ext.get("dirty_since_unix_ms")
                assets["external_changes_last_cleared_unix_ms"] = ext.get("last_cleared_unix_ms")
    except Exception:
        # A failed scoped identity query must not leave a guessed identity ready.
        if config.project_path:
            identity_verified = False

    state_v2 = _enrich_advice_and_staleness(state_v2, identity_verified=identity_verified)
    return MCPResponse(success=True, message="Retrieved editor state.", data=state_v2)
