# Unity MCP Package

A Unity package that enables seamless communication between Unity and Large Language Models (LLMs) like Claude Desktop via the **Model Context Protocol (MCP)**. This server acts as a bridge, allowing Unity to send commands to and receive responses from MCP-compliant tools, empowering developers to automate workflows, manipulate assets, and control the Unity Editor programmatically.

Welcome to the initial release of this open-source project! Whether you're looking to integrate LLMs into your Unity workflow or contribute to an exciting new tool, I appreciate you taking the time to check out my project.

## Overview

The Unity MCP Server provides a bidirectional communication channel between Unity (via C#) and a Python server, enabling:

- **Asset Management**: Create, import, and manipulate Unity assets programmatically.
- **Scene Control**: Manage scenes, objects, and their properties.
- **Material Editing**: Modify materials and their properties.
- **Script Integration**: View, create, and update Unity scripts.
- **Editor Automation**: Control Unity Editor functions like undo, redo, play, and build.

This project is perfect for developers who want to leverage LLMs to enhance their Unity projects or automate repetitive tasks.

## Installation

### Prerequisites

- Unity 2020.3 LTS or newer (⚠️ only works in URP projects currently)
- Python 3.7 or newer
- uv package manager

**If you're on Mac, please install uv as**

```bash
brew install uv
```

**On Windows**

```bash
powershell -c "irm https://astral.sh/uv/install.ps1 | iex"
```

and then add to your PATH:

```bash
set Path=%USERPROFILE%\.local\bin;%Path%
```

**On Linux**

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

Otherwise, installation instructions are on their website: [Install uv](https://docs.astral.sh/uv/getting-started/installation/)

**⚠️ Do not proceed before installing UV**

### Unity Package Installation

1. **Add the Unity Package**

   - Open Unity Package Manager (`Window > Package Manager`)
   - Click the `+` button and select `Add package from git URL`
   - Enter: `https://github.com/justinpbarnett/unity-mcp.git`

2. **Set Up Python Environment**
   - Navigate to the Python directory in your project:
     - If installed as a package: `Library/PackageCache/com.justinpbarnett.unity-mcp/Python`
     - If installed locally: `Assets/unity-mcp/Python`
   - Install dependencies:
     ```bash
     uv venv
     uv pip install -e .
     ```

### MCP Client Integration

1. Open the Unity MCP window (`Window > Unity MCP`)
2. Click the "Auto Configure" button for your desired MCP client
3. Status indicator should show green and a "Configured" message

Alternatively, manually configure your MCP client:

1. Open the Unity MCP window (`Window > Unity MCP`)
2. Click the "Manually Configure" button for your desired MCP client
3. Copy the JSON code below to the config file

```json
{
  "mcpServers": {
    "unityMCP": {
      "command": "uv",
      "args": [
        "--directory",
        "/path/to/your/unity-mcp/Python",
        "run",
        "server.py"
      ]
    }
  }
}
```

Replace `/path/to/your/unity-mcp/Python` with the actual path to the Unity MCP Python directory.

**⚠️ Only run one instance of the MCP server (either on Cursor or Claude Desktop), not both**

4. **Start Claude Desktop or Cursor**
   - Launch your preferred tool
   - The Unity MCP Server will automatically start and connect

## Usage

Once configured, you can use the MCP Client to interact with Unity directly through their chat interface.

## Features

- **Bidirectional Communication**: Seamlessly send and receive data between Unity and LLMs.
- **Asset Management**: Import assets, instantiate prefabs, and create new prefabs programmatically.
- **Scene Control**: Open, save, and modify scenes, plus create and manipulate game objects.
- **Material Editing**: Apply and modify materials with ease.
- **Script Integration**: Create, view, and update C# scripts within Unity.
- **Editor Automation**: Automate Unity Editor tasks like building projects or entering play mode.

## Contributing

I'd love your help to make the Unity MCP Server even better! Here's how to contribute:

1. **Fork the Repository**  
   Fork [github.com/justinpbarnett/unity-mcp](https://github.com/justinpbarnett/unity-mcp) to your GitHub account.

2. **Create a Branch**

   ```bash
   git checkout -b feature/your-feature-name
   ```

   OR

   ```bash
   git checkout -b bugfix/your-bugfix-name
   ```

3. **Make Changes**  
   Implement your feature or fix.

4. **Commit and Push**  
   Use clear, descriptive commit messages:

   ```bash
   git commit -m "Add feature: your feature description"
   git push origin feature/your-feature-name
   ```

5. **Submit a Pull Request**  
   Open a pull request to the `master` branch. Include a description of your changes and any relevant details.

## License

This project is licensed under the **MIT License**. Feel free to use, modify, and distribute it as you see fit. See the full license [here](https://github.com/justinpbarnett/unity-mcp/blob/master/LICENSE).

## Troubleshooting

Encountering issues? Here are some common fixes:

- **Unity Bridge Not Running**  
  Ensure the Unity Editor is open and the MCP window is active. Restart Unity if needed.

- **Python Server Not Connected**

  - Verify the Python server is running (`python server.py` in the `Python` directory).
  - Check `config.json` for correct port settings (default: `unity_port: 6400`, `mcp_port: 6500`).
  - Ensure `uv` and dependencies are installed correctly.

- **Configuration Issues with Claude Desktop or Cursor**  
  Confirm the paths and settings in the configuration dialog match your tool's installation.

For additional help, check the [issue tracker](https://github.com/justinpbarnett/unity-mcp/issues) or file a new issue.

## Contact

Have questions or want to chat about the project? Reach out!

- **X**: [@justinpbarnett](https://x.com/justinpbarnett)

## Acknowledgments

A huge thanks to everyone who's supported this project's initial release. Special shoutout to Unity Technologies for having an excellent Editor API.

Happy coding, and enjoy integrating LLMs with Unity!
