"""
Defines the manage_asset tool for interacting with Unity assets.
"""
from typing import Optional, Dict, Any, List
from mcp.server.fastmcp import FastMCP, Context

def register_manage_asset_tools(mcp: FastMCP):
    """Registers the manage_asset tool with the MCP server."""

    @mcp.tool()
    async def manage_asset(
        ctx: Context,
        action: str,
        path: str,
        asset_type: Optional[str] = None,
        properties: Optional[Dict[str, Any]] = None,
        destination: Optional[str] = None, # Used for move/duplicate
        generate_preview: Optional[bool] = False,
        # Search specific parameters
        search_pattern: Optional[str] = None, # Replaces path for search action? Or use path as pattern?
        filter_type: Optional[str] = None, # Redundant with asset_type?
        filter_date_after: Optional[str] = None, # ISO 8601 format
        page_size: Optional[int] = None,
        page_number: Optional[int] = None
    ) -> Dict[str, Any]:
        """Performs asset operations (import, create, modify, delete, etc.) in Unity.

        Args:
            ctx: The MCP context.
            action: Operation to perform (e.g., 'import', 'create', 'search').
            path: Asset path (e.g., "Materials/MyMaterial.mat") or search scope.
            asset_type: Asset type (e.g., 'Material', 'Folder') - required for 'create'.
            properties: Dictionary of properties for 'create'/'modify'.
            destination: Target path for 'duplicate'/'move'.
            search_pattern: Search pattern (e.g., '*.prefab').
            filter_*: Filters for search (type, date).
            page_*: Pagination for search.

        Returns:
            A dictionary with operation results ('success', 'data', 'error').
        """
        # Ensure properties is a dict if None
        if properties is None:
            properties = {}
            
        # Prepare parameters for the C# handler
        params_dict = {
            "action": action.lower(),
            "path": path,
            "assetType": asset_type,
            "properties": properties,
            "destination": destination,
            "generatePreview": generate_preview,
            "searchPattern": search_pattern,
            "filterType": filter_type,
            "filterDateAfter": filter_date_after,
            "pageSize": page_size,
            "pageNumber": page_number
        }
        
        # Remove None values to avoid sending unnecessary nulls
        params_dict = {k: v for k, v in params_dict.items() if v is not None}

        # Forward the command to the Unity editor handler using the send_command method
        # The C# side expects a command type and parameters.
        return await ctx.send_command("manage_asset", params_dict) 