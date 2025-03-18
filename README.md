# Unity MCP Package

A Unity package that enables seamless communication between Unity and Large Language Models (LLMs) like Claude Desktop via the **Model Context Protocol (MCP)**. This server acts as a bridge, allowing Unity to send commands to and receive responses from MCP-compliant tools, empowering developers to automate workflows, manipulate assets, and control the Unity Editor programmatically.

Welcome to the initial release of this open-source project! Whether you're looking to integrate LLMs into your Unity workflow or contribute to an exciting new tool, we’re thrilled to have you here.

## Overview

The Unity MCP Server provides a bidirectional communication channel between Unity (via C#) and a Python server, enabling:

- **Asset Management**: Create, import, and manipulate Unity assets programmatically.
- **Scene Control**: Manage scenes, objects, and their properties.
- **Material Editing**: Modify materials and their properties.
- **Script Integration**: View, create, and update Unity scripts.
- **Editor Automation**: Control Unity Editor functions like undo, redo, play, and build.

This project is perfect for developers who want to leverage LLMs to enhance their Unity projects or automate repetitive tasks.

## Installation

Getting started is simple! Follow these steps to add the Unity MCP Server to your project:

### Unity Package

1. **Download the Package**  
   Add via the Unity package manager using this link
  ```text
  https://github.com/justinpbarnett/unity-mcp.git
  ```

2. **Add to Unity**  
   - Open Unity and navigate to `Window > Package Manager`.
   - Click the `+` button and select `Add package from disk...`.
   - Locate the downloaded package and select the `package.json` file.

### Python Environment

1. **Prerequisites**  
   Ensure you have:
   - **Python** (version 3.7 or higher) installed. Download it from [python.org](https://www.python.org/downloads/).
   - **`uv`** installed for managing Python dependencies. Install it via:
     ```bash
     pip install uv
     ```

2. **Set Up the Python Server**  
   - Navigate to the `Python` directory within the package (e.g., `Assets/MCPServer/Python`).
   - Create a virtual environment and install dependencies:
     ```bash
     uv venv
     uv pip install -e .
     ```

## Configuration

To connect the MCP Server to tools like Claude Desktop or Cursor:

1. **Open the Unity MCP Window**  
   In Unity, go to `Window > Unity MCP` to open the editor window.

2. **Configure Your Tools**  
   - In the Unity MCP window, you’ll see buttons to configure **Claude Desktop** or **Cursor**.
   - Click the appropriate button and follow the on-screen instructions to set up the integration.

3. **Verify Server Status**  
   - Check the server status in the Unity MCP window. It will display:
     - **Unity Bridge**: Should show "Running" when active.
     - **Python Server**: Should show "Connected" (green) when successfully linked.

## Usage

Once configured, you can use the MCP Server to interact with LLMs directly from Unity or Python. Here are a couple of examples:

### Creating a Cube in the Scene

```python
# Send a command to create a cube at position (0, 0, 0)
create_primitive(primitive_type="Cube", position=[0, 0, 0])
```

### Changing a Material’s Color

```python
# Set a material’s color to red (RGBA: 1, 0, 0, 1)
set_material_color(material_name="MyMaterial", color=[1, 0, 0, 1])
```

Explore more commands in the [HOW_TO_ADD_A_TOOL.md](HOW_TO_ADD_A_TOOL.md) file for detailed examples and instructions on extending functionality.

## Features

- **Bidirectional Communication**: Seamlessly send and receive data between Unity and LLMs.
- **Asset Management**: Import assets, instantiate prefabs, and create new prefabs programmatically.
- **Scene Control**: Open, save, and modify scenes, plus create and manipulate game objects.
- **Material Editing**: Apply and modify materials with ease.
- **Script Integration**: Create, view, and update C# scripts within Unity.
- **Editor Automation**: Automate Unity Editor tasks like building projects or entering play mode.

## Contributing

We’d love your help to make the Unity MCP Server even better! Here’s how to contribute:

1. **Fork the Repository**  
   Fork [github.com/justinpbarnett/unity-mcp](https://github.com/justinpbarnett/unity-mcp) to your GitHub account.

2. **Create a Branch**  
   ```bash
   git checkout -b feature/your-feature-name
   ```

3. **Make Changes**  
   Implement your feature or fix, following the project’s coding standards (see [HOW_TO_ADD_A_TOOL.md](HOW_TO_ADD_A_TOOL.md) for guidance).

4. **Commit and Push**  
   Use clear, descriptive commit messages:
   ```bash
   git commit -m "Add feature: your feature description"
   git push origin feature/your-feature-name
   ```

5. **Submit a Pull Request**  
   Open a pull request to the `master` branch. Include a description of your changes and any relevant details.

For more details, check out [CONTRIBUTING.md](CONTRIBUTING.md) (to be created).

## License

This project is licensed under the **MIT License**. Feel free to use, modify, and distribute it as you see fit. See the full license [here](https://github.com/justinpbarnett/unity-mcp/blob/master/LICENSE).
## Troubleshooting

Encountering issues? Here are some common fixes:

- **Unity Bridge Not Running**  
  Ensure the Unity Editor is open and the MCP window is active. Restart Unity if needed.

- **Python Server Not Connected**  
  - Verify the Python server is running (`python server.py` in the `Python` directory).
  - Check `config.json` (in `Assets/MCPServer`) for correct port settings (default: `unity_port: 6400`, `mcp_port: 6500`).
  - Ensure `uv` and dependencies are installed correctly.

- **Configuration Issues with Claude Desktop or Cursor**  
  Confirm the paths and settings in the configuration dialog match your tool’s installation.

For additional help, check the [issue tracker](https://github.com/justinpbarnett/unity-mcp/issues) or file a new issue.

## Contact

Have questions or want to chat about the project? Reach out!

- **X**: [@justinpbarnett](https://x.com/justinpbarnett)
- **GitHub**: [justinpbarnett](https://github.com/justinpbarnett)  
- **Discord**: Join our community (link coming soon!).

## Acknowledgments

A huge thanks to everyone who’s supported this project’s initial release. Special shoutout to Unity Technologies for inspiring tools that push creative boundaries, and to the open-source community for making projects like this possible.

Happy coding, and enjoy integrating LLMs with Unity!
