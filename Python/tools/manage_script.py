from mcp.server.fastmcp import FastMCP, Context
from typing import Optional, Dict, Any
from unity_connection import get_unity_connection
import os

def register_manage_script_tools(mcp: FastMCP):
    """Register all script management tools with the MCP server."""

    @mcp.tool()
    def manage_script(
        ctx: Context,
        action: str,
        name: str,
        path: Optional[str] = None,
        contents: Optional[str] = None,
        script_type: Optional[str] = None,
        namespace: Optional[str] = None
    ) -> Dict[str, Any]:
        """Manages C# scripts in Unity (create, read, update, delete).

        Args:
            action: Operation ('create', 'read', 'update', 'delete').
            name: Script name (no .cs extension).
            path: Asset path (optional, default: "Assets/").
            contents: C# code for 'create'/'update'.
            script_type: Type hint (e.g., 'MonoBehaviour', optional).
            namespace: Script namespace (optional).

        Returns:
            Dictionary with results ('success', 'message', 'data').
        """
        try:
            # Prepare parameters for Unity
            params = {
                "action": action,
                "name": name,
                "path": path,
                "contents": contents,
                "scriptType": script_type,
                "namespace": namespace
            }
            # Remove None values so they don't get sent as null
            params = {k: v for k, v in params.items() if v is not None}

            # Send command to Unity
            response = get_unity_connection().send_command("manage_script", params)
            
            # Process response from Unity
            if response.get("success"):
                return {"success": True, "message": response.get("message", "Operation successful."), "data": response.get("data")}
            else:
                return {"success": False, "message": response.get("error", "An unknown error occurred.")}

        except Exception as e:
            # Handle Python-side errors (e.g., connection issues)
            return {"success": False, "message": f"Python error managing script: {str(e)}"}

    # Potentially add more specific helper tools if needed later, e.g.:
    # @mcp.tool()
    # def create_script(...): ...
    # @mcp.tool()
    # def read_script(...): ...
    # etc. 