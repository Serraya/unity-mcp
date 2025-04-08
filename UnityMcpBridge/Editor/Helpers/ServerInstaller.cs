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
        private const string RootFolder = "UnityMCP";
        private const string ServerFolder = "UnityMcpServer";
        private const string BranchName = "feature/install-overhaul";
        private const string GitUrl = "https://github.com/justinpbarnett/unity-mcp.git";
        private const string PyprojectUrl =
            "https://raw.githubusercontent.com/justinpbarnett/unity-mcp/refs/heads/"
            + BranchName
            + "/UnityMcpServer/src/pyproject.toml";

        /// <summary>
        /// Ensures the unity-mcp-server is installed and up to date.
        /// </summary>
        public static void EnsureServerInstalled()
        {
            try
            {
                string saveLocation = GetSaveLocation();

                if (!IsServerInstalled(saveLocation))
                {
                    InstallServer(saveLocation);
                }
                else
                {
                    string installedVersion = GetInstalledVersion(saveLocation);
                    string latestVersion = GetLatestVersion();

                    if (IsNewerVersion(latestVersion, installedVersion))
                    {
                        UpdateServer(saveLocation);
                    }
                    else { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to ensure server installation: {ex.Message}");
            }
        }

        public static string GetServerPath()
        {
            return Path.Combine(GetSaveLocation(), ServerFolder, "src");
        }

        /// <summary>
        /// Gets the platform-specific save location for the server.
        /// </summary>
        private static string GetSaveLocation()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData",
                    "Local",
                    "Programs",
                    RootFolder
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "bin",
                    RootFolder
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string path = "/usr/local/bin";
                return !Directory.Exists(path) || !IsDirectoryWritable(path)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Applications",
                        RootFolder
                    )
                    : Path.Combine(path, RootFolder);
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
            return Directory.Exists(location)
                && File.Exists(Path.Combine(location, ServerFolder, "version.txt"));
        }

        /// <summary>
        /// Installs the server by cloning only the UnityMcpServer folder from the repository and setting up dependencies.
        /// </summary>
        private static void InstallServer(string location)
        {
            // Create the src directory where the server code will reside
            Directory.CreateDirectory(location);

            // Initialize git repo in the src directory
            RunCommand("git", $"init", workingDirectory: location);

            // Add remote
            RunCommand("git", $"remote add origin {GitUrl}", workingDirectory: location);

            // Configure sparse checkout
            RunCommand("git", "config core.sparseCheckout true", workingDirectory: location);

            // Set sparse checkout path to only include UnityMcpServer folder
            string sparseCheckoutPath = Path.Combine(location, ".git", "info", "sparse-checkout");
            File.WriteAllText(sparseCheckoutPath, $"{ServerFolder}/");

            // Fetch and checkout the branch
            RunCommand("git", $"fetch --depth=1 origin {BranchName}", workingDirectory: location);
            RunCommand("git", $"checkout {BranchName}", workingDirectory: location);

            // Create version.txt file based on pyproject.toml, stored at the root level
            string pyprojectPath = Path.Combine(location, ServerFolder, "src", "pyproject.toml");
            if (File.Exists(pyprojectPath))
            {
                string pyprojectContent = File.ReadAllText(pyprojectPath);
                string version = ParseVersionFromPyproject(pyprojectContent);
                File.WriteAllText(Path.Combine(location, ServerFolder, "version.txt"), version);
            }
            else
            {
                throw new Exception("Failed to find pyproject.toml after checkout");
            }
        }

        /// <summary>
        /// Retrieves the installed server version from version.txt.
        /// </summary>
        private static string GetInstalledVersion(string location)
        {
            string versionFile = Path.Combine(location, ServerFolder, "version.txt");
            return File.ReadAllText(versionFile).Trim();
        }

        /// <summary>
        /// Fetches the latest version from the GitHub pyproject.toml file.
        /// </summary>
        private static string GetLatestVersion()
        {
            using WebClient webClient = new();
            string pyprojectContent = webClient.DownloadString(PyprojectUrl);
            return ParseVersionFromPyproject(pyprojectContent);
        }

        /// <summary>
        /// Updates the server by pulling the latest changes for the UnityMcpServer folder only.
        /// </summary>
        private static void UpdateServer(string location)
        {
            // Pull latest changes in the src directory
            string serverDir = Path.Combine(location, ServerFolder, "src");
            RunCommand("git", $"pull origin {BranchName}", workingDirectory: serverDir);

            // Update version.txt file based on pyproject.toml in src
            string pyprojectPath = Path.Combine(serverDir, "pyproject.toml");
            if (File.Exists(pyprojectPath))
            {
                string pyprojectContent = File.ReadAllText(pyprojectPath);
                string version = ParseVersionFromPyproject(pyprojectContent);
                File.WriteAllText(Path.Combine(location, ServerFolder, "version.txt"), version);
            }
        }

        /// <summary>
        /// Parses the version number from pyproject.toml content.
        /// </summary>
        private static string ParseVersionFromPyproject(string content)
        {
            foreach (string line in content.Split('\n'))
            {
                if (line.Trim().StartsWith("version ="))
                {
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        return parts[1].Trim().Trim('"');
                    }
                }
            }
            throw new Exception("Version not found in pyproject.toml");
        }

        /// <summary>
        /// Compares two version strings to determine if the latest is newer.
        /// </summary>
        private static bool IsNewerVersion(string latest, string installed)
        {
            int[] latestParts = latest.Split('.').Select(int.Parse).ToArray();
            int[] installedParts = installed.Split('.').Select(int.Parse).ToArray();
            for (int i = 0; i < Math.Min(latestParts.Length, installedParts.Length); i++)
            {
                if (latestParts[i] > installedParts[i])
                {
                    return true;
                }

                if (latestParts[i] < installedParts[i])
                {
                    return false;
                }
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
            System.Diagnostics.Process process = new()
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
