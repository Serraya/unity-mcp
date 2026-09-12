from __future__ import annotations

import asyncio
import logging
import os
import time
from collections.abc import Awaitable, Callable
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
import transport.unity_transport as unity_transport
import transport.legacy.unity_connection as _legacy_conn
from transport.legacy.unity_connection import _extract_response_reason
from services.state.external_changes_scanner import external_changes_scanner
import services.resources.editor_state as editor_state

logger = logging.getLogger(__name__)

def _in_pytest() -> bool:
    """Return True when running inside pytest to avoid polling unmocked resources."""
    return "PYTEST_CURRENT_TEST" in os.environ


async def wait_for_editor_ready(ctx: Context, timeout_s: float = 30.0) -> tuple[bool, float]:
    """Poll editor_state until Unity is ready for tool calls.

    Returns (ready, elapsed_seconds).  Treats exceptions from
    get_editor_state as "not ready yet" so the loop survives transient
    connection errors during domain reload.
    """
    if _in_pytest():
        return (True, 0.0)

    start = time.monotonic()
    while time.monotonic() - start < timeout_s:
        try:
            remaining = timeout_s - (time.monotonic() - start)
            state_resp = await asyncio.wait_for(editor_state.get_editor_state(ctx), timeout=remaining)
            state = state_resp.model_dump() if hasattr(state_resp, "model_dump") else state_resp
            data = (state or {}).get("data") if isinstance(state, dict) else None
            advice = (data or {}).get("advice") if isinstance(data, dict) else None
            if (isinstance(state, dict) and state.get("success") is True
                    and isinstance(advice, dict) and advice.get("ready_for_tools") is True):
                return (True, time.monotonic() - start)
        except Exception:
            pass  # not ready yet — keep polling
        await asyncio.sleep(0.25)

    return (False, time.monotonic() - start)


def is_reloading_rejection(resp: Any) -> bool:
    """True when Unity rejected a command because it thinks it is reloading.

    The command was never executed, so retrying is safe.
    """
    if not isinstance(resp, dict) or resp.get("success"):
        return False
    data = resp.get("data") or {}
    return data.get("reason") == "reloading" and resp.get("hint") == "retry"


def is_connection_lost_after_send(resp: Any) -> bool:
    """True when a mutation's response indicates TCP was lost after command was sent.

    Script mutations trigger domain reload which kills the TCP connection.
    The mutation was likely executed but the response was lost.
    """
    if isinstance(resp, dict):
        if resp.get("success"):
            return False
        err = (resp.get("error") or resp.get("message") or "").lower()
    else:
        if getattr(resp, "success", None):
            return False
        err = (getattr(resp, "error", "") or "").lower()
    return "connection closed" in err or "disconnected" in err or "aborted" in err


async def send_mutation(
    ctx: Context,
    unity_instance: str | None,
    command: str,
    params: dict[str, Any],
    *,
    verify_after_disconnect: Callable[[], Awaitable[dict | None]] | None = None,
) -> dict | Any:
    """Send a non-idempotent mutation with reload recovery.

    Handles the full retry/recovery pattern for script mutations:
    1. Send with retry_on_reload=False (don't re-send if Unity is reloading)
    2. If reloading rejection (command never executed) → wait + retry once
    3. If connection lost after send → wait + verify via callback
    4. Wait for editor readiness before returning

    Args:
        verify_after_disconnect: async callable returning a replacement response
            dict if the mutation was verified after connection loss, or None to
            keep the original error response.
    """
    resp = await unity_transport.send_with_unity_instance(
        _legacy_conn.async_send_command_with_retry,
        unity_instance,
        command,
        params,
        retry_on_reload=False,
    )
    if is_reloading_rejection(resp):
        ready, _ = await wait_for_editor_ready(ctx)
        if not ready:
            return resp
        resp = await unity_transport.send_with_unity_instance(
            _legacy_conn.async_send_command_with_retry,
            unity_instance,
            command,
            params,
            retry_on_reload=False,
        )
    if is_connection_lost_after_send(resp) and verify_after_disconnect:
        ready, _ = await wait_for_editor_ready(ctx)
        if not ready:
            return resp
        verified = await verify_after_disconnect()
        if verified is not None:
            resp = verified
    ready, _ = await wait_for_editor_ready(ctx)
    payload = resp.model_dump() if isinstance(resp, MCPResponse) else resp
    if not isinstance(payload, dict) or type(payload.get("success")) is not bool:
        return {"success": False, "error": "invalid_mutation_response",
                "message": "Mutation acknowledgement is malformed; stop subsequent mutations and establish its outcome before retrying.",
                "data": {"original_response": payload, "ready_for_tools": None}}
    if not ready and isinstance(payload, dict) and payload.get("success") is True:
        return {
            "success": False,
            "error": "editor_readiness_unconfirmed",
            "message": "Mutation acknowledged, but readiness was not confirmed. Stop subsequent mutations; do not replay the acknowledged operation.",
            "data": {"operation_acknowledged": True, "original_response": payload,
                     "ready_for_tools": None},
        }
    return resp


async def verify_edit_by_sha(
    unity_instance: str | None,
    name: str,
    path: str,
    pre_sha: str | None,
) -> bool:
    """Verify a script edit was applied by comparing SHA before and after.

    Returns True if the file's SHA changed (edit likely applied).
    """
    if not pre_sha:
        return False
    try:
        verify = await unity_transport.send_with_unity_instance(
            _legacy_conn.async_send_command_with_retry,
            unity_instance,
            "manage_script",
            {"action": "get_sha", "name": name, "path": path},
        )
        if isinstance(verify, dict) and verify.get("success"):
            new_sha = (verify.get("data") or {}).get("sha256")
            return bool(new_sha and new_sha != pre_sha)
    except Exception as exc:
        logger.debug(
            "Failed to verify edit after disconnect for %s at %s: %r",
            name, path, exc,
        )
    return False


@mcp_for_unity_tool(
    description="Request a Unity asset database refresh and optionally a script compilation. Can optionally wait for readiness.",
    annotations=ToolAnnotations(
        title="Refresh Unity",
        destructiveHint=True,
    ),
)
async def refresh_unity(
    ctx: Context,
    mode: Annotated[Literal["if_dirty", "force"], "Refresh mode"] = "if_dirty",
    scope: Annotated[Literal["assets", "scripts", "all"],
                     "Refresh scope"] = "all",
    compile: Annotated[Literal["none", "request"],
                       "Whether to request compilation"] = "none",
    wait_for_ready: Annotated[bool,
                              "If true, wait until mcpforunity://editor/state reports data.advice.ready_for_tools true"] = True,
) -> MCPResponse | dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {
        "mode": mode,
        "scope": scope,
        "compile": compile,
        "wait_for_ready": bool(wait_for_ready),
    }

    recovered_from_disconnect = False
    # Don't retry on reload - refresh_unity triggers compilation/reload,
    # so retrying would cause multiple reloads (issue #577)
    response = await unity_transport.send_with_unity_instance(
        _legacy_conn.async_send_command_with_retry,
        unity_instance,
        "refresh_unity",
        params,
        retry_on_reload=False,
    )

    # A lost acknowledgement leaves execution unknown, even if a later state read succeeds.
    response_dict = response.model_dump() if isinstance(response, MCPResponse) else response
    if not isinstance(response_dict, dict) or type(response_dict.get("success")) is not bool:
        return MCPResponse(success=False, error="invalid_refresh_response",
                           message="Refresh acknowledgement is malformed; outcome and readiness are unknown. Do not replay automatically.")
    if response_dict["success"] is False:
        hint = response_dict.get("hint")
        err = (response_dict.get("error") or response_dict.get("message") or "").lower()
        reason = _extract_response_reason(response_dict)

        is_connection_lost = (
            "connection closed" in err
            or "disconnected" in err
            or "aborted" in err  # WinError 10053: connection aborted
            or "timeout" in err
            or reason == "reloading"
        )

        if is_connection_lost and compile == "request":
            logger.info("refresh_unity: Acknowledgement lost; refresh outcome is unknown")
            recovered_from_disconnect = True
        elif hint == "retry" or "could not connect" in err:
            # Retryable error - proceed to wait loop if wait_for_ready
            if not wait_for_ready:
                return MCPResponse(**response_dict)
            recovered_from_disconnect = True
        else:
            # Non-recoverable error - connection issue unrelated to domain reload
            logger.warning(f"refresh_unity: Non-recoverable error (compile={compile}): {err[:100]}")
            return MCPResponse(**response_dict)

    # Optional server-side wait loop (defensive): if Unity tool doesn't wait or returns quickly,
    # poll the canonical editor_state resource until ready or timeout.
    ready_confirmed = False
    if wait_for_ready:
        ready_confirmed, _ = await wait_for_editor_ready(ctx, timeout_s=60.0)

        if not ready_confirmed and not recovered_from_disconnect:
            logger.warning("refresh_unity: Timed out after 60s waiting for editor to become ready")
            return MCPResponse(
                success=False,
                message="Refresh acknowledged but timed out after 60s waiting for editor readiness.",
                data={"timeout": True, "wait_seconds": 60.0},
            )

    if recovered_from_disconnect:
        # Readiness does not verify that the earlier refresh imported the external edits.
        failure = MCPResponse(**response_dict)
        return failure.model_copy(update={
            "message": "Refresh outcome is unknown after connection failure. Do not replay it automatically.",
            "hint": "Check the requested operation's completion evidence before retrying; do not restart Unity.",
            "data": {
                **(failure.data if isinstance(failure.data, dict) else {}),
                "original_response": failure.model_dump(),
                "operation_outcome": "unknown",
                "ready_for_tools": True if ready_confirmed else None,
            },
        })

    # Clear dirty only for an acknowledged refresh, never an indeterminate operation.
    try:
        inst = unity_instance or await editor_state.infer_single_instance_id(ctx)
        if inst:
            external_changes_scanner.clear_dirty(inst)
    except Exception:
        pass

    return MCPResponse(**response_dict) if isinstance(response, dict) else response
