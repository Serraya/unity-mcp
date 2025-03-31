"""
Defines the read_console tool for accessing Unity Editor console messages.
"""
from typing import Optional, List, Dict, Any
from mcp.server.fastmcp import FastMCP, Context
from unity_connection import get_unity_connection 

def register_read_console_tools(mcp: FastMCP):
    """Registers the read_console tool with the MCP server."""

    @mcp.tool()
    def read_console(
        ctx: Context,
        action: Optional[str] = 'get',
        types: Optional[List[str]] = ['error', 'warning', 'log'],
        count: Optional[int] = None,
        filter_text: Optional[str] = None,
        since_timestamp: Optional[str] = None,
        format: Optional[str] = 'detailed',
        include_stacktrace: Optional[bool] = True,
    ) -> Dict[str, Any]:
        """Gets messages from or clears the Unity Editor console.

        Args:
            action: Operation ('get' or 'clear').
            types: Message types to get ('error', 'warning', 'log', 'all').
            count: Max messages to return.
            filter_text: Text filter for messages.
            since_timestamp: Get messages after this timestamp (ISO 8601).
            format: Output format ('plain', 'detailed', 'json').
            include_stacktrace: Include stack traces in output.

        Returns:
            Dictionary with results. For 'get', includes 'data' (messages).
        """
        
        # Get the connection instance
        bridge = get_unity_connection()

        # Normalize action
        action = action.lower() if action else 'get'
        
        # Prepare parameters for the C# handler
        params_dict = {
            "action": action,
            "types": types if types else ['error', 'warning', 'log'], # Ensure types is not None
            "count": count,
            "filterText": filter_text,
            "sinceTimestamp": since_timestamp,
            "format": format.lower() if format else 'detailed',
            "includeStacktrace": include_stacktrace
        }

        # Remove None values unless it's 'count' (as None might mean 'all')
        params_dict = {k: v for k, v in params_dict.items() if v is not None or k == 'count'} 
        
        # Add count back if it was None, explicitly sending null might be important for C# logic
        if 'count' not in params_dict:
             params_dict['count'] = None 

        # Forward the command using the bridge's send_command method
        # The command type is the name of the tool itself in this case
        # No await needed as send_command is synchronous
        return bridge.send_command("read_console", params_dict) 