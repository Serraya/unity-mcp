"""
Defines the execute_menu_item tool for running Unity Editor menu commands.
"""
from typing import Optional, Dict, Any
from mcp.server.fastmcp import FastMCP, Context

def register_execute_menu_item_tools(mcp: FastMCP):
    """Registers the execute_menu_item tool with the MCP server."""

    @mcp.tool()
    async def execute_menu_item(
        ctx: Context,
        menu_path: str,
        action: Optional[str] = 'execute', # Allows extending later (e.g., 'validate', 'get_available')
        parameters: Optional[Dict[str, Any]] = None, # For menu items that might accept parameters (less common)
        # alias: Optional[str] = None, # Potential future addition for common commands
        # context: Optional[Dict[str, Any]] = None # Potential future addition for context-specific menus
    ) -> Dict[str, Any]:
        """Executes a Unity Editor menu item via its path (e.g., "File/Save Project").

        Args:
            ctx: The MCP context.
            menu_path: The full path of the menu item to execute.
            action: The operation to perform (default: 'execute').
            parameters: Optional parameters for the menu item (rarely used).

        Returns:
            A dictionary indicating success or failure, with optional message/error.
        """
        
        action = action.lower() if action else 'execute'
        
        # Prepare parameters for the C# handler
        params_dict = {
            "action": action,
            "menuPath": menu_path,
            "parameters": parameters if parameters else {},
            # "alias": alias,
            # "context": context
        }

        # Remove None values
        params_dict = {k: v for k, v in params_dict.items() if v is not None}

        if "parameters" not in params_dict:
            params_dict["parameters"] = {} # Ensure parameters dict exists

        # Forward the command to the Unity editor handler
        # The C# handler is the static method HandleCommand in the ExecuteMenuItem class.
        # We assume ctx.call is the correct way to invoke it via FastMCP.
        # Note: The exact target string might need adjustment based on FastMCP's specifics.
        csharp_handler_target = "UnityMCP.Editor.Tools.ExecuteMenuItem.HandleCommand"
        return await ctx.call(csharp_handler_target, params_dict) 