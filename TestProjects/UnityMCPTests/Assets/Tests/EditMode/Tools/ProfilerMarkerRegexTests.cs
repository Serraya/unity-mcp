using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Tools.Profiler;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Covers the bounded match timeout for user-supplied marker_filter regex
    /// patterns (RecordedFrameAnalysisOps / MarkerCalltreeOps). A pathological
    /// pattern must abort near the timeout instead of freezing the Editor.
    /// </summary>
    public class ProfilerMarkerRegexTests
    {
        private const string PathologicalPattern = "^(a+)+$";

        private static string AdversarialInput => new string('a', 64) + "!";

        [Test]
        public void Create_ValidPattern_MatchesWithBoundedTimeout()
        {
            var regex = MarkerFilterRegex.Create("^Update.*Loop$");

            Assert.AreEqual(MarkerFilterRegex.MatchTimeout, regex.MatchTimeout,
                "marker_filter regexes must carry the bounded match timeout.");
            Assert.IsTrue(regex.IsMatch("UpdateMainLoop"));
            Assert.IsFalse(regex.IsMatch("Render.Camera"));
        }

        [Test]
        public void Create_PathologicalPattern_TimesOutInsteadOfHanging()
        {
            var regex = MarkerFilterRegex.Create(PathologicalPattern);

            var stopwatch = Stopwatch.StartNew();
            Assert.Throws<RegexMatchTimeoutException>(() => regex.IsMatch(AdversarialInput));
            stopwatch.Stop();

            // Without a MatchTimeout this backtracks for ~2^63 steps (effectively forever).
            Assert.Less(
                stopwatch.Elapsed,
                MarkerFilterRegex.MatchTimeout + TimeSpan.FromSeconds(4),
                "Pathological pattern must abort near the match timeout instead of hanging the Editor.");
        }

        [Test]
        public void TimeoutError_IsStructuredAndActionable()
        {
            var regex = MarkerFilterRegex.Create(PathologicalPattern);
            var ex = Assert.Throws<RegexMatchTimeoutException>(() => regex.IsMatch(AdversarialInput));

            var error = ToJObject(MarkerFilterRegex.TimeoutError(ex));

            Assert.IsFalse(error.Value<bool>("success"));
            StringAssert.Contains("timed out", error.Value<string>("error"));
            StringAssert.Contains("match_mode", error.Value<string>("error"));
            Assert.AreEqual(PathologicalPattern, error["data"]?["marker_filter"]?.ToString());
            Assert.AreEqual(
                MarkerFilterRegex.MatchTimeout.TotalMilliseconds,
                error["data"]?["match_timeout_ms"]?.Value<double>());
        }

        [Test]
        public void FindMarker_InvalidRegex_ReturnsStructuredError()
        {
            var result = ToJObject(RecordedFrameAnalysisOps.FindMarker(new JObject
            {
                ["marker_filter"] = "[unclosed",
                ["match_mode"] = "regex",
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Invalid marker_filter regex", result.Value<string>("error"));
        }

        [Test]
        public void GetMarkerCalltree_InvalidRegex_ReturnsStructuredError()
        {
            var result = ToJObject(MarkerCalltreeOps.GetMarkerCalltree(new JObject
            {
                ["frame_index"] = 0,
                ["marker_filter"] = "[unclosed",
                ["match_mode"] = "regex",
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Invalid marker_filter regex", result.Value<string>("error"));
        }

        [Test]
        public void FindMarker_ValidRegex_PassesOptionsValidation()
        {
            var result = ToJObject(RecordedFrameAnalysisOps.FindMarker(new JObject
            {
                ["marker_filter"] = "^Update",
                ["match_mode"] = "regex",
                ["execution_mode"] = "sync",
            }));

            // Recorded profiler data may or may not exist in the test editor;
            // either way a valid pattern must never be rejected as invalid.
            string error = result.Value<string>("error");
            if (error != null)
            {
                StringAssert.DoesNotContain("Invalid marker_filter regex", error);
            }
        }
    }
}
