using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityMcpBridge.Editor.Helpers
{
    public static class ServerInstaller
    {
        private const string PackageName = "unity-mcp-server";
        private const string GitUrl =
            "git+https://github.com/justinpbarnett/unity-mcp.git#subdirectory=UnityMcpServer";
        private const string PyprojectUrl =
            "https://raw.githubusercontent.com/justinpbarnett/unity-mcp/master/UnityMcpServer/pyproject.toml";

        // Typical uv installation paths per OS
        private static readonly string[] WindowsUvPaths = new[]
        {
            @"C:\Users\$USER$\.local\bin\uv.exe", // Default uv install path
            @"C:\Program Files\uv\uv.exe", // Possible system-wide install
            @"C:\Users\$USER$\AppData\Local\Programs\uv\uv.exe",
        };

        private static readonly string[] LinuxUvPaths = new[]
        {
            "/home/$USER$/.local/bin/uv",
            "/usr/local/bin/uv",
            "/usr/bin/uv",
        };

        private static readonly string[] MacUvPaths = new[]
        {
            "/Users/$USER$/.local/bin/uv",
            "/usr/local/bin/uv",
            "/opt/homebrew/bin/uv", // Homebrew on Apple Silicon
        };

        public static void EnsureServerInstalled()
        {
            try
            {
                string uvPath = FindUvExecutable();
                if (string.IsNullOrEmpty(uvPath))
                {
                    throw new Exception(GetUvNotFoundMessage());
                }

                // Check if unity-mcp-server is installed
                string output = RunCommand(uvPath, $"pip show {PackageName}");
                if (output.Contains("WARNING: Package(s) not found"))
                {
                    Debug.Log($"Installing {PackageName}...");
                    RunCommand(uvPath, $"pip install {GitUrl}");
                    Debug.Log($"{PackageName} installed successfully.");
                }
                else
                {
                    string installedVersion = GetVersionFromPipShow(output);
                    string latestVersion = GetLatestVersionFromGitHub();
                    if (new Version(installedVersion) < new Version(latestVersion))
                    {
                        Debug.Log(
                            $"Updating {PackageName} from {installedVersion} to {latestVersion}..."
                        );
                        RunCommand(uvPath, $"pip install --upgrade {GitUrl}");
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
                Debug.LogWarning(GetInstallInstructions());
            }
        }

        private static string FindUvExecutable()
        {
            string username = Environment.UserName;
            string[] uvPaths;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uvPaths = WindowsUvPaths.Select(p => p.Replace("$USER$", username)).ToArray();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                uvPaths = LinuxUvPaths.Select(p => p.Replace("$USER$", username)).ToArray();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                uvPaths = MacUvPaths.Select(p => p.Replace("$USER$", username)).ToArray();
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported operating system.");
            }

            // First, try 'uv' directly from PATH
            try
            {
                System.Diagnostics.Process process = new();
                process.StartInfo.FileName = "uv";
                process.StartInfo.Arguments = "--version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.Start();
                process.WaitForExit(2000); // Wait up to 2 seconds
                if (process.ExitCode == 0)
                {
                    return "uv"; // Found in PATH
                }
            }
            catch
            {
                // Not in PATH, proceed to check specific locations
            }

            // Check specific paths
            foreach (string path in uvPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null; // Not found
        }

        private static string RunCommand(string fileName, string arguments)
        {
            System.Diagnostics.Process process = new();
            process.StartInfo.FileName = fileName;

            // On non-Windows, might need to adjust for shell execution
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.StartInfo.FileName = "/bin/sh";
                process.StartInfo.Arguments = $"-c \"{fileName} {arguments}\"";
            }
            else
            {
                process.StartInfo.Arguments = arguments;
            }

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

        private static string GetVersionFromPipShow(string output)
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("Version:"))
                {
                    return line.Substring("Version:".Length).Trim();
                }
            }
            throw new Exception("Version not found in pip show output");
        }

        private static string GetLatestVersionFromGitHub()
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "UnityMcpBridge");
                string content = client.GetStringAsync(PyprojectUrl).Result;
                string pattern = @"version\s*=\s*""(.*?)""";
                Match match = Regex.Match(content, pattern);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
                throw new Exception("Could not find version in pyproject.toml");
            }
        }

        private static string GetUvNotFoundMessage()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "uv not found in PATH or typical Windows locations.";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "uv not found in PATH or typical Linux locations.";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "uv not found in PATH or typical macOS locations.";
            }
            return "uv not found on this platform.";
        }

        private static string GetInstallInstructions()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Install uv with: powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\" and ensure it's in your PATH.";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Install uv with: curl -LsSf https://astral.sh/uv/install.sh | sh and ensure it's in your PATH.";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "Install uv with: brew install uv or curl -LsSf https://astral.sh/uv/install.sh | sh and ensure it's in your PATH.";
            }
            return "Install uv following platform-specific instructions and add it to your PATH.";
        }
    }
}
