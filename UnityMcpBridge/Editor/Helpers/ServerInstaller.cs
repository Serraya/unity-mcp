using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityMcpBridge.Editor.Helpers
{
    public static class ServerInstaller
    {
        private const string PackageName = "unity-mcp-server";
        private const string GitUrlTemplate =
            "git+https://github.com/justinpbarnett/unity-mcp.git@{0}#subdirectory=UnityMcpServer";
        private const string PyprojectUrlTemplate =
            "https://raw.githubusercontent.com/justinpbarnett/unity-mcp/{0}/UnityMcpServer/pyproject.toml";
        private const string DefaultBranch = "master";

        /// <summary>
        /// Ensures that UnityMcpServer is installed and up to date by checking the typical application path via Python's package manager.
        /// </summary>
        /// <param name="branch">The GitHub branch to install from. Defaults to "master" if not specified.</param>
        public static void EnsureServerInstalled(string branch = DefaultBranch)
        {
            try
            {
                // Format the URLs with the specified branch
                string gitUrl = string.Format(GitUrlTemplate, branch);

                // Check if unity-mcp-server is installed using uv
                string output = RunCommand("uv", $"pip show {PackageName}");
                if (output.Contains("WARNING: Package(s) not found"))
                {
                    Debug.Log($"Installing {PackageName} from branch '{branch}'...");
                    RunCommand("uv", $"pip install {gitUrl}");
                    Debug.Log($"{PackageName} installed successfully.");
                }
                else
                {
                    // Extract the installed version
                    string installedVersion = GetVersionFromPipShow(output);
                    // Get the latest version from GitHub
                    string latestVersion = GetLatestVersionFromGitHub(branch);
                    // Compare versions
                    if (new Version(installedVersion) < new Version(latestVersion))
                    {
                        Debug.Log(
                            $"Updating {PackageName} from {installedVersion} to {latestVersion} (branch '{branch}')..."
                        );
                        RunCommand("uv", $"pip install --upgrade {gitUrl}");
                        Debug.Log($"{PackageName} updated successfully.");
                    }
                    else
                    {
                        Debug.Log($"{PackageName} is up to date (version {installedVersion}).");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to ensure {PackageName} is installed: {ex.Message}");
                Debug.LogWarning(
                    "Please ensure 'uv' is installed and accessible. See the Unity MCP README for installation instructions."
                );
            }
        }

        /// <summary>
        /// Executes a command and returns its output.
        /// </summary>
        private static string RunCommand(string fileName, string arguments)
        {
            System.Diagnostics.Process process = new();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception(
                    $"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}: {error}"
                );
            }
            return output;
        }

        /// <summary>
        /// Extracts the version from 'uv pip show' output.
        /// </summary>
        private static string GetVersionFromPipShow(string output)
        {
            string[] lines = output.Split('\n');
            foreach (string line in lines)
            {
                if (line.StartsWith("Version:"))
                {
                    return line["Version:".Length..].Trim();
                }
            }
            throw new Exception("Version not found in pip show output");
        }

        /// <summary>
        /// Fetches the latest version from the GitHub repository's pyproject.toml.
        /// </summary>
        /// <param name="branch">The GitHub branch to fetch the version from.</param>
        private static string GetLatestVersionFromGitHub(string branch)
        {
            string pyprojectUrl = string.Format(PyprojectUrlTemplate, branch);
            using HttpClient client = new();
            // Add GitHub headers to avoid rate limiting
            client.DefaultRequestHeaders.Add("User-Agent", "UnityMcpBridge");
            string content = client.GetStringAsync(pyprojectUrl).Result;
            string pattern = @"version\s*=\s*""(.*?)""";
            Match match = Regex.Match(content, pattern);
            return match.Success
                ? match.Groups[1].Value
                : throw new Exception("Could not find version in pyproject.toml");
        }
    }
}
