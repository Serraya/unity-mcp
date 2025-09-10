from mcp.server.fastmcp import FastMCP, Context
from typing import Dict, Any
from unity_connection import get_unity_connection, send_command_with_retry
from config import config
import time

from telemetry_decorator import telemetry_tool

def register_manage_scene_tools(mcp: FastMCP):
    """Register all scene management tools with the MCP server."""

    @mcp.tool()
    @telemetry_tool("manage_scene")
    def manage_scene(
        ctx: Context,
        action: str,
        name: str,
        path: str,
        build_index: Any,
    ) -> Dict[str, Any]:
        """Manages Unity scenes (load, save, create, get hierarchy, etc.).

        Args:
            action: Operation (e.g., 'load', 'save', 'create', 'get_hierarchy').
            name: Scene name (no extension) for create/load/save.
            path: Asset path for scene operations (default: "Assets/").
            build_index: Build index for load/build settings actions.
            # Add other action-specific args as needed (e.g., for hierarchy depth)

        Returns:
            Dictionary with results ('success', 'message', 'data').
        """
        try:
            # Coerce numeric inputs defensively
            def _coerce_int(value, default=None):
                if value is None:
                    return default
                try:
                    if isinstance(value, bool):
                        return default
                    if isinstance(value, int):
                        return int(value)
                    s = str(value).strip()
                    if s.lower() in ("", "none", "null"):
                        return default
                    return int(float(s))
                except Exception:
                    return default

            build_index = _coerce_int(build_index, default=0)

            params = {
                "action": action,
                "name": name,
                "path": path,
                "buildIndex": build_index
            }
            params = {k: v for k, v in params.items() if v is not None}
            
            # Use centralized retry helper
            response = send_command_with_retry("manage_scene", params)

            # Preserve structured failure data; unwrap success into a friendlier shape
            if isinstance(response, dict) and response.get("success"):
                return {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}
            return response if isinstance(response, dict) else {"success": False, "message": str(response)}

        except Exception as e:
            return {"success": False, "message": f"Python error managing scene: {str(e)}"}