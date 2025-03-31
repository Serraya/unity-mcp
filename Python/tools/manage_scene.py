from mcp.server.fastmcp import FastMCP, Context
from typing import Optional, Dict, Any
from unity_connection import get_unity_connection

def register_manage_scene_tools(mcp: FastMCP):
    """Register all scene management tools with the MCP server."""

    @mcp.tool()
    def manage_scene(
        ctx: Context,
        action: str,
        name: Optional[str] = None,
        path: Optional[str] = None,
        build_index: Optional[int] = None,
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
            # Prepare parameters, removing None values
            params = {
                "action": action,
                "name": name,
                "path": path,
                "buildIndex": build_index
            }
            params = {k: v for k, v in params.items() if v is not None}
            
            # Send command to Unity
            response = get_unity_connection().send_command("manage_scene", params)

            # Process response
            if response.get("success"):
                return {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}
            else:
                return {"success": False, "message": response.get("error", "An unknown error occurred during scene management.")}

        except Exception as e:
            return {"success": False, "message": f"Python error managing scene: {str(e)}"}

    # Consider adding specific tools if the single 'manage_scene' becomes too complex:
    # @mcp.tool()
    # def load_scene(ctx: Context, name: str, path: Optional[str] = None, build_index: Optional[int] = None) -> Dict[str, Any]: ...
    # @mcp.tool()
    # def get_scene_hierarchy(ctx: Context) -> Dict[str, Any]: ... 