from mcp.server.fastmcp import FastMCP, Context
from typing import Optional, Dict, Any, List, Union
from unity_connection import get_unity_connection

def register_manage_gameobject_tools(mcp: FastMCP):
    """Register all GameObject management tools with the MCP server."""

    @mcp.tool()
    def manage_gameobject(
        ctx: Context,
        action: str,
        target: Optional[Union[str, int]] = None, # Name, path, or instance ID
        search_method: Optional[str] = None, # by_name, by_tag, by_layer, by_component, by_id
        # --- Parameters for 'create' ---
        name: Optional[str] = None, # Required for 'create'
        tag: Optional[str] = None, # Tag to assign during creation
        parent: Optional[Union[str, int]] = None, # Name or ID of parent
        position: Optional[List[float]] = None, # [x, y, z]
        rotation: Optional[List[float]] = None, # [x, y, z] Euler angles
        scale: Optional[List[float]] = None, # [x, y, z]
        components_to_add: Optional[List[Union[str, Dict[str, Any]]]] = None, # List of component names or dicts with properties
        primitive_type: Optional[str] = None, # Optional: create primitive (Cube, Sphere, etc.) instead of empty
        save_as_prefab: Optional[bool] = False, # If True, save the created object as a prefab
        prefab_path: Optional[str] = None, # Full path to save prefab (e.g., "Assets/Prefabs/MyObject.prefab"). Overrides prefab_folder.
        prefab_folder: Optional[str] = "Assets/Prefabs", # Default folder if prefab_path not set (e.g., "Assets/Prefabs")
        # --- Parameters for 'modify' ---
        new_name: Optional[str] = None,
        new_parent: Optional[Union[str, int]] = None,
        set_active: Optional[bool] = None,
        new_tag: Optional[str] = None,
        new_layer: Optional[Union[str, int]] = None, # Layer name or number
        components_to_remove: Optional[List[str]] = None,
        component_properties: Optional[Dict[str, Dict[str, Any]]] = None, # { "ComponentName": { "propName": value } }
        # --- Parameters for 'find' ---
        search_term: Optional[str] = None, # Used with search_method (e.g., name, tag value, component type)
        find_all: Optional[bool] = False, # Find all matches or just the first?
        search_in_children: Optional[bool] = False, # Limit search scope
        search_inactive: Optional[bool] = False, # Include inactive GameObjects?
        # -- Component Management Arguments --
        component_name: Optional[str] = None,       # Target component for component actions
    ) -> Dict[str, Any]:
        """Manages GameObjects: create, modify, delete, find, and component operations.

        Args:
            action: Operation (e.g., 'create', 'modify', 'find', 'add_component').
            target: GameObject identifier (name, path, ID) for modify/delete/component actions.
            search_method: How to find objects ('by_name', 'by_id', 'by_path', etc.). Used with 'find'.
            Action-specific arguments (e.g., name, parent, position for 'create'; 
                     component_name, component_properties for component actions; 
                     search_term, find_all for 'find').

        Returns:
            Dictionary with operation results ('success', 'message', 'data').
        """
        try:
            # Prepare parameters, removing None values
            params = {
                "action": action,
                "target": target,
                "searchMethod": search_method,
                "name": name,
                "tag": tag,
                "parent": parent,
                "position": position,
                "rotation": rotation,
                "scale": scale,
                "componentsToAdd": components_to_add,
                "primitiveType": primitive_type,
                "saveAsPrefab": save_as_prefab,
                "prefabPath": prefab_path,
                "prefabFolder": prefab_folder,
                "newName": new_name,
                "newParent": new_parent,
                "setActive": set_active,
                "newTag": new_tag,
                "newLayer": new_layer,
                "componentsToRemove": components_to_remove,
                "componentProperties": component_properties,
                "searchTerm": search_term,
                "findAll": find_all,
                "searchInChildren": search_in_children,
                "searchInactive": search_inactive,
                "componentName": component_name
            }
            params = {k: v for k, v in params.items() if v is not None}
            
            # --- Handle Prefab Path Logic ---
            if action == "create" and params.get("saveAsPrefab"): # Check if 'saveAsPrefab' is explicitly True in params
                if "prefabPath" not in params:
                    if "name" not in params or not params["name"]:
                        return {"success": False, "message": "Cannot create default prefab path: 'name' parameter is missing."}
                    # Use the provided prefab_folder (which has a default) and the name to construct the path
                    constructed_path = f"{prefab_folder}/{params['name']}.prefab"
                    # Ensure clean path separators (Unity prefers '/')
                    params["prefabPath"] = constructed_path.replace("\\", "/")
                elif not params["prefabPath"].lower().endswith(".prefab"):
                    return {"success": False, "message": f"Invalid prefab_path: '{params['prefabPath']}' must end with .prefab"}
            # Ensure prefab_folder itself isn't sent if prefabPath was constructed or provided
            # The C# side only needs the final prefabPath
            params.pop("prefab_folder", None) 
            # --------------------------------
            
            # Send the command to Unity via the established connection
            # Use the get_unity_connection function to retrieve the active connection instance
            # Changed "MANAGE_GAMEOBJECT" to "manage_gameobject" to potentially match Unity expectation
            response = get_unity_connection().send_command("manage_gameobject", params)

            # Check if the response indicates success
            # If the response is not successful, raise an exception with the error message
            if response.get("success"):
                return {"success": True, "message": response.get("message", "GameObject operation successful."), "data": response.get("data")}
            else:
                return {"success": False, "message": response.get("error", "An unknown error occurred during GameObject management.")}

        except Exception as e:
            return {"success": False, "message": f"Python error managing GameObject: {str(e)}"} 