"""
Defines the read_console tool for accessing Unity Editor console messages.
"""
from typing import Optional, List, Dict, Any
from mcp.server.fastmcp import FastMCP, Context

def register_read_console_tools(mcp: FastMCP):
    """Registers the read_console tool with the MCP server."""

    @mcp.tool()
    async def read_console(
        ctx: Context,
        action: Optional[str] = 'get', # Default action is to get messages
        types: Optional[List[str]] = ['error', 'warning', 'log'], # Default types to retrieve
        count: Optional[int] = None, # Max number of messages to return (null for all matching)
        filter_text: Optional[str] = None, # Text to filter messages by
        since_timestamp: Optional[str] = None, # ISO 8601 timestamp to get messages since
        format: Optional[str] = 'detailed', # 'plain', 'detailed', 'json'
        include_stacktrace: Optional[bool] = True, # Whether to include stack traces in detailed/json formats
        # context: Optional[Dict[str, Any]] = None # Future context
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

        # Forward the command to the Unity editor handler
        # The C# handler name might need adjustment (e.g., CommandRegistry)
        return await ctx.bridge.unity_editor.HandleReadConsole(params_dict) 