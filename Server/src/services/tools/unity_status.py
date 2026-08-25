from typing import Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.resources.editor_state import get_editor_state
from services.resources.unity_instances import unity_instances
from transport.unity_instance_middleware import get_unity_instance_middleware


@mcp_for_unity_tool(
    unity_target=None,
    group=None,
    description=(
        "Read Unity instance routing and Editor readiness. Use this when the MCP client "
        "cannot read mcpforunity://instances or mcpforunity://editor/state resources."
    ),
    annotations=ToolAnnotations(
        title="Unity Status",
        readOnlyHint=True,
        destructiveHint=False,
        idempotentHint=True,
        openWorldHint=False,
    ),
)
async def unity_status(ctx: Context) -> dict[str, Any]:
    instances_result = await unity_instances(ctx)
    middleware = get_unity_instance_middleware()
    active_instance = await middleware.get_active_instance(ctx)

    result: dict[str, Any] = {
        "success": bool(instances_result.get("success")),
        "transport": instances_result.get("transport"),
        "active_instance": active_instance,
        "instance_count": instances_result.get("instance_count", 0),
        "instances": instances_result.get("instances", []),
        "editor_state": None,
    }

    if not result["success"]:
        result["error"] = instances_result.get("error") or "Failed to list Unity instances."
        return result

    if active_instance:
        editor_response = await get_editor_state(ctx)
        result["editor_state"] = (
            editor_response.model_dump()
            if hasattr(editor_response, "model_dump")
            else editor_response
        )
    elif result["instance_count"] > 1:
        result["message"] = (
            "Multiple Unity instances are available. Pass unity_instance on the next call "
            "or call set_active_instance with an exact Name@hash."
        )
    elif result["instance_count"] == 0:
        result["message"] = "No Unity instances are currently connected."

    return result
