using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class MarkerCalltreeOps
    {
        private const int DefaultMaxDepth = 8;
        private const int MaximumMaxDepth = 64;
        private const int DefaultMaxRows = 200;
        private const int MaximumMaxRows = 2000;
        private const int MaximumThreadScan = 128;
        private const int MaximumTrailingInvalidThreads = 8;

        internal static object GetMarkerCalltree(JObject @params)
        {
            try
            {
                return GetMarkerCalltreeImpl(@params);
            }
            catch (EntryPointNotFoundException ex)
            {
                return ProfilerApiUnavailable(ex);
            }
            catch (TypeLoadException ex)
            {
                return ProfilerApiUnavailable(ex);
            }
            catch (MissingMemberException ex)
            {
                return ProfilerApiUnavailable(ex);
            }
        }

        private static object GetMarkerCalltreeImpl(JObject @params)
        {
            var p = new ToolParams(@params);

            var frameIndexResult = GetRequiredInt(p, "frame_index");
            if (!frameIndexResult.IsSuccess)
                return new ErrorResponse(frameIndexResult.ErrorMessage);

            var markerFilterResult = p.GetRequired("marker_filter");
            if (!markerFilterResult.IsSuccess)
                return new ErrorResponse(markerFilterResult.ErrorMessage);

            var optionsResult = BuildOptions(p, frameIndexResult.Value, markerFilterResult.Value);
            if (!optionsResult.IsSuccess)
                return new ErrorResponse(optionsResult.ErrorMessage);

            var options = optionsResult.Value;
            int firstFrameIndex = ProfilerDriver.firstFrameIndex;
            int lastFrameIndex = ProfilerDriver.lastFrameIndex;

            if (lastFrameIndex < firstFrameIndex ||
                options.FrameIndex < firstFrameIndex ||
                options.FrameIndex > lastFrameIndex)
            {
                return new ErrorResponse("No profiler data for the requested frame.", new
                {
                    frame_index = options.FrameIndex,
                    first_frame_index = firstFrameIndex,
                    last_frame_index = lastFrameIndex,
                });
            }

            var searchedThreads = new List<object>();
            var matches = new List<object>();
            var rowBudget = new RowBudget(options.MaxRows);
            int scannedItemCount = 0;
            int matchCount = 0;
            bool validThreadFound = false;
            bool matchedRequestedThread = false;
            bool truncated = false;

            if (options.ThreadIndex.HasValue)
            {
                var threadResult = ScanThread(
                    options.ThreadIndex.Value,
                    options,
                    rowBudget,
                    searchedThreads,
                    matches);

                scannedItemCount += threadResult.ScannedItemCount;
                matchCount += threadResult.MatchCount;
                validThreadFound = threadResult.Valid;
                matchedRequestedThread = threadResult.Searched;
                truncated |= threadResult.Truncated;
            }
            else
            {
                int trailingInvalidThreads = 0;
                for (int threadIndex = 0; threadIndex < MaximumThreadScan; threadIndex++)
                {
                    var threadResult = ScanThread(
                        threadIndex,
                        options,
                        rowBudget,
                        searchedThreads,
                        matches);

                    if (!threadResult.Valid)
                    {
                        if (validThreadFound && ++trailingInvalidThreads >= MaximumTrailingInvalidThreads)
                            break;
                        continue;
                    }

                    validThreadFound = true;
                    trailingInvalidThreads = 0;
                    scannedItemCount += threadResult.ScannedItemCount;
                    matchCount += threadResult.MatchCount;
                    matchedRequestedThread |= threadResult.Searched;
                    truncated |= threadResult.Truncated;
                }
            }

            truncated |= rowBudget.Truncated;

            if (!validThreadFound)
            {
                return new ErrorResponse("No profiler data for the requested frame.", new
                {
                    frame_index = options.FrameIndex,
                    first_frame_index = firstFrameIndex,
                    last_frame_index = lastFrameIndex,
                    searched_threads = searchedThreads,
                    scanned_item_count = scannedItemCount,
                });
            }

            if (!matchedRequestedThread)
            {
                return new ErrorResponse("Requested profiler thread was not found or invalid.", new
                {
                    frame_index = options.FrameIndex,
                    first_frame_index = firstFrameIndex,
                    last_frame_index = lastFrameIndex,
                    requested_thread_index = options.ThreadIndex,
                    requested_thread_name = options.ThreadName,
                    searched_threads = searchedThreads,
                    scanned_item_count = scannedItemCount,
                });
            }

            if (matchCount == 0)
            {
                return new ErrorResponse("No matching marker found.", new
                {
                    frame_index = options.FrameIndex,
                    marker_filter = options.MarkerFilter,
                    match_mode = options.MatchMode,
                    searched_threads = searchedThreads,
                    scanned_item_count = scannedItemCount,
                    match_count = 0,
                    first_frame_index = firstFrameIndex,
                    last_frame_index = lastFrameIndex,
                });
            }

            return new SuccessResponse($"Found {matchCount} matching profiler marker(s).", new
            {
                frame_index = options.FrameIndex,
                searched_threads = searchedThreads,
                matches = matches,
                match_count = matchCount,
                rows_returned = rowBudget.RowsUsed,
                scanned_item_count = scannedItemCount,
                max_depth = options.MaxDepth,
                max_rows = options.MaxRows,
                truncated = truncated,
            });
        }

        private static Result<MarkerCalltreeOptions> BuildOptions(
            ToolParams p,
            int frameIndex,
            string markerFilter)
        {
            string matchMode = p.Get("match_mode", "contains").ToLowerInvariant();
            if (matchMode != "contains" && matchMode != "exact" && matchMode != "regex")
                return Result<MarkerCalltreeOptions>.Error(
                    $"Invalid match_mode '{matchMode}'. Valid values: contains, exact, regex.");

            Regex markerRegex = null;
            if (matchMode == "regex")
            {
                try
                {
                    markerRegex = new Regex(markerFilter, RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    return Result<MarkerCalltreeOptions>.Error(
                        $"Invalid marker_filter regex: {ex.Message}");
                }
            }

            var threadIndexResult = GetOptionalInt(p, "thread_index");
            if (!threadIndexResult.IsSuccess)
                return Result<MarkerCalltreeOptions>.Error(threadIndexResult.ErrorMessage);

            var maxDepthResult = GetOptionalInt(p, "max_depth");
            if (!maxDepthResult.IsSuccess)
                return Result<MarkerCalltreeOptions>.Error(maxDepthResult.ErrorMessage);

            var maxRowsResult = GetOptionalInt(p, "max_rows");
            if (!maxRowsResult.IsSuccess)
                return Result<MarkerCalltreeOptions>.Error(maxRowsResult.ErrorMessage);

            return Result<MarkerCalltreeOptions>.Success(new MarkerCalltreeOptions
            {
                FrameIndex = frameIndex,
                MarkerFilter = markerFilter,
                ThreadName = p.Get("thread_name"),
                ThreadIndex = threadIndexResult.Value,
                MatchMode = matchMode,
                MarkerRegex = markerRegex,
                MaxDepth = Clamp(maxDepthResult.Value ?? DefaultMaxDepth, 0, MaximumMaxDepth),
                MaxRows = Clamp(maxRowsResult.Value ?? DefaultMaxRows, 1, MaximumMaxRows),
                IncludeParents = p.GetBool("include_parents", true),
                IncludeChildren = p.GetBool("include_children", true),
            });
        }

        private static ThreadScanResult ScanThread(
            int threadIndex,
            MarkerCalltreeOptions options,
            RowBudget rowBudget,
            List<object> searchedThreads,
            List<object> matches)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                       options.FrameIndex,
                       threadIndex,
                       HierarchyFrameDataView.ViewModes.Default,
                       HierarchyFrameDataView.columnDontSort,
                       false))
            {
                if (view == null || !view.valid)
                {
                    return new ThreadScanResult
                    {
                        Valid = false,
                        Searched = false,
                    };
                }

                string threadName = view.threadName;
                var threadInfo = new Dictionary<string, object>
                {
                    ["thread_index"] = threadIndex,
                    ["thread_name"] = threadName,
                    ["thread_group_name"] = view.threadGroupName,
                    ["valid"] = true,
                    ["sample_count"] = view.sampleCount,
                };
                searchedThreads.Add(threadInfo);

                bool threadMatches = string.IsNullOrEmpty(options.ThreadName) ||
                                     string.Equals(
                                         threadName,
                                         options.ThreadName,
                                         StringComparison.OrdinalIgnoreCase);

                if (!threadMatches)
                {
                    return new ThreadScanResult
                    {
                        Valid = true,
                        Searched = false,
                    };
                }

                var matchedItems = new List<int>();
                int scannedItemCount = FindMatchingItems(view, options, matchedItems);
                threadInfo["scanned_item_count"] = scannedItemCount;
                threadInfo["match_count"] = matchedItems.Count;

                bool truncated = false;
                for (int i = 0; i < matchedItems.Count; i++)
                {
                    if (!rowBudget.TryConsume())
                    {
                        truncated = true;
                        break;
                    }

                    bool matchTruncated;
                    var match = BuildMatch(view, matchedItems[i], options, rowBudget, out matchTruncated);
                    truncated |= matchTruncated;
                    matches.Add(match);
                }

                return new ThreadScanResult
                {
                    Valid = true,
                    Searched = true,
                    ScannedItemCount = scannedItemCount,
                    MatchCount = matchedItems.Count,
                    Truncated = truncated,
                };
            }
        }

        private static int FindMatchingItems(
            HierarchyFrameDataView view,
            MarkerCalltreeOptions options,
            List<int> matchedItems)
        {
            int rootItemId = view.GetRootItemID();
            if (rootItemId < 0)
                return 0;

            int scannedItemCount = 0;
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(rootItemId);

            while (stack.Count > 0)
            {
                int itemId = stack.Pop();
                if (!visited.Add(itemId))
                    continue;

                scannedItemCount++;
                string markerName = view.GetItemName(itemId);
                if (MarkerMatches(markerName, options))
                    matchedItems.Add(itemId);

                var children = new List<int>();
                view.GetItemChildren(itemId, children);
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }

            return scannedItemCount;
        }

        private static bool MarkerMatches(string markerName, MarkerCalltreeOptions options)
        {
            if (string.IsNullOrEmpty(markerName))
                return false;

            switch (options.MatchMode)
            {
                case "exact":
                    return string.Equals(markerName, options.MarkerFilter, StringComparison.Ordinal);
                case "regex":
                    return options.MarkerRegex != null && options.MarkerRegex.IsMatch(markerName);
                default:
                    return markerName.IndexOf(options.MarkerFilter, StringComparison.Ordinal) >= 0;
            }
        }

        private static Dictionary<string, object> BuildMatch(
            HierarchyFrameDataView view,
            int itemId,
            MarkerCalltreeOptions options,
            RowBudget rowBudget,
            out bool truncated)
        {
            var match = BuildMarkerData(view, itemId);
            truncated = false;

            if (options.IncludeParents)
            {
                bool parentTruncated;
                match["parent_chain"] = BuildParentChain(view, itemId, rowBudget, out parentTruncated);
                truncated |= parentTruncated;
            }
            else
            {
                match["parent_chain"] = new List<object>();
            }

            if (options.IncludeChildren)
            {
                bool childTruncated;
                match["children"] = BuildChildren(view, itemId, 0, options, rowBudget, out childTruncated);
                truncated |= childTruncated;
            }
            else
            {
                match["children"] = new List<object>();
            }

            match["truncated"] = truncated;
            return match;
        }

        private static List<object> BuildParentChain(
            HierarchyFrameDataView view,
            int itemId,
            RowBudget rowBudget,
            out bool truncated)
        {
            truncated = false;
            var ancestorIds = new List<int>();
            view.GetItemAncestors(itemId, ancestorIds);
            ancestorIds.Sort((a, b) => view.GetItemDepth(a).CompareTo(view.GetItemDepth(b)));

            var parentChain = new List<object>();
            for (int i = 0; i < ancestorIds.Count; i++)
            {
                if (!rowBudget.TryConsume())
                {
                    truncated = true;
                    break;
                }

                parentChain.Add(BuildMarkerData(view, ancestorIds[i]));
            }

            return parentChain;
        }

        private static List<object> BuildChildren(
            HierarchyFrameDataView view,
            int parentItemId,
            int depthFromMatch,
            MarkerCalltreeOptions options,
            RowBudget rowBudget,
            out bool truncated)
        {
            truncated = false;
            var children = new List<int>();
            view.GetItemChildren(parentItemId, children);

            if (children.Count == 0)
                return new List<object>();

            if (depthFromMatch >= options.MaxDepth)
            {
                truncated = true;
                return new List<object>();
            }

            var childRows = new List<object>();
            for (int i = 0; i < children.Count; i++)
            {
                if (!rowBudget.TryConsume())
                {
                    truncated = true;
                    break;
                }

                var child = BuildMarkerData(view, children[i]);
                bool childTruncated;
                child["children"] = BuildChildren(
                    view,
                    children[i],
                    depthFromMatch + 1,
                    options,
                    rowBudget,
                    out childTruncated);

                if (childTruncated)
                    child["truncated"] = true;

                truncated |= childTruncated;
                childRows.Add(child);
            }

            return childRows;
        }

        private static Dictionary<string, object> BuildMarkerData(
            HierarchyFrameDataView view,
            int itemId)
        {
            var marker = new Dictionary<string, object>
            {
                ["item_id"] = itemId,
                ["marker_name"] = view.GetItemName(itemId),
                ["depth"] = view.GetItemDepth(itemId),
            };

            AddFloatColumn(marker, "total_time_ms", view, itemId, HierarchyFrameDataView.columnTotalTime);
            AddFloatColumn(marker, "self_time_ms", view, itemId, HierarchyFrameDataView.columnSelfTime);
            AddIntColumn(marker, "calls", view, itemId, HierarchyFrameDataView.columnCalls);
            AddGcAllocColumn(marker, view, itemId);

            return marker;
        }

        private static void AddFloatColumn(
            Dictionary<string, object> marker,
            string key,
            HierarchyFrameDataView view,
            int itemId,
            int column)
        {
            float? value = TryGetColumnSingle(view, itemId, column);
            if (value.HasValue)
                marker[key] = Math.Round(value.Value, 4);
        }

        private static void AddIntColumn(
            Dictionary<string, object> marker,
            string key,
            HierarchyFrameDataView view,
            int itemId,
            int column)
        {
            float? value = TryGetColumnSingle(view, itemId, column);
            if (value.HasValue)
                marker[key] = (int)Math.Round(value.Value);
        }

        private static void AddGcAllocColumn(
            Dictionary<string, object> marker,
            HierarchyFrameDataView view,
            int itemId)
        {
            float? value = TryGetColumnSingle(view, itemId, HierarchyFrameDataView.columnGcMemory);
            if (value.HasValue)
                marker["gc_alloc_bytes"] = (long)Math.Round(value.Value);
        }

        private static float? TryGetColumnSingle(
            HierarchyFrameDataView view,
            int itemId,
            int column)
        {
            try
            {
                float value = view.GetItemColumnDataAsSingle(itemId, column);
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return null;
                return value;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static Result<int> GetRequiredInt(ToolParams p, string key)
        {
            var value = GetOptionalInt(p, key);
            if (!value.IsSuccess)
                return Result<int>.Error(value.ErrorMessage);
            if (!value.Value.HasValue)
                return Result<int>.Error($"'{key}' parameter is required.");
            return Result<int>.Success(value.Value.Value);
        }

        private static Result<int?> GetOptionalInt(ToolParams p, string key)
        {
            if (!p.Has(key))
                return Result<int?>.Success(null);

            var token = p.GetRaw(key);
            if (token == null || token.Type == JTokenType.Null)
                return Result<int?>.Success(null);

            int value;
            if (token.Type == JTokenType.Integer)
                return Result<int?>.Success(token.Value<int>());

            if (int.TryParse(token.ToString(), out value))
                return Result<int?>.Success(value);

            return Result<int?>.Error($"'{key}' parameter must be an integer.");
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static ErrorResponse ProfilerApiUnavailable(Exception ex)
        {
            return new ErrorResponse("Unity profiler hierarchy API unavailable in this editor version.", new
            {
                exception_type = ex.GetType().Name,
                exception_message = ex.Message,
            });
        }

        private sealed class MarkerCalltreeOptions
        {
            public int FrameIndex { get; set; }
            public string MarkerFilter { get; set; }
            public string ThreadName { get; set; }
            public int? ThreadIndex { get; set; }
            public string MatchMode { get; set; }
            public Regex MarkerRegex { get; set; }
            public int MaxDepth { get; set; }
            public int MaxRows { get; set; }
            public bool IncludeParents { get; set; }
            public bool IncludeChildren { get; set; }
        }

        private sealed class ThreadScanResult
        {
            public bool Valid { get; set; }
            public bool Searched { get; set; }
            public int ScannedItemCount { get; set; }
            public int MatchCount { get; set; }
            public bool Truncated { get; set; }
        }

        private sealed class RowBudget
        {
            private readonly int _maxRows;

            public int RowsUsed { get; private set; }
            public bool Truncated { get; private set; }

            public RowBudget(int maxRows)
            {
                _maxRows = maxRows;
            }

            public bool TryConsume()
            {
                if (RowsUsed >= _maxRows)
                {
                    Truncated = true;
                    return false;
                }

                RowsUsed++;
                return true;
            }
        }
    }
}
