using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityMcpBridge.Editor.Helpers
{
    public static class ServerInstaller
    {
        private const string PackageName = "unity-mcp-server";
        private const string BranchName = "feature/install-overhaul"; // Adjust branch as needed
        private const string GitUrl = "https://github.com/justinpbarnett/unity-mcp.git";
        private const string PyprojectUrl =
            "https://raw.githubusercontent.com/justinpbarnett/unity-mcp/"
            + BranchName
            + "/UnityMcpServer/pyproject.toml";

        /// <summary>
        /// Ensures the unity-mcp-server is installed and up to date.
        /// </summary>
        public static void EnsureServerInstalled()
        {
            try
            {
                string saveLocation = GetSaveLocation();
                Debug.Log($"Server save location: {saveLocation}");

                if (!IsServerInstalled(saveLocation))
                {
                    Debug.Log("Server not found. Installing...");
                    InstallServer(saveLocation);
                }
                else
                {
                    Debug.Log("Server is installed. Checking version...");
                    string installedVersion = GetInstalledVersion(saveLocation);
                    string latestVersion = GetLatestVersion();

                    if (IsNewerVersion(latestVersion, installedVersion))
                    {
                        Debug.Log(
                            $"Newer version available ({latestVersion} > {installedVersion}). Updating..."
                        );
                        UpdateServer(saveLocation);
                    }
                    else
                    {
                        Debug.Log("Server is up to date.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to ensure server installation: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the platform-specific save location for the server.
        /// </summary>
        private static string GetSaveLocation()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use a user-specific program directory under %USERPROFILE%\AppData\Local\Programs
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData",
                    "Local",
                    "Programs",
                    PackageName
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "bin",
                    PackageName
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string path = "/usr/local/bin";
                if (!Directory.Exists(path) || !IsDirectoryWritable(path))
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Applications",
                        PackageName
                    );
                }
                return Path.Combine(path, PackageName);
            }
            throw new Exception("Unsupported operating system.");
        }

        private static bool IsDirectoryWritable(string path)
        {
            try
            {
                File.Create(Path.Combine(path, "test.txt")).Dispose();
                File.Delete(Path.Combine(path, "test.txt"));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the server is installed at the specified location.
        /// </summary>
        private static bool IsServerInstalled(string location)
        {
            return Directory.Exists(location) && File.Exists(Path.Combine(location, "version.txt"));
        }

        /// <summary>
        /// Installs the server by cloning only the UnityMcpServer folder from the repository and setting up dependencies.
        /// </summary>
        private static void InstallServer(string location)
        {
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(location);

            // Initialize git repo
            RunCommand("git", $"init", workingDirectory: location);

            // Add remote
            RunCommand("git", $"remote add origin {GitUrl}", workingDirectory: location);

            // Configure sparse checkout
            RunCommand("git", "config core.sparseCheckout true", workingDirectory: location);

            // Set sparse checkout path to only include UnityMcpServer folder
            string sparseCheckoutPath = Path.Combine(location, ".git", "info", "sparse-checkout");
            File.WriteAllText(sparseCheckoutPath, "UnityMcpServer/");

            // Fetch and checkout the branch
            RunCommand("git", $"fetch --depth=1 origin {BranchName}", workingDirectory: location);
            RunCommand("git", $"checkout {BranchName}", workingDirectory: location);

            // Create version.txt file based on the pyproject.toml
            string pyprojectPath = Path.Combine(location, "UnityMcpServer", "pyproject.toml");
            if (File.Exists(pyprojectPath))
            {
                string pyprojectContent = File.ReadAllText(pyprojectPath);
                string version = ParseVersionFromPyproject(pyprojectContent);
                File.WriteAllText(Path.Combine(location, "version.txt"), version);
            }
            else
            {
                throw new Exception("Failed to find pyproject.toml after checkout");
            }

            // Set up virtual environment
            string venvPath = Path.Combine(location, "venv");
            RunCommand("python", $"-m venv \"{venvPath}\"");

            // Determine the path to the virtual environment's Python interpreter
            string pythonPath = Path.Combine(
                venvPath,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "Scripts\\python.exe"
                    : "bin/python"
            );

            // Install uv into the virtual environment
            RunCommand(pythonPath, "-m pip install uv");

            // Use uv to install dependencies from the UnityMcpServer subdirectory
            string uvPath = Path.Combine(
                venvPath,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts\\uv.exe" : "bin/uv"
            );
            RunCommand(uvPath, "pip install ./UnityMcpServer", workingDirectory: location);
        }

        /// <summary>
        /// Retrieves the installed server version from version.txt.
        /// </summary>
        private static string GetInstalledVersion(string location)
        {
            string versionFile = Path.Combine(location, "version.txt");
            return File.ReadAllText(versionFile).Trim();
        }

        /// <summary>
        /// Fetches the latest version from the GitHub pyproject.toml file.
        /// </summary>
        private static string GetLatestVersion()
        {
            using var webClient = new WebClient();
            string pyprojectContent = webClient.DownloadString(PyprojectUrl);
            return ParseVersionFromPyproject(pyprojectContent);
        }

        /// <summary>
        /// Updates the server by pulling the latest changes for the UnityMcpServer folder only.
        /// </summary>
        private static void UpdateServer(string location)
        {
            // Pull only the sparse checkout paths (UnityMcpServer folder)
            RunCommand("git", "pull origin " + BranchName, workingDirectory: location);

            // Update version.txt file
            string pyprojectPath = Path.Combine(location, "UnityMcpServer", "pyproject.toml");
            if (File.Exists(pyprojectPath))
            {
                string pyprojectContent = File.ReadAllText(pyprojectPath);
                string version = ParseVersionFromPyproject(pyprojectContent);
                File.WriteAllText(Path.Combine(location, "version.txt"), version);
            }

            // Reinstall dependencies to ensure they're up to date
            string venvPath = Path.Combine(location, "venv");
            string uvPath = Path.Combine(
                venvPath,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts\\uv.exe" : "bin/uv"
            );
            RunCommand(uvPath, "pip install -U ./UnityMcpServer", workingDirectory: location);
        }

        /// <summary>
        /// Parses the version number from pyproject.toml content.
        /// </summary>
        private static string ParseVersionFromPyproject(string content)
        {
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim().StartsWith("version ="))
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                        return parts[1].Trim().Trim('"');
                }
            }
            throw new Exception("Version not found in pyproject.toml");
        }

        /// <summary>
        /// Compares two version strings to determine if the latest is newer.
        /// </summary>
        private static bool IsNewerVersion(string latest, string installed)
        {
            var latestParts = latest.Split('.').Select(int.Parse).ToArray();
            var installedParts = installed.Split('.').Select(int.Parse).ToArray();
            for (int i = 0; i < Math.Min(latestParts.Length, installedParts.Length); i++)
            {
                if (latestParts[i] > installedParts[i])
                    return true;
                if (latestParts[i] < installedParts[i])
                    return false;
            }
            return latestParts.Length > installedParts.Length;
        }

        /// <summary>
        /// Runs a command-line process and handles output/errors.
        /// </summary>
        private static void RunCommand(
            string command,
            string arguments,
            string workingDirectory = null
        )
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                },
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception(
                    $"Command failed: {command} {arguments}\nOutput: {output}\nError: {error}"
                );
            }
        }
    }
}
