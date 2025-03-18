using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPServer.Editor.Helpers;

namespace MCPServer.Editor.Commands
{
    /// <summary>
    /// Handles script-related commands for Unity
    /// </summary>
    public static class ScriptCommandHandler
    {
        /// <summary>
        /// Views the contents of a Unity script file
        /// </summary>
        public static object ViewScript(JObject @params)
        {
            string scriptPath = (string)@params["script_path"] ?? throw new System.Exception("Parameter 'script_path' is required.");
            string fullPath = Path.Combine(Application.dataPath, scriptPath);

            if (!File.Exists(fullPath))
                throw new System.Exception($"Script file not found: {scriptPath}");

            return new { content = File.ReadAllText(fullPath) };
        }

        /// <summary>
        /// Ensures the Scripts folder exists in the project
        /// </summary>
        private static void EnsureScriptsFolderExists()
        {
            string scriptsFolderPath = Path.Combine(Application.dataPath, "Scripts");
            if (!Directory.Exists(scriptsFolderPath))
            {
                Directory.CreateDirectory(scriptsFolderPath);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Creates a new Unity script file in the Scripts folder
        /// </summary>
        public static object CreateScript(JObject @params)
        {
            string scriptName = (string)@params["script_name"] ?? throw new System.Exception("Parameter 'script_name' is required.");
            string scriptType = (string)@params["script_type"] ?? "MonoBehaviour";
            string namespaceName = (string)@params["namespace"];
            string template = (string)@params["template"];
            string scriptFolder = (string)@params["script_folder"];
            string content = (string)@params["content"];

            // Ensure script name ends with .cs
            if (!scriptName.EndsWith(".cs"))
                scriptName += ".cs";

            string scriptPath;

            // If content is provided, use it directly
            if (!string.IsNullOrEmpty(content))
            {
                // Use specified folder or default to Scripts
                scriptPath = string.IsNullOrEmpty(scriptFolder) ? "Scripts" : scriptFolder;

                // Ensure folder exists
                string folderPath = Path.Combine(Application.dataPath, scriptPath);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    AssetDatabase.Refresh();
                }

                // Create the script file with provided content
                string fullPath = Path.Combine(Application.dataPath, scriptPath, scriptName);
                File.WriteAllText(fullPath, content);

                // Refresh the AssetDatabase
                AssetDatabase.Refresh();

                return new { message = $"Created script: {Path.Combine(scriptPath, scriptName)}" };
            }

            // Otherwise generate content based on template and parameters

            // Ensure Scripts folder exists
            EnsureScriptsFolderExists();

            // Create namespace-based folder structure if namespace is specified
            scriptPath = string.IsNullOrEmpty(scriptFolder) ? "Scripts" : scriptFolder;
            if (!string.IsNullOrEmpty(namespaceName))
            {
                if (scriptPath == "Scripts") // Only modify path if we're using the default
                {
                    scriptPath = Path.Combine(scriptPath, namespaceName.Replace('.', '/'));
                }
                string namespaceFolderPath = Path.Combine(Application.dataPath, scriptPath);
                if (!Directory.Exists(namespaceFolderPath))
                {
                    Directory.CreateDirectory(namespaceFolderPath);
                    AssetDatabase.Refresh();
                }
            }

            // Create the script content
            StringBuilder contentBuilder = new StringBuilder();

            // Add using directives
            contentBuilder.AppendLine("using UnityEngine;");
            contentBuilder.AppendLine();

            // Add namespace if specified
            if (!string.IsNullOrEmpty(namespaceName))
            {
                contentBuilder.AppendLine($"namespace {namespaceName}");
                contentBuilder.AppendLine("{");
            }

            // Add class definition
            contentBuilder.AppendLine($"    public class {Path.GetFileNameWithoutExtension(scriptName)} : {scriptType}");
            contentBuilder.AppendLine("    {");

            // Add default Unity methods based on script type
            if (scriptType == "MonoBehaviour")
            {
                contentBuilder.AppendLine("        private void Start()");
                contentBuilder.AppendLine("        {");
                contentBuilder.AppendLine("            // Initialize your component here");
                contentBuilder.AppendLine("        }");
                contentBuilder.AppendLine();
                contentBuilder.AppendLine("        private void Update()");
                contentBuilder.AppendLine("        {");
                contentBuilder.AppendLine("            // Update your component here");
                contentBuilder.AppendLine("        }");
            }
            else if (scriptType == "ScriptableObject")
            {
                contentBuilder.AppendLine("        private void OnEnable()");
                contentBuilder.AppendLine("        {");
                contentBuilder.AppendLine("            // Initialize your ScriptableObject here");
                contentBuilder.AppendLine("        }");
            }

            // Close class
            contentBuilder.AppendLine("    }");

            // Close namespace if specified
            if (!string.IsNullOrEmpty(namespaceName))
            {
                contentBuilder.AppendLine("}");
            }

            // Create the script file in the Scripts folder
            string fullFilePath = Path.Combine(Application.dataPath, scriptPath, scriptName);
            File.WriteAllText(fullFilePath, contentBuilder.ToString());

            // Refresh the AssetDatabase
            AssetDatabase.Refresh();

            return new { message = $"Created script: {Path.Combine(scriptPath, scriptName)}" };
        }

        /// <summary>
        /// Updates the contents of an existing Unity script
        /// </summary>
        public static object UpdateScript(JObject @params)
        {
            string scriptPath = (string)@params["script_path"] ?? throw new System.Exception("Parameter 'script_path' is required.");
            string content = (string)@params["content"] ?? throw new System.Exception("Parameter 'content' is required.");
            bool createIfMissing = (bool?)@params["create_if_missing"] ?? false;
            bool createFolderIfMissing = (bool?)@params["create_folder_if_missing"] ?? false;

            string fullPath = Path.Combine(Application.dataPath, scriptPath);
            string directory = Path.GetDirectoryName(fullPath);

            // Check if file exists, create if requested
            if (!File.Exists(fullPath))
            {
                if (createIfMissing)
                {
                    // Create the directory if requested and needed
                    if (!Directory.Exists(directory) && createFolderIfMissing)
                    {
                        Directory.CreateDirectory(directory);
                    }
                    else if (!Directory.Exists(directory))
                    {
                        throw new System.Exception($"Directory does not exist: {Path.GetDirectoryName(scriptPath)}");
                    }

                    // Create the file with content
                    File.WriteAllText(fullPath, content);
                    AssetDatabase.Refresh();
                    return new { message = $"Created script: {scriptPath}" };
                }
                else
                {
                    throw new System.Exception($"Script file not found: {scriptPath}");
                }
            }

            // Update existing script
            File.WriteAllText(fullPath, content);

            // Refresh the AssetDatabase
            AssetDatabase.Refresh();

            return new { message = $"Updated script: {scriptPath}" };
        }

        /// <summary>
        /// Lists all script files in a specified folder
        /// </summary>
        public static object ListScripts(JObject @params)
        {
            string folderPath = (string)@params["folder_path"] ?? "Assets";

            // Special handling for "Assets" path since it's already the root
            string fullPath;
            if (folderPath.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = Application.dataPath;
            }
            else if (folderPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                // Remove "Assets/" from the path since Application.dataPath already points to it
                string relativePath = folderPath.Substring(7);
                fullPath = Path.Combine(Application.dataPath, relativePath);
            }
            else
            {
                // Assume it's a relative path from Assets
                fullPath = Path.Combine(Application.dataPath, folderPath);
            }

            if (!Directory.Exists(fullPath))
                throw new System.Exception($"Folder not found: {folderPath}");

            string[] scripts = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace(Application.dataPath, "Assets"))
                .ToArray();

            return new { scripts };
        }

        /// <summary>
        /// Attaches a script component to a GameObject
        /// </summary>
        public static object AttachScript(JObject @params)
        {
            string objectName = (string)@params["object_name"] ?? throw new System.Exception("Parameter 'object_name' is required.");
            string scriptName = (string)@params["script_name"] ?? throw new System.Exception("Parameter 'script_name' is required.");

            // Find the target object
            GameObject targetObject = GameObject.Find(objectName);
            if (targetObject == null)
                throw new System.Exception($"Object '{objectName}' not found in scene.");

            // Ensure script name ends with .cs
            if (!scriptName.EndsWith(".cs"))
                scriptName += ".cs";

            // Find the script asset
            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(scriptName));
            if (guids.Length == 0)
                throw new System.Exception($"Script '{scriptName}' not found in project.");

            // Get the script asset
            string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            MonoScript scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (scriptAsset == null)
                throw new System.Exception($"Failed to load script asset: {scriptName}");

            // Get the script type
            System.Type scriptType = scriptAsset.GetClass();
            if (scriptType == null)
                throw new System.Exception($"Script '{scriptName}' does not contain a valid MonoBehaviour class.");

            // Add the component
            Component component = targetObject.AddComponent(scriptType);
            if (component == null)
                throw new System.Exception($"Failed to add component of type {scriptType.Name} to object '{objectName}'.");

            return new
            {
                message = $"Successfully attached script '{scriptName}' to object '{objectName}'",
                component_type = scriptType.Name
            };
        }
    }
}