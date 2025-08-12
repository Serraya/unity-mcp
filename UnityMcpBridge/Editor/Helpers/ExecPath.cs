using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;

namespace UnityMcpBridge.Editor.Helpers
{
    internal static class ExecPath
    {
        private const string PrefClaude = "UnityMCP.ClaudeCliPath";

        // Resolve Claude CLI absolute path. Pref → env → common locations → PATH.
        internal static string ResolveClaude()
        {
            try
            {
                string pref = EditorPrefs.GetString(PrefClaude, string.Empty);
                if (!string.IsNullOrEmpty(pref) && File.Exists(pref)) return pref;
            }
            catch { }

            string env = Environment.GetEnvironmentVariable("CLAUDE_CLI");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] candidates =
                {
                    "/opt/homebrew/bin/claude",
                    "/usr/local/bin/claude",
                    Path.Combine(home, ".local", "bin", "claude"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                return Which("claude", "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin");
#else
                return null;
#endif
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if UNITY_EDITOR_WINDOWS
                // Common npm global locations
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? string.Empty;
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? string.Empty;
                string[] candidates =
                {
                    Path.Combine(appData, "npm", "claude.cmd"),
                    Path.Combine(localAppData, "npm", "claude.cmd"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
                string fromWhere = Where("claude.exe") ?? Where("claude.cmd") ?? Where("claude");
                if (!string.IsNullOrEmpty(fromWhere)) return fromWhere;
#endif
                return null;
            }

            // Linux
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
                string[] candidates =
                {
                    "/usr/local/bin/claude",
                    "/usr/bin/claude",
                    Path.Combine(home, ".local", "bin", "claude"),
                };
                foreach (string c in candidates) { if (File.Exists(c)) return c; }
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                return Which("claude", "/usr/local/bin:/usr/bin:/bin");
#else
                return null;
#endif
            }
        }

        // Use existing UV resolver; returns absolute path or null.
        internal static string ResolveUv()
        {
            return ServerInstaller.FindUvPath();
        }

        internal static bool TryRun(
            string file,
            string args,
            string workingDir,
            out string stdout,
            out string stderr,
            int timeoutMs = 15000,
            string extraPathPrepend = null)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                if (!string.IsNullOrEmpty(extraPathPrepend))
                {
                    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    psi.Environment["PATH"] = string.IsNullOrEmpty(currentPath)
                        ? extraPathPrepend
                        : (extraPathPrepend + System.IO.Path.PathSeparator + currentPath);
                }
                using var p = Process.Start(psi);
                if (p == null) return false;
                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return false; }
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        private static string Which(string exe, string prependPath)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/which", exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                psi.Environment["PATH"] = string.IsNullOrEmpty(path) ? prependPath : (prependPath + Path.PathSeparator + path);
                using var p = Process.Start(psi);
                string output = p?.StandardOutput.ReadToEnd().Trim();
                p?.WaitForExit(1500);
                return (!string.IsNullOrEmpty(output) && File.Exists(output)) ? output : null;
            }
            catch { return null; }
        }
#endif

#if UNITY_EDITOR_WINDOWS
        private static string Where(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo("where", exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                string first = p?.StandardOutput.ReadToEnd()
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                p?.WaitForExit(1500);
                return (!string.IsNullOrEmpty(first) && File.Exists(first)) ? first : null;
            }
            catch { return null; }
        }
#endif
    }
}


