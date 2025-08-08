using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Helpers
{
    /// <summary>
    /// Handles automatic installation of the Python server when the package is first installed.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageInstaller
    {
        private const string InstallationFlagKey = "UnityMCP.ServerInstalled";
        
        static PackageInstaller()
        {
            // Check if this is the first time the package is loaded
            if (!EditorPrefs.GetBool(InstallationFlagKey, false))
            {
                // Schedule the installation for after Unity is fully loaded
                EditorApplication.delayCall += InstallServerOnFirstLoad;
            }
        }
        
        private static void InstallServerOnFirstLoad()
        {
            try
            {
                Debug.Log("Unity MCP: Installing Python server...");
                ServerInstaller.EnsureServerInstalled();
                
                // Mark as installed
                EditorPrefs.SetBool(InstallationFlagKey, true);
                
                Debug.Log("Unity MCP: Python server installation completed successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Unity MCP: Failed to install Python server: {ex.Message}");
                Debug.LogWarning("Unity MCP: You may need to manually install the Python server. Check the Unity MCP Editor Window for instructions.");
            }
        }
    }
}
