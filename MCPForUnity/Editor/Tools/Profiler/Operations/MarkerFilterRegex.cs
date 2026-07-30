using System;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Profiler
{
    /// <summary>
    /// Single owner for user-supplied marker_filter regex compilation.
    /// The bounded match timeout keeps pathological patterns (catastrophic
    /// backtracking) from freezing the Editor main thread.
    /// </summary>
    internal static class MarkerFilterRegex
    {
        internal static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

        internal static Regex Create(string pattern)
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }

        internal static string BuildTimeoutMessage(RegexMatchTimeoutException ex)
        {
            return $"marker_filter regex '{ex.Pattern}' timed out after {ex.MatchTimeout.TotalMilliseconds:0} ms while matching a marker name. " +
                   "Simplify the pattern (avoid nested quantifiers that backtrack) or use match_mode 'contains' or 'exact'.";
        }

        internal static ErrorResponse TimeoutError(RegexMatchTimeoutException ex)
        {
            return new ErrorResponse(BuildTimeoutMessage(ex), new
            {
                marker_filter = ex.Pattern,
                match_mode = "regex",
                match_timeout_ms = ex.MatchTimeout.TotalMilliseconds,
            });
        }
    }
}
