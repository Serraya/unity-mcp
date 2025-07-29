using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityMcpBridge.Editor.Helpers
{
    /// <summary>
    /// Manages dynamic port allocation and persistent storage for Unity MCP Bridge
    /// </summary>
    public static class PortManager
    {
        private const int DefaultPort = 6400;
        private const int MaxPortAttempts = 100;
        private const string RegistryFileName = "unity-mcp-port.json";

        [Serializable]
        public class PortConfig
        {
            public int unity_port;
            public string created_date;
            public string project_path;
        }

        /// <summary>
        /// Get the port to use - either from storage or discover a new one
        /// Will try stored port first, then fallback to discovering new port
        /// </summary>
        /// <returns>Port number to use</returns>
        public static int GetPortWithFallback()
        {
            // Try to load stored port first
            int storedPort = LoadStoredPort();
            if (storedPort > 0 && IsPortAvailable(storedPort))
            {
                Debug.Log($"Using stored port {storedPort}");
                return storedPort;
            }

            // If no stored port or stored port is unavailable, find a new one
            int newPort = FindAvailablePort();
            SavePort(newPort);
            return newPort;
        }

        /// <summary>
        /// Discover and save a new available port (used by Auto-Connect button)
        /// </summary>
        /// <returns>New available port</returns>
        public static int DiscoverNewPort()
        {
            int newPort = FindAvailablePort();
            SavePort(newPort);
            Debug.Log($"Discovered and saved new port: {newPort}");
            return newPort;
        }

        /// <summary>
        /// Find an available port starting from the default port
        /// </summary>
        /// <returns>Available port number</returns>
        private static int FindAvailablePort()
        {
            // Always try default port first
            if (IsPortAvailable(DefaultPort))
            {
                Debug.Log($"Using default port {DefaultPort}");
                return DefaultPort;
            }

            Debug.Log($"Default port {DefaultPort} is in use, searching for alternative...");

            // Search for alternatives
            for (int port = DefaultPort + 1; port < DefaultPort + MaxPortAttempts; port++)
            {
                if (IsPortAvailable(port))
                {
                    Debug.Log($"Found available port {port}");
                    return port;
                }
            }

            throw new Exception($"No available ports found in range {DefaultPort}-{DefaultPort + MaxPortAttempts}");
        }

        /// <summary>
        /// Check if a specific port is available
        /// </summary>
        /// <param name="port">Port to check</param>
        /// <returns>True if port is available</returns>
        public static bool IsPortAvailable(int port)
        {
            try
            {
                var testListener = new TcpListener(IPAddress.Loopback, port);
                testListener.Start();
                testListener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Save port to persistent storage
        /// </summary>
        /// <param name="port">Port to save</param>
        private static void SavePort(int port)
        {
            try
            {
                var portConfig = new PortConfig
                {
                    unity_port = port,
                    created_date = DateTime.UtcNow.ToString("O"),
                    project_path = Application.dataPath
                };

                string registryDir = GetRegistryDirectory();
                Directory.CreateDirectory(registryDir);

                string registryFile = Path.Combine(registryDir, RegistryFileName);
                string json = JsonConvert.SerializeObject(portConfig, Formatting.Indented);
                File.WriteAllText(registryFile, json);

                Debug.Log($"Saved port {port} to storage");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not save port to storage: {ex.Message}");
            }
        }

        /// <summary>
        /// Load port from persistent storage
        /// </summary>
        /// <returns>Stored port number, or 0 if not found</returns>
        private static int LoadStoredPort()
        {
            try
            {
                string registryFile = Path.Combine(GetRegistryDirectory(), RegistryFileName);
                
                if (!File.Exists(registryFile))
                {
                    return 0;
                }

                string json = File.ReadAllText(registryFile);
                var portConfig = JsonConvert.DeserializeObject<PortConfig>(json);

                return portConfig?.unity_port ?? 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not load port from storage: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Get the current stored port configuration
        /// </summary>
        /// <returns>Port configuration if exists, null otherwise</returns>
        public static PortConfig GetStoredPortConfig()
        {
            try
            {
                string registryFile = Path.Combine(GetRegistryDirectory(), RegistryFileName);
                
                if (!File.Exists(registryFile))
                {
                    return null;
                }

                string json = File.ReadAllText(registryFile);
                return JsonConvert.DeserializeObject<PortConfig>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not load port config: {ex.Message}");
                return null;
            }
        }

        private static string GetRegistryDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
        }
    }
}