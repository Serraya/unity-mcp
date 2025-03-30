from mcp.server.fastmcp import FastMCP, Context
from typing import Optional, Dict, Any, Union
from unity_connection import get_unity_connection

def register_manage_editor_tools(mcp: FastMCP):
    """Register all editor management tools with the MCP server."""

    @mcp.tool()
    def manage_editor(
        ctx: Context,
        action: str,
        wait_for_completion: Optional[bool] = None,
        # --- Parameters for specific actions ---
        # For 'set_active_tool'
        tool_name: Optional[str] = None, 
        # For 'add_tag', 'remove_tag'
        tag_name: Optional[str] = None,
        # For 'add_layer', 'remove_layer'
        layer_name: Optional[str] = None,
        # Example: width: Optional[int] = None, height: Optional[int] = None
        # Example: window_name: Optional[str] = None
        # context: Optional[Dict[str, Any]] = None # Additional context
    ) -> Dict[str, Any]:
        """Controls and queries the Unity editor's state and settings.

        Args:
            action: Operation (e.g., 'play', 'pause', 'get_state', 'set_active_tool', 'add_tag').
            wait_for_completion: Optional. If True, waits for certain actions.
            Action-specific arguments (e.g., tool_name, tag_name, layer_name).

        Returns:
            Dictionary with operation results ('success', 'message', 'data').
        """
        try:
            # Prepare parameters, removing None values
            params = {
                "action": action,
                "waitForCompletion": wait_for_completion,
                "toolName": tool_name, # Corrected parameter name to match C#
                "tagName": tag_name,   # Pass tag name
                "layerName": layer_name, # Pass layer name
                # Add other parameters based on the action being performed
                # "width": width,
                # "height": height,
                # etc.
            }
            params = {k: v for k, v in params.items() if v is not None}
            
            # Send command to Unity
            response = get_unity_connection().send_command("manage_editor", params)

            # Process response
            if response.get("success"):
                return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
            else:
                return {"success": False, "message": response.get("error", "An unknown error occurred during editor management.")}

        except Exception as e:
            return {"success": False, "message": f"Python error managing editor: {str(e)}"}

    # Example of potentially splitting into more specific tools:
    # @mcp.tool()
    # def get_editor_state(ctx: Context) -> Dict[str, Any]: ...
    # @mcp.tool()
    # def set_editor_playmode(ctx: Context, state: str) -> Dict[str, Any]: ... # state='play'/'pause'/'stop' 
    # @mcp.tool()
    # def add_editor_tag(ctx: Context, tag_name: str) -> Dict[str, Any]: ...
    # @mcp.tool()
    # def add_editor_layer(ctx: Context, layer_name: str) -> Dict[str, Any]: ... 