# Tool Refactor Plan
The purpose of this refactor is to minimize the amount of tools in use. Right now we have around 35 tool available to the LLM. Most research I've seen says the ideal amount of tools is 10-30 total. This includes when using multiple MCP servers. So to help the LLM make the best tool choice for the job, we're going to narrow down the number of tools we are using from 35 to 8-ish.

## Project Structure
We are building a Unity plugin under the folder and name UnityMCP. Within this folder are two projects. One is the MCP server under Python/ and the other is the Unity bridge and tool implementations under Editor/

## Steps

1. Remove all existing tools except for execute_command under editor_tools.py and for HandleExecuteCommand in EditorControlHandler.cs. This will be the only tool reused. All other files should be deleted. Rename editor_tools.py to execute_command.py. Rename EditorControllerHandler.cs to ExecuteCommand.cs.

2. Create Python/tools/manage_script.py and Editor/Tools/ManageScript.cs
    - Implement all CRUD operations. Specify the action with an 'action' parameter.
    - Add required parameter 'name'
    - Add optional parameters 'path', 'contents', and 'script_type' (MonoBehaviour, ScriptableObject, Editor, etc.)
    - Include validation for script syntax
    - Add optional 'namespace' parameter for organizing scripts

3. Create Python/tools/manage_scene.py and Editor/Tools/ManageScene.cs
    - Implement scene operations like loading, saving, creating new scenes.
    - Add required parameter 'action' to specify operation (load, save, create, get_hierarchy, etc.)
    - Add optional parameters 'name', 'path', and 'build_index'
    - Handle scene hierarchy queries with 'get_hierarchy' action

4. Create Python/tools/manage_editor.py and Editor/Tools/ManageEditor.cs
    - Control editor state (play mode, pause, stop). Query editor state
    - Add required parameter 'action' to specify the operation ('play', 'pause', 'stop', 'get_state', etc.)
    - Add optional parameters for specific settings ('resolution', 'quality', 'target_framerate')
    - Include operations for managing editor windows and layouts
    - Add optional 'wait_for_completion' boolean parameter for operations that take time
    - Support querying current active tool and selection

5. Create Python/tools/manage_gameobject.py and Editor/Tools/ManageGameObject.cs
    - Handle GameObject creation, modification, deletion
    - Add required parameters 'action' ('create', 'modify', 'delete', 'find', 'get_components', etc.)
    - Add required parameter 'target' for operations on existing objects (path, name, or ID)
    - Add optional parameters 'parent', 'position', 'rotation', 'scale', 'components'
    - Support component-specific operations with 'component_name' and 'component_properties'
    - Add 'search_method' parameter ('by_name', 'by_tag', 'by_layer', 'by_component')
    - Return standardized GameObject data structure with transforms and components

6. Create Python/tools/manage_asset.py and Editor/Tools/ManageAsset.cs
    - Implement asset operations ('import', 'create', 'modify', 'delete', 'duplicate', 'search')
    - Add required parameters 'action' and 'path'
    - Add optional parameters 'asset_type', 'properties', 'destination' (for duplicate/move)
    - Support asset-specific parameters based on asset_type
    - Include preview generation with optional 'generate_preview' parameter
    - Add pagination support with 'page_size' and 'page_number' for search results
    - Support filtering assets by type, name pattern, or creation date

7. Create Python/tools/read_console.py and Editor/Tools/ReadConsole.cs
    - Retrieve Unity console output (errors, warnings, logs)
    - Add optional parameters 'type' (array of 'error', 'warning', 'log', 'all')
    - Add optional 'count', 'filter_text', 'since_timestamp' parameters
    - Support 'clear' action to clear console
    - Add 'format' parameter ('plain', 'detailed', 'json') for different output formats
    - Include stack trace toggle with 'include_stacktrace' boolean

8. Create Python/tools/execute_menu_item.py and Editor/Tools/ExecuteMenuItem.cs
    - Execute Unity editor menu commands through script
    - Add required parameter 'menu_path' for the menu item to execute
    - Add optional 'parameters' object for menu items that accept parameters
    - Support common menu operations with 'alias' parameter for simplified access
    - Include validation to prevent execution of dangerous operations
    - Add 'get_available_menus' action to list accessible menu items
    - Support context-specific menu items with optional 'context' parameter

## Implementation Guidelines

1. Ensure consistent parameter naming and structure across all tools:
   - Use 'action' parameter consistently for all operation-based tools
   - Return standardized response format with 'success', 'data', and 'error' fields
   - Use consistent error codes and messages

2. Implement proper error handling and validation:
   - Validate parameters before execution
   - Provide detailed error messages with suggestions for resolution
   - Add timeout handling for long-running operations
   - Include parameter type checking in both Python and C#

3. Use JSON for structured data exchange:
   - Define clear schema for each tool's input and output
   - Handle serialization edge cases (e.g., circular references)
   - Optimize for large data transfers when necessary

4. Minimize dependencies between tools:
   - Design each tool to function independently
   - Use common utility functions for shared functionality
   - Document any required dependencies clearly

5. Add performance considerations:
   - Implement batching for multiple related operations
   - Add optional asynchronous execution for long-running tasks
   - Include optional progress reporting for time-consuming operations

6. Improve documentation:
   - Add detailed XML/JSDoc comments for all public methods
   - Include example usage for common scenarios
   - Document potential side effects of operations