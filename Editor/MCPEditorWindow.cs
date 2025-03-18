using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;

public class DefaultServerConfig : ServerConfig
{
    public new string unityHost = "localhost";
    public new int unityPort = 6400;
    public new int mcpPort = 6500;
    public new float connectionTimeout = 15.0f;
    public new int bufferSize = 32768;
    public new string logLevel = "INFO";
    public new string logFormat = "%(asctime)s - %(name)s - %(levelname)s - %(message)s";
    public new int maxRetries = 3;
    public new float retryDelay = 1.0f;

}

[Serializable]
public class MCPConfig
{
    [JsonProperty("mcpServers")]
    public MCPConfigServers mcpServers;
}

[Serializable]
public class MCPConfigServers
{
    [JsonProperty("unityMCP")]
    public MCPConfigServer unityMCP;
}

[Serializable]
public class MCPConfigServer
{
    [JsonProperty("command")]
    public string command;

    [JsonProperty("args")]
    public string[] args;
}

[Serializable]
public class ServerConfig
{
    [JsonProperty("unity_host")]
    public string unityHost = "localhost";

    [JsonProperty("unity_port")]
    public int unityPort;

    [JsonProperty("mcp_port")]
    public int mcpPort;

    [JsonProperty("connection_timeout")]
    public float connectionTimeout;

    [JsonProperty("buffer_size")]
    public int bufferSize;

    [JsonProperty("log_level")]
    public string logLevel;

    [JsonProperty("log_format")]
    public string logFormat;

    [JsonProperty("max_retries")]
    public int maxRetries;

    [JsonProperty("retry_delay")]
    public float retryDelay;
}

public class MCPEditorWindow : EditorWindow
{
    private bool isUnityBridgeRunning = false;
    private Vector2 scrollPosition;
    private string claudeConfigStatus = "Not configured";
    private string pythonServerStatus = "Not Connected";
    private Color pythonServerStatusColor = Color.red;
    private const int unityPort = 6400;  // Hardcoded Unity port
    private const int mcpPort = 6500;    // Hardcoded MCP port
    private const float CONNECTION_CHECK_INTERVAL = 2f; // Check every 2 seconds
    private float lastCheckTime = 0f;

    [MenuItem("Window/Unity MCP")]
    public static void ShowWindow()
    {
        GetWindow<MCPEditorWindow>("MCP Editor");
    }

    private void OnEnable()
    {
        // Check initial states
        isUnityBridgeRunning = UnityMCPBridge.IsRunning;
        CheckPythonServerConnection();
    }

    private void Update()
    {
        // Check Python server connection periodically
        if (Time.realtimeSinceStartup - lastCheckTime >= CONNECTION_CHECK_INTERVAL)
        {
            CheckPythonServerConnection();
            lastCheckTime = Time.realtimeSinceStartup;
        }
    }

    private async void CheckPythonServerConnection()
    {
        try
        {
            using (var client = new TcpClient())
            {
                // Try to connect with a short timeout
                var connectTask = client.ConnectAsync("localhost", unityPort);
                if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask)
                {
                    // Try to send a ping message to verify connection is alive
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        byte[] pingMessage = Encoding.UTF8.GetBytes("ping");
                        await stream.WriteAsync(pingMessage, 0, pingMessage.Length);

                        // Wait for response with timeout
                        byte[] buffer = new byte[1024];
                        var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                        if (await Task.WhenAny(readTask, Task.Delay(1000)) == readTask)
                        {
                            // Connection successful and responsive
                            pythonServerStatus = "Connected";
                            pythonServerStatusColor = Color.green;
                            UnityEngine.Debug.Log($"Python server connected successfully on port {unityPort}");
                        }
                        else
                        {
                            // No response received
                            pythonServerStatus = "No Response";
                            pythonServerStatusColor = Color.yellow;
                            UnityEngine.Debug.LogWarning($"Python server not responding on port {unityPort}");
                        }
                    }
                    catch (Exception e)
                    {
                        // Connection established but communication failed
                        pythonServerStatus = "Communication Error";
                        pythonServerStatusColor = Color.yellow;
                        UnityEngine.Debug.LogWarning($"Error communicating with Python server: {e.Message}");
                    }
                }
                else
                {
                    // Connection failed
                    pythonServerStatus = "Not Connected";
                    pythonServerStatusColor = Color.red;
                    UnityEngine.Debug.LogWarning($"Python server is not running or not accessible on port {unityPort}");
                }
                client.Close();
            }
        }
        catch (Exception e)
        {
            pythonServerStatus = "Connection Error";
            pythonServerStatusColor = Color.red;
            UnityEngine.Debug.LogError($"Error checking Python server connection: {e.Message}");
        }
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("MCP Editor", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        // Python Server Status Section
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Python Server Status", EditorStyles.boldLabel);

        // Status bar
        var statusRect = EditorGUILayout.BeginHorizontal();
        EditorGUI.DrawRect(new Rect(statusRect.x, statusRect.y, 10, 20), pythonServerStatusColor);
        EditorGUILayout.LabelField(pythonServerStatus);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField($"Unity Port: {unityPort}");
        EditorGUILayout.LabelField($"MCP Port: {mcpPort}");
        EditorGUILayout.HelpBox("Start the Python server using command line: 'uv run server.py' in the Python directory", MessageType.Info);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // Unity Bridge Section
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Unity MCP Bridge", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Status: {(isUnityBridgeRunning ? "Running" : "Stopped")}");
        EditorGUILayout.LabelField($"Port: {unityPort}");

        if (GUILayout.Button(isUnityBridgeRunning ? "Stop Bridge" : "Start Bridge"))
        {
            ToggleUnityBridge();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // Claude Desktop Configuration Section
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Claude Desktop Configuration", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Status: {claudeConfigStatus}");

        if (GUILayout.Button("Configure Claude Desktop"))
        {
            ConfigureClaudeDesktop();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
    }

    private void ToggleUnityBridge()
    {
        if (isUnityBridgeRunning)
        {
            UnityMCPBridge.Stop();
        }
        else
        {
            UnityMCPBridge.Start();
        }
        isUnityBridgeRunning = !isUnityBridgeRunning;
    }

    private void ConfigureClaudeDesktop()
    {
        try
        {
            // Determine the config file path based on OS
            string configPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Claude",
                    "claude_desktop_config.json"
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "Claude",
                    "claude_desktop_config.json"
                );
            }
            else
            {
                claudeConfigStatus = "Unsupported OS";
                return;
            }

            // Create directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            // Get the absolute path to the Python directory
            string pythonDir = Path.GetFullPath(Path.Combine(Application.dataPath, "unity-mcp", "Python"));
            UnityEngine.Debug.Log($"Python directory path: {pythonDir}");

            // Create configuration object
            var config = new MCPConfig
            {
                mcpServers = new MCPConfigServers
                {
                    unityMCP = new MCPConfigServer
                    {
                        command = "uv",
                        args = new[]
                        {
                            "--directory",
                            pythonDir,
                            "run",
                            "server.py"
                        }
                    }
                }
            };

            // Serialize and write to file with proper formatting
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
            string jsonConfig = JsonConvert.SerializeObject(config, jsonSettings);
            File.WriteAllText(configPath, jsonConfig);

            claudeConfigStatus = "Configured successfully";
            UnityEngine.Debug.Log($"Claude Desktop configuration saved to: {configPath}");
            UnityEngine.Debug.Log($"Configuration contents:\n{jsonConfig}");
        }
        catch (Exception e)
        {
            claudeConfigStatus = "Configuration failed";
            UnityEngine.Debug.LogError($"Failed to configure Claude Desktop: {e.Message}");
        }
    }
}