using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityMCP.Editor.Helpers; // For Response class
using System.Globalization;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles reading and clearing Unity Editor console log entries.
    /// Uses reflection to access internal LogEntry methods/properties.
    /// </summary>
    public static class ReadConsole
    {
        // Reflection members for accessing internal LogEntry data
        private static MethodInfo _getEntriesMethod;
        private static MethodInfo _startGettingEntriesMethod;
        private static MethodInfo _stopGettingEntriesMethod;
        private static MethodInfo _clearMethod;
        private static MethodInfo _getCountMethod;
        private static MethodInfo _getEntryMethod;
        private static FieldInfo _modeField;
        private static FieldInfo _messageField;
        private static FieldInfo _fileField;
        private static FieldInfo _lineField;
        private static FieldInfo _instanceIdField;
        // Note: Timestamp is not directly available in LogEntry; need to parse message or find alternative?

        // Static constructor for reflection setup
        static ReadConsole()
        {
            try {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                 if (logEntriesType == null) throw new Exception("Could not find internal type UnityEditor.LogEntries");

                 _getEntriesMethod = logEntriesType.GetMethod("GetEntries", BindingFlags.Static | BindingFlags.Public);
                 _startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
                 _stopGettingEntriesMethod = logEntriesType.GetMethod("StopGettingEntries", BindingFlags.Static | BindingFlags.Public);
                 _clearMethod = logEntriesType.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                 _getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                 _getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);

                 Type logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                 if (logEntryType == null) throw new Exception("Could not find internal type UnityEditor.LogEntry");

                 _modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public);
                 _messageField = logEntryType.GetField("message", BindingFlags.Instance | BindingFlags.Public);
                 _fileField = logEntryType.GetField("file", BindingFlags.Instance | BindingFlags.Public);
                 _lineField = logEntryType.GetField("line", BindingFlags.Instance | BindingFlags.Public);
                 _instanceIdField = logEntryType.GetField("instanceID", BindingFlags.Instance | BindingFlags.Public);

                // Basic check if reflection worked
                if (_getEntriesMethod == null || _clearMethod == null || _modeField == null || _messageField == null)
                {
                     throw new Exception("Failed to get required reflection members for LogEntries/LogEntry.");
                 }
            }
            catch (Exception e)
            {
                 Debug.LogError($"[ReadConsole] Static Initialization Failed: Could not setup reflection for LogEntries. Console reading/clearing will likely fail. Error: {e}");
                 // Set members to null to prevent NullReferenceExceptions later, HandleCommand should check this.
                 _getEntriesMethod = _startGettingEntriesMethod = _stopGettingEntriesMethod = _clearMethod = _getCountMethod = _getEntryMethod = null;
                 _modeField = _messageField = _fileField = _lineField = _instanceIdField = null;
            }
        }

        // --- Main Handler ---

        public static object HandleCommand(JObject @params)
        {
            // Check if reflection setup failed in static constructor
             if (_clearMethod == null || _getEntriesMethod == null || _startGettingEntriesMethod == null || _stopGettingEntriesMethod == null || _getCountMethod == null || _getEntryMethod == null || _modeField == null || _messageField == null)
             {
                 return Response.Error("ReadConsole handler failed to initialize due to reflection errors. Cannot access console logs.");
             }

            string action = @params["action"]?.ToString().ToLower() ?? "get";

            try
            {
                if (action == "clear")
                {
                    return ClearConsole();
                }
                else if (action == "get")
                {
                    // Extract parameters for 'get'
                    var types = (@params["types"] as JArray)?.Select(t => t.ToString().ToLower()).ToList() ?? new List<string> { "error", "warning", "log" };
                    int? count = @params["count"]?.ToObject<int?>();
                    string filterText = @params["filterText"]?.ToString();
                    string sinceTimestampStr = @params["sinceTimestamp"]?.ToString(); // TODO: Implement timestamp filtering
                    string format = (@params["format"]?.ToString() ?? "detailed").ToLower();
                    bool includeStacktrace = @params["includeStacktrace"]?.ToObject<bool?>() ?? true;

                    if (types.Contains("all")) {
                        types = new List<string> { "error", "warning", "log" }; // Expand 'all'
                    }

                    if (!string.IsNullOrEmpty(sinceTimestampStr))
                    {
                         Debug.LogWarning("[ReadConsole] Filtering by 'since_timestamp' is not currently implemented.");
                         // Need a way to get timestamp per log entry.
                    }

                    return GetConsoleEntries(types, count, filterText, format, includeStacktrace);
                }
                else
                {
                     return Response.Error($"Unknown action: '{action}'. Valid actions are 'get' or 'clear'.");
                }
            }
             catch (Exception e)
            {
                 Debug.LogError($"[ReadConsole] Action '{action}' failed: {e}");
                 return Response.Error($"Internal error processing action '{action}': {e.Message}");
            }
        }

        // --- Action Implementations ---

        private static object ClearConsole()
        {
            try
            {
                _clearMethod.Invoke(null, null); // Static method, no instance, no parameters
                 return Response.Success("Console cleared successfully.");
            }
            catch (Exception e)
            {
                 Debug.LogError($"[ReadConsole] Failed to clear console: {e}");
                 return Response.Error($"Failed to clear console: {e.Message}");
            }
        }

        private static object GetConsoleEntries(List<string> types, int? count, string filterText, string format, bool includeStacktrace)
        {
             List<object> formattedEntries = new List<object>();
             int retrievedCount = 0;

             try
             {
                 // LogEntries requires calling Start/Stop around GetEntries/GetEntryInternal
                 _startGettingEntriesMethod.Invoke(null, null);

                 int totalEntries = (int)_getCountMethod.Invoke(null, null);
                 // Create instance to pass to GetEntryInternal - Ensure the type is correct
                 Type logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                 if (logEntryType == null) throw new Exception("Could not find internal type UnityEditor.LogEntry during GetConsoleEntries.");
                 object logEntryInstance = Activator.CreateInstance(logEntryType); 

                 for (int i = 0; i < totalEntries; i++)
                 {
                     // Get the entry data into our instance using reflection
                     _getEntryMethod.Invoke(null, new object[] { i, logEntryInstance });

                     // Extract data using reflection
                     int mode = (int)_modeField.GetValue(logEntryInstance);
                     string message = (string)_messageField.GetValue(logEntryInstance);
                     string file = (string)_fileField.GetValue(logEntryInstance);
                     int line = (int)_lineField.GetValue(logEntryInstance);
                     // int instanceId = (int)_instanceIdField.GetValue(logEntryInstance);

                     if (string.IsNullOrEmpty(message)) continue; // Skip empty messages

                     // --- Filtering ---
                     // Filter by type
                     LogType currentType = GetLogTypeFromMode(mode);
                     if (!types.Contains(currentType.ToString().ToLowerInvariant()))
                     {
                         continue;
                     }

                      // Filter by text (case-insensitive)
                     if (!string.IsNullOrEmpty(filterText) && message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                     {
                         continue;
                     }

                     // TODO: Filter by timestamp (requires timestamp data)

                     // --- Formatting ---
                    string stackTrace = includeStacktrace ? ExtractStackTrace(message) : null;
                    // Get first line if stack is present and requested, otherwise use full message
                    string messageOnly = (includeStacktrace && !string.IsNullOrEmpty(stackTrace)) ? message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0] : message;

                     object formattedEntry = null;
                     switch (format)
                     {
                         case "plain":
                             formattedEntry = messageOnly;
                             break;
                         case "json":
                         case "detailed": // Treat detailed as json for structured return
                         default:
                             formattedEntry = new {
                                 type = currentType.ToString(),
                                 message = messageOnly,
                                 file = file,
                                 line = line,
                                 // timestamp = "", // TODO
                                 stackTrace = stackTrace // Will be null if includeStacktrace is false or no stack found
                             };
                             break;
                     }

                     formattedEntries.Add(formattedEntry);
                     retrievedCount++;

                     // Apply count limit (after filtering)
                     if (count.HasValue && retrievedCount >= count.Value)
                     {
                         break;
                     }
                 }
             }
             catch (Exception e) {
                  Debug.LogError($"[ReadConsole] Error while retrieving log entries: {e}");
                   // Ensure StopGettingEntries is called even if there's an error during iteration
                  try { _stopGettingEntriesMethod.Invoke(null, null); } catch { /* Ignore nested exception */ }
                  return Response.Error($"Error retrieving log entries: {e.Message}");
             }
             finally
             {
                  // Ensure we always call StopGettingEntries
                 try { _stopGettingEntriesMethod.Invoke(null, null); } catch (Exception e) {
                      Debug.LogError($"[ReadConsole] Failed to call StopGettingEntries: {e}");
                      // Don't return error here as we might have valid data, but log it.
                 }
             }

            // Return the filtered and formatted list (might be empty)
             return Response.Success($"Retrieved {formattedEntries.Count} log entries.", formattedEntries);
        }

        // --- Internal Helpers ---

        // Mapping from LogEntry.mode bits to LogType enum
        // Based on decompiled UnityEditor code or common patterns. Precise bits might change between Unity versions.
        // See comments below for LogEntry mode bits exploration.
        // Note: This mapping is simplified and might not cover all edge cases or future Unity versions perfectly.
        private const int ModeBitError = 1 << 0;
        private const int ModeBitAssert = 1 << 1;
        private const int ModeBitWarning = 1 << 2;
        private const int ModeBitLog = 1 << 3;
        private const int ModeBitException = 1 << 4; // Often combined with Error bits
        private const int ModeBitScriptingError = 1 << 9;
        private const int ModeBitScriptingWarning = 1 << 10;
        private const int ModeBitScriptingLog = 1 << 11;
        private const int ModeBitScriptingException = 1 << 18;
        private const int ModeBitScriptingAssertion = 1 << 22;


         private static LogType GetLogTypeFromMode(int mode)
         {
             // Check for specific error/exception/assert types first
             // Combine general and scripting-specific bits for broader matching.
             if ((mode & (ModeBitError | ModeBitScriptingError | ModeBitException | ModeBitScriptingException)) != 0) {
                 return LogType.Error;
             }
             if ((mode & (ModeBitAssert | ModeBitScriptingAssertion)) != 0) {
                 return LogType.Assert;
             }
             if ((mode & (ModeBitWarning | ModeBitScriptingWarning)) != 0) {
                 return LogType.Warning;
             }
             // If none of the above, assume it's a standard log message.
             // This covers ModeBitLog and ModeBitScriptingLog.
             return LogType.Log;
         }

        /// <summary>
        /// Attempts to extract the stack trace part from a log message.
        /// Unity log messages often have the stack trace appended after the main message,
        /// starting on a new line and typically indented or beginning with "at ".
        /// </summary>
        /// <param name="fullMessage">The complete log message including potential stack trace.</param>
        /// <returns>The extracted stack trace string, or null if none is found.</returns>
        private static string ExtractStackTrace(string fullMessage)
        {
            if (string.IsNullOrEmpty(fullMessage)) return null;

            // Split into lines, removing empty ones to handle different line endings gracefully.
            // Using StringSplitOptions.None might be better if empty lines matter within stack trace, but RemoveEmptyEntries is usually safer here.
            string[] lines = fullMessage.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // If there's only one line or less, there's no separate stack trace.
            if (lines.Length <= 1) return null;

            int stackStartIndex = -1;

            // Start checking from the second line onwards.
            for(int i = 1; i < lines.Length; ++i)
            {
                // Performance: TrimStart creates a new string. Consider using IsWhiteSpace check if performance critical.
                string trimmedLine = lines[i].TrimStart(); 

                // Check for common stack trace patterns.
                if (trimmedLine.StartsWith("at ") ||
                    trimmedLine.StartsWith("UnityEngine.") ||
                    trimmedLine.StartsWith("UnityEditor.") ||
                    trimmedLine.Contains("(at ") || // Covers "(at Assets/..." pattern
                    // Heuristic: Check if line starts with likely namespace/class pattern (Uppercase.Something)
                    (trimmedLine.Length > 0 && char.IsUpper(trimmedLine[0]) && trimmedLine.Contains('.')) 
                   )
                {
                    stackStartIndex = i;
                    break; // Found the likely start of the stack trace
                }
            }

            // If a potential start index was found...
            if (stackStartIndex > 0)
            {
                // Join the lines from the stack start index onwards using standard newline characters.
                // This reconstructs the stack trace part of the message.
                return string.Join("\n", lines.Skip(stackStartIndex));
            }

            // No clear stack trace found based on the patterns.
            return null;
        }


        /* LogEntry.mode bits exploration (based on Unity decompilation/observation):
           May change between versions.

           Basic Types:
           kError = 1 << 0 (1)
           kAssert = 1 << 1 (2)
           kWarning = 1 << 2 (4)
           kLog = 1 << 3 (8)
           kFatal = 1 << 4 (16) - Often treated as Exception/Error

           Modifiers/Context:
           kAssetImportError = 1 << 7 (128)
           kAssetImportWarning = 1 << 8 (256)
           kScriptingError = 1 << 9 (512)
           kScriptingWarning = 1 << 10 (1024)
           kScriptingLog = 1 << 11 (2048)
           kScriptCompileError = 1 << 12 (4096)
           kScriptCompileWarning = 1 << 13 (8192)
           kStickyError = 1 << 14 (16384) - Stays visible even after Clear On Play
           kMayIgnoreLineNumber = 1 << 15 (32768)
           kReportBug = 1 << 16 (65536) - Shows the "Report Bug" button
           kDisplayPreviousErrorInStatusBar = 1 << 17 (131072)
           kScriptingException = 1 << 18 (262144)
           kDontExtractStacktrace = 1 << 19 (524288) - Hint to the console UI
           kShouldClearOnPlay = 1 << 20 (1048576) - Default behavior
           kGraphCompileError = 1 << 21 (2097152)
           kScriptingAssertion = 1 << 22 (4194304)
           kVisualScriptingError = 1 << 23 (8388608)

           Example observed values:
           Log: 2048 (ScriptingLog) or 8 (Log)
           Warning: 1028 (ScriptingWarning | Warning) or 4 (Warning)
           Error: 513 (ScriptingError | Error) or 1 (Error)
           Exception: 262161 (ScriptingException | Error | kFatal?) - Complex combination
           Assertion: 4194306 (ScriptingAssertion | Assert) or 2 (Assert)
        */
    }
} 