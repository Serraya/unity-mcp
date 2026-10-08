using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ReadConsoleTests
    {
        [Test]
        public void HandleCommand_Clear_Works()
        {
            // Arrange
            // Ensure there's something to clear
            Debug.Log("Log to clear");
            
            // Verify content exists before clear
            var getBefore = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get", ["types"] = new JArray { "error", "warning", "log" }, ["count"] = 10 }));
            Assert.IsTrue(getBefore.Value<bool>("success"), getBefore.ToString());
            var entriesBefore = getBefore["data"] as JArray;
            
            // Ideally we'd assert count > 0, but other tests/system logs might affect this.
            // Just ensuring the call doesn't fail is a baseline, but let's try to be stricter if possible.
            // Since we just logged, there should be at least one entry.
            Assert.IsTrue(entriesBefore != null && entriesBefore.Count > 0, "Setup failed: console should have logs.");

            // Act
            var result = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "clear" }));

            // Assert
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            
            // Verify clear effect
            var getAfter = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "get", ["types"] = new JArray { "error", "warning", "log" }, ["count"] = 10 }));
            Assert.IsTrue(getAfter.Value<bool>("success"), getAfter.ToString());
            var entriesAfter = getAfter["data"] as JArray;
            Assert.IsTrue(entriesAfter == null || entriesAfter.Count == 0, "Console should be empty after clear.");
        }

        [Test]
        public void HandleCommand_Get_Works()
        {
            // Arrange
            string uniqueMessage = $"Test Log Message {Guid.NewGuid()}";
            Debug.Log(uniqueMessage);
            
            var paramsObj = new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "error", "warning", "log" },
                ["format"] = "detailed",
                ["count"] = 1000 // Fetch enough to likely catch our message
            };

            // Act
            var result = ToJObject(ReadConsole.HandleCommand(paramsObj));

            // Assert
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JArray;
            Assert.IsNotNull(data, "Data array should not be null.");
            Assert.IsTrue(data.Count > 0, "Should retrieve at least one log entry.");

            // Verify content
            bool found = false;
            foreach (var entry in data)
            {
                if (entry["message"]?.ToString().Contains(uniqueMessage) == true)
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, $"The unique log message '{uniqueMessage}' was not found in retrieved logs.");
        }

        [Test]
        public void HandleCommand_Get_PreservesMultilineMessageBody()
        {
            string id = Guid.NewGuid().ToString();
            string firstLine = $"First line {id}";
            string secondLine = $"Second line {id}";
            Debug.Log($"{firstLine}\n\n{secondLine}");

            var paramsObj = new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "error", "warning", "log" },
                ["format"] = "detailed",
                ["count"] = 1000
            };

            var result = ToJObject(ReadConsole.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JArray;
            Assert.IsNotNull(data, "Data array should not be null.");

            string message = null;
            foreach (var entry in data)
            {
                string candidate = entry["message"]?.ToString();
                if (candidate != null && candidate.Contains(firstLine))
                {
                    message = candidate;
                    break;
                }
            }

            Assert.IsNotNull(message, "Multi-line log entry was not found.");
            StringAssert.Contains($"{firstLine}\n\n{secondLine}", message);
            StringAssert.DoesNotContain("UnityEngine.Debug", message);
        }

        [TestCase(LogType.Error, StackTraceLogType.None)]
        [TestCase(LogType.Error, StackTraceLogType.ScriptOnly)]
        [TestCase(LogType.Warning, StackTraceLogType.None)]
        [TestCase(LogType.Warning, StackTraceLogType.ScriptOnly)]
        public void HandleCommand_Get_QuotedDebugLog_PreservesProducerSeverity(
            LogType logType,
            StackTraceLogType stackTraceLogType
        )
        {
            string message = $"Severity probe {Guid.NewGuid()}: quoted UnityEngine.Debug:Log (object)";
            StackTraceLogType originalStackTraceLogType = Application.GetStackTraceLogType(logType);

            try
            {
                Application.SetStackTraceLogType(logType, stackTraceLogType);
                LogAssert.Expect(logType, message);
                if (logType == LogType.Error)
                    Debug.LogError(message);
                else
                    Debug.LogWarning(message);

                var result = ToJObject(ReadConsole.HandleCommand(new JObject
                {
                    ["action"] = "get",
                    ["types"] = new JArray { logType.ToString().ToLowerInvariant() },
                    ["format"] = "detailed",
                    ["filterText"] = message,
                    ["count"] = 1,
                }));

                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                var entries = result["data"] as JArray;
                Assert.IsNotNull(entries);
                Assert.AreEqual(1, entries.Count);
                Assert.AreEqual(logType.ToString(), entries[0].Value<string>("type"));
                Assert.AreEqual(message, entries[0].Value<string>("message"));
            }
            finally
            {
                Application.SetStackTraceLogType(logType, originalStackTraceLogType);
            }
        }

        [TestCase(StackTraceLogType.None)]
        [TestCase(StackTraceLogType.ScriptOnly)]
        public void HandleCommand_Get_SeverityKeywords_RespectModesAndDefaultFilter(
            StackTraceLogType stackTraceLogType
        )
        {
            string prefix = $"Keyword severity probe {Guid.NewGuid()}";
            string errorMessage = $"{prefix} actual error";
            string warningMessage = $"{prefix} actual warning";
            StackTraceLogType originalLog = Application.GetStackTraceLogType(LogType.Log);
            StackTraceLogType originalError = Application.GetStackTraceLogType(LogType.Error);
            StackTraceLogType originalWarning = Application.GetStackTraceLogType(LogType.Warning);

            try
            {
                Application.SetStackTraceLogType(LogType.Log, stackTraceLogType);
                Application.SetStackTraceLogType(LogType.Error, stackTraceLogType);
                Application.SetStackTraceLogType(LogType.Warning, stackTraceLogType);

                foreach (string keyword in new[] { "LogError", "LogWarning", "Exception", "Assertion" })
                    Debug.Log($"{prefix} harmless {keyword}");
                LogAssert.Expect(LogType.Error, errorMessage);
                Debug.LogError(errorMessage);
                LogAssert.Expect(LogType.Warning, warningMessage);
                Debug.LogWarning(warningMessage);

                var parameters = new JObject
                {
                    ["action"] = "get",
                    ["types"] = new JArray { "all" },
                    ["format"] = "detailed",
                    ["filterText"] = prefix,
                    ["count"] = 6,
                };
                var allResult = ToJObject(ReadConsole.HandleCommand(parameters));
                Assert.IsTrue(allResult.Value<bool>("success"), allResult.ToString());
                var allEntries = allResult["data"] as JArray;
                Assert.IsNotNull(allEntries);
                Assert.AreEqual(6, allEntries.Count);
                foreach (var entry in allEntries)
                {
                    string body = entry.Value<string>("message");
                    string expectedType = body == errorMessage ? "Error"
                        : body == warningMessage ? "Warning" : "Log";
                    Assert.AreEqual(expectedType, entry.Value<string>("type"), body);
                }

                parameters.Remove("types");
                var defaultResult = ToJObject(ReadConsole.HandleCommand(parameters));
                Assert.IsTrue(defaultResult.Value<bool>("success"), defaultResult.ToString());
                var defaultEntries = defaultResult["data"] as JArray;
                Assert.IsNotNull(defaultEntries);
                Assert.AreEqual(2, defaultEntries.Count);
                Assert.IsTrue(ContainsMessage(defaultEntries, errorMessage));
                Assert.IsTrue(ContainsMessage(defaultEntries, warningMessage));
            }
            finally
            {
                Application.SetStackTraceLogType(LogType.Log, originalLog);
                Application.SetStackTraceLogType(LogType.Error, originalError);
                Application.SetStackTraceLogType(LogType.Warning, originalWarning);
            }
        }

        [TestCase(10)]
        [TestCase(int.MaxValue)]
        public void HandleCommand_Get_Paging_BeyondEnd_PreservesExactTotal(int cursor)
        {
            string prefix = $"Paging beyond end probe {Guid.NewGuid()}";
            for (int i = 0; i < 3; i++)
                Debug.Log($"{prefix} entry {i}");

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["format"] = "plain",
                ["filterText"] = prefix,
                ["pageSize"] = 1,
                ["cursor"] = cursor,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(0, ((JArray)data["items"]).Count);
            Assert.AreEqual(3, data.Value<int>("total"));
            Assert.AreEqual(cursor, data.Value<int>("cursor"));
            Assert.AreEqual(1, data.Value<int>("pageSize"));
            Assert.IsFalse(data.Value<bool>("truncated"));
            Assert.IsNull(data.Value<string>("nextCursor"));
        }

        [Test]
        public void HandleCommand_Get_Paging_TotalIsExactAcrossAllPages()
        {
            // Unique prefix isolates this test's entries from unrelated console noise.
            string prefix = $"PagingTotal {Guid.NewGuid()}";
            for (int i = 0; i < 25; i++)
            {
                Debug.Log($"{prefix} entry {i}");
            }

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["format"] = "plain",
                ["filterText"] = prefix,
                ["pageSize"] = 10,
                ["cursor"] = 0,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JObject;
            Assert.IsNotNull(data, "Paged response should carry a structured payload.");
            Assert.AreEqual(10, ((JArray)data["items"]).Count);
            Assert.AreEqual(25, data.Value<int>("total"),
                "'total' must be the exact match count across all pages, not a lower bound.");
            Assert.IsTrue(data.Value<bool>("truncated"));
            Assert.AreEqual("10", data.Value<string>("nextCursor"));
            Assert.AreEqual(0, data.Value<int>("cursor"));
            Assert.AreEqual(10, data.Value<int>("pageSize"));
        }

        [Test]
        public void HandleCommand_Get_Paging_LastPage_ExactTotalAndNotTruncated()
        {
            string prefix = $"PagingLast {Guid.NewGuid()}";
            for (int i = 0; i < 12; i++)
            {
                Debug.Log($"{prefix} entry {i}");
            }

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["format"] = "plain",
                ["filterText"] = prefix,
                ["pageSize"] = 5,
                ["cursor"] = 10,
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(2, ((JArray)data["items"]).Count);
            Assert.AreEqual(12, data.Value<int>("total"));
            Assert.IsFalse(data.Value<bool>("truncated"));
            Assert.IsNull(data.Value<string>("nextCursor"));
        }

        [Test]
        public void SplitMessageAndStackTrace_StripsNativeFrames()
        {
            // A Debug.Log entry with Stack Trace Logging set to Full (issue #1433).
            string message = string.Join("\n",
                "[CloudSaveManager] Status: Ready - Cloud save ready",
                "0x00007ffd387f224e (Unity) StackWalker::ShowCallstack",
                "0x00007ffd3a025909 (Unity) PlatformStacktrace::GetStacktrace",
                "0x00007ffd387a2caf (Unity) DebugStringToFile",
                "0x0000022bc5e78bc8 (Mono JIT Code) UnityEngine.Debug:Log (object)",
                "CloudSaveManager:Start () (at Assets/Scripts/CloudSaveManager.cs:12)");

            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace(message);

            Assert.AreEqual("[CloudSaveManager] Status: Ready - Cloud save ready", body);
            StringAssert.StartsWith("0x00007ffd387f224e (Unity) StackWalker::ShowCallstack", stackTrace);
        }

        [Test]
        public void SplitMessageAndStackTrace_KeepsBodyLineStartingWithHex()
        {
            string message = "Packet dump\n0x1F is the header byte\n0x1F (header byte) then payload\nend of dump";

            var (body, stackTrace) = ReadConsole.SplitMessageAndStackTrace(message);

            Assert.AreEqual(message, body);
            Assert.IsNull(stackTrace);
        }

        [TestCase("detailed")]
        [TestCase("json")]
        public void HandleCommand_Get_RequestedStack_PreservesAllDetails(string format)
        {
            string stack = CreateLongNativeAndManagedStack();

            string returnedStack = ReadLoggedStack(stack, format);

            StringAssert.StartsWith(stack, returnedStack);
            StringAssert.Contains("(at Assets/Scripts/CloudSaveManager.cs:12)", returnedStack);
            StringAssert.DoesNotContain("... truncated", returnedStack);
        }

        private static string CreateLongNativeAndManagedStack()
        {
            // Exceed both former defaults before the application caller appears.
            var frames = new string[21];
            for (int i = 0; i < 20; i++)
                frames[i] = $"0x00007ffd387f224e (Unity) NativeFrame{i}";
            frames[19] += new string('x', 13000);
            frames[20] = "CloudSaveManager:Start () (at Assets/Scripts/CloudSaveManager.cs:12)";
            return string.Join("\n", frames);
        }

        private static string ReadLoggedStack(string stack, string format)
        {
            string message = $"Stack preservation probe {Guid.NewGuid()}";
            Debug.Log(message + "\n" + stack);
            var parameters = new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["format"] = format,
                ["includeStacktrace"] = true,
                ["filterText"] = message,
                ["count"] = 1,
            };

            var result = ToJObject(ReadConsole.HandleCommand(parameters));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var entries = result["data"] as JArray;
            Assert.IsNotNull(entries);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(message, entries[0].Value<string>("message"));
            string returnedStack = entries[0].Value<string>("stackTrace");
            Assert.IsNotNull(returnedStack);
            return returnedStack;
        }

        // ──────────────────── LogEntry.mode severity mapping (issue #1348) ────────────────────

        // 0x804400 and 0x804100 were captured from a real Unity 6000.5.4f1 console via
        // reflection in issue #1348; 0x804200 is the same envelope with the ScriptingWarning
        // bit. The remaining cases exercise one ConsoleWindow.Mode bit each. The old table
        // was off by one for every scripting bit, surfacing Logs as Warnings and Warnings
        // as Errors.
        [TestCase(0x804400, LogType.Log, TestName = "ScriptingLog bit (1<<10) maps to Log")]
        [TestCase(0x804200, LogType.Warning, TestName = "ScriptingWarning bit (1<<9) maps to Warning")]
        [TestCase(0x804100, LogType.Error, TestName = "ScriptingError bit (1<<8) maps to Error")]
        [TestCase(1 << 2, LogType.Log, TestName = "Log bit (1<<2) maps to Log")]
        [TestCase(1 << 0, LogType.Error, TestName = "Error bit (1<<0) maps to Error")]
        [TestCase(1 << 1, LogType.Assert, TestName = "Assert bit (1<<1) maps to Assert")]
        [TestCase(1 << 4, LogType.Error, TestName = "Fatal bit (1<<4) maps to Error")]
        [TestCase(1 << 6, LogType.Error, TestName = "AssetImportError bit (1<<6) maps to Error")]
        [TestCase(1 << 7, LogType.Warning, TestName = "AssetImportWarning bit (1<<7) maps to Warning")]
        [TestCase(1 << 11, LogType.Error, TestName = "ScriptCompileError bit (1<<11) maps to Error")]
        [TestCase(1 << 12, LogType.Warning, TestName = "ScriptCompileWarning bit (1<<12) maps to Warning")]
        [TestCase(1 << 17, LogType.Exception, TestName = "ScriptingException bit (1<<17) maps to Exception")]
        [TestCase(1 << 21, LogType.Assert, TestName = "ScriptingAssertion bit (1<<21) maps to Assert")]
        public void GetLogTypeFromMode_MapsUnityConsoleModeBits(int mode, LogType expected)
        {
            Assert.AreEqual(expected, ReadConsole.GetLogTypeFromMode(mode));
        }

        [Test]
        public void GetLogTypeFromMode_ExceptionWins_WhenCombinedWithErrorBit()
        {
            // Unity sets the Error bit alongside ScriptingException; Exception must win.
            int mode = (1 << 17) | (1 << 0);
            Assert.AreEqual(LogType.Exception, ReadConsole.GetLogTypeFromMode(mode));
        }

        [Test]
        public void GetLogTypeFromMode_UnknownBits_FallBackToLog()
        {
            Assert.AreEqual(LogType.Log, ReadConsole.GetLogTypeFromMode(1 << 14));
        }

        // The Console window's severity toggles and search box live on the shared internal
        // UnityEditor.LogEntries state, so they leak into every StartGettingEntries caller.
        // read_console must neutralize them for the duration of a read and restore them after.

        private const int ConsoleFlagLogLevelLog = 1 << 7;
        private const int ConsoleFlagLogLevelWarning = 1 << 8;

        private static Type LogEntriesType =>
            typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");

        private static PropertyInfo ConsoleFlagsProperty => LogEntriesType.GetProperty(
            "consoleFlags", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static MethodInfo SetFilteringTextMethod => LogEntriesType.GetMethod(
            "SetFilteringText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static MethodInfo GetFilteringTextMethod => LogEntriesType.GetMethod(
            "GetFilteringText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static int ConsoleFlags
        {
            get => (int)ConsoleFlagsProperty.GetValue(null);
            set => ConsoleFlagsProperty.SetValue(null, value);
        }

        private static string FilteringText
        {
            get => (string)GetFilteringTextMethod.Invoke(null, null);
            set => SetFilteringTextMethod.Invoke(null, new object[] { value });
        }

        private static JArray GetAllEntries()
        {
            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "error", "warning", "log" },
                ["format"] = "detailed",
                ["count"] = 1000
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            return result["data"] as JArray;
        }

        private static bool ContainsMessage(JArray entries, string needle)
        {
            if (entries == null) return false;
            foreach (var entry in entries)
            {
                if (entry["message"]?.ToString().Contains(needle) == true) return true;
            }
            return false;
        }

        [Test]
        public void HandleCommand_Get_IgnoresConsoleSearchFilter()
        {
            string uniqueMessage = $"Search filter probe {Guid.NewGuid()}";
            string unrelatedQuery = $"no-entry-matches-{Guid.NewGuid()}";
            string originalFilter = FilteringText;

            try
            {
                Debug.Log(uniqueMessage);
                FilteringText = unrelatedQuery;

                var entries = GetAllEntries();

                Assert.IsTrue(
                    ContainsMessage(entries, uniqueMessage),
                    "read_console must return entries hidden by the Console window's search query.");
                Assert.AreEqual(
                    unrelatedQuery,
                    FilteringText,
                    "read_console must leave the user's console search query untouched.");
            }
            finally
            {
                FilteringText = originalFilter ?? string.Empty;
            }
        }

        [Test]
        public void HandleCommand_Get_IgnoresConsoleSeverityToggles()
        {
            string uniqueMessage = $"Severity toggle probe {Guid.NewGuid()}";
            int originalFlags = ConsoleFlags;
            int hiddenFlags = originalFlags & ~(ConsoleFlagLogLevelLog | ConsoleFlagLogLevelWarning);

            try
            {
                Debug.Log(uniqueMessage);
                ConsoleFlags = hiddenFlags;

                var entries = GetAllEntries();

                Assert.IsTrue(
                    ContainsMessage(entries, uniqueMessage),
                    "read_console must return entries hidden by the Console window's severity toggles.");
                Assert.AreEqual(
                    hiddenFlags,
                    ConsoleFlags,
                    "read_console must restore the Console window's severity toggles.");
            }
            finally
            {
                ConsoleFlags = originalFlags;
            }
        }
    }
}
