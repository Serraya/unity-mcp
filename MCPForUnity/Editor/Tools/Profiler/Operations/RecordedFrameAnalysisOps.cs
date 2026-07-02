using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class RecordedFrameAnalysisOps
    {
        private const int DefaultTopN = 50;
        private const int DefaultFrameSummaryTopN = 20;
        private const int MaximumTopN = 500;
        private const int DefaultMaxFrames = 500;
        private const int DefaultFrameSummaryMaxFrames = 1000;
        private const int DefaultExportMaxFrames = 2000;
        private const int MaximumMaxFrames = 5000;
        private const int DefaultMaxMarkerRows = 20000;
        private const int MaximumMaxMarkerRows = 100000;
        private const int MaximumThreadScan = 128;
        private const int MaximumTrailingInvalidThreads = 8;
        private const int MaximumOutputDirectoryAttempts = 1000;

        private static readonly string[] FrameTimeColumns =
        {
            "Frame Offset",
            "Frame Index",
            "Frame Time (ms)",
            "Time from first frame (ms)",
        };

        private static readonly string[] MarkerTableColumns =
        {
            "Name",
            "Median Time",
            "Min Time",
            "Max Time",
            "Median Frame Index",
            "Min Frame Index",
            "Max Frame Index",
            "Min Depth",
            "Max Depth",
            "Total Time",
            "Mean Time",
            "Time Lower Quartile",
            "Time Upper Quartile",
            "Count Total",
            "Count Median",
            "Count Min",
            "Count Max",
            "Number of frames containing Marker",
            "First Frame Index",
            "Time Min Individual",
            "Time Max Individual",
            "Min Individual Frame",
            "Max Individual Frame",
            "Time at Median Frame",
        };

        internal static object GetFrameSummary(JObject @params)
        {
            try
            {
                return GetFrameSummaryImpl(@params);
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

        internal static object GetHotMarkers(JObject @params)
        {
            try
            {
                return GetHotMarkersImpl(@params);
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

        internal static object FindMarker(JObject @params)
        {
            try
            {
                return FindMarkerImpl(@params);
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

        internal static object ExportProfileTables(JObject @params)
        {
            try
            {
                return ExportProfileTablesImpl(@params);
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

        private static object GetFrameSummaryImpl(JObject @params)
        {
            var p = new ToolParams(@params);
            var optionsResult = BuildOptions(p, false, false, DefaultFrameSummaryTopN, DefaultFrameSummaryMaxFrames);
            if (!optionsResult.IsSuccess)
                return new ErrorResponse(optionsResult.ErrorMessage);

            var options = optionsResult.Value;
            object rangeError;
            var range = ResolveFrameRange(options, out rangeError);
            if (rangeError != null)
                return rangeError;

            var searchedThreads = new ThreadCatalog();
            var frameSamples = new List<FrameTimeSample>();
            bool validThreadFound = false;
            bool matchedRequestedThread = false;
            bool truncated = false;
            int scannedThreadCount = 0;
            int scannedFrameCount = 0;

            for (int frameIndex = range.StartFrame; frameIndex <= range.EndFrame; frameIndex++)
            {
                if (scannedFrameCount >= options.MaxFrames)
                {
                    truncated = true;
                    break;
                }

                var frameResult = ScanFrameForSummary(frameIndex, options, searchedThreads);
                scannedFrameCount++;
                scannedThreadCount += frameResult.ScannedThreadCount;
                validThreadFound |= frameResult.ValidThreadFound;
                matchedRequestedThread |= frameResult.MatchedRequestedThread;

                if (frameResult.MatchedRequestedThread && frameResult.FrameTimeMs.HasValue)
                    frameSamples.Add(new FrameTimeSample(frameIndex, frameResult.FrameTimeMs.Value));
            }

            var threadError = ValidateThreadSearch(
                validThreadFound,
                matchedRequestedThread,
                options,
                range,
                searchedThreads,
                scannedFrameCount,
                scannedThreadCount);
            if (threadError != null)
                return threadError;

            if (frameSamples.Count == 0)
            {
                return new ErrorResponse("No recorded frame timing data found for the requested range.", new
                {
                    available_range = range.AvailableRange,
                    requested_range = range.RequestedRange,
                    searched_threads = searchedThreads.ToResponse(),
                    scanned_frame_count = scannedFrameCount,
                    scanned_thread_count = scannedThreadCount,
                });
            }

            return new SuccessResponse($"Summarized {frameSamples.Count} recorded profiler frame(s).", new
            {
                available_range = range.AvailableRange,
                requested_range = range.RequestedRange,
                searched_threads = searchedThreads.ToResponse(),
                frame_count = frameSamples.Count,
                scanned_frame_count = scannedFrameCount,
                scanned_thread_count = scannedThreadCount,
                max_frames = options.MaxFrames,
                frame_time_ms = BuildFrameTimeStats(frameSamples),
                worst_frames = BuildWorstFrames(frameSamples, options.TopN),
                truncated = truncated,
            });
        }

        private static object GetHotMarkersImpl(JObject @params)
        {
            var p = new ToolParams(@params);
            var optionsResult = BuildOptions(p, false, true, DefaultTopN, DefaultMaxFrames);
            if (!optionsResult.IsSuccess)
                return new ErrorResponse(optionsResult.ErrorMessage);

            var options = optionsResult.Value;
            object rangeError;
            var range = ResolveFrameRange(options, out rangeError);
            if (rangeError != null)
                return rangeError;

            var searchedThreads = new ThreadCatalog();
            var markersByName = new Dictionary<string, MarkerAggregate>(StringComparer.Ordinal);
            bool validThreadFound = false;
            bool matchedRequestedThread = false;
            bool truncated = false;
            int scannedFrameCount = 0;
            int scannedThreadCount = 0;
            int scannedItemCount = 0;

            for (int frameIndex = range.StartFrame; frameIndex <= range.EndFrame; frameIndex++)
            {
                if (scannedFrameCount >= options.MaxFrames)
                {
                    truncated = true;
                    break;
                }

                var frameResult = ScanFrameForMarkers(frameIndex, options, searchedThreads, markersByName, null);
                scannedFrameCount++;
                scannedThreadCount += frameResult.ScannedThreadCount;
                scannedItemCount += frameResult.ScannedItemCount;
                validThreadFound |= frameResult.ValidThreadFound;
                matchedRequestedThread |= frameResult.MatchedRequestedThread;
            }

            var threadError = ValidateThreadSearch(
                validThreadFound,
                matchedRequestedThread,
                options,
                range,
                searchedThreads,
                scannedFrameCount,
                scannedThreadCount);
            if (threadError != null)
                return threadError;

            if (markersByName.Count == 0)
                return NoMarkerError(range, options, searchedThreads, scannedFrameCount, scannedThreadCount, scannedItemCount, truncated);

            var rows = BuildMarkerRows(markersByName);
            SortMarkerRows(rows, options.SortBy);
            if (rows.Count > options.TopN)
                truncated = true;

            var markerRows = new List<object>();
            int rowsToReturn = Math.Min(options.TopN, rows.Count);
            for (int i = 0; i < rowsToReturn; i++)
                markerRows.Add(rows[i].ToResponse());

            return new SuccessResponse($"Found {rows.Count} recorded profiler marker(s).", new
            {
                available_range = range.AvailableRange,
                requested_range = range.RequestedRange,
                searched_threads = searchedThreads.ToResponse(),
                markers = markerRows,
                marker_count = rows.Count,
                rows_returned = markerRows.Count,
                scanned_frame_count = scannedFrameCount,
                scanned_thread_count = scannedThreadCount,
                scanned_item_count = scannedItemCount,
                max_frames = options.MaxFrames,
                sort_by = options.SortBy,
                marker_filter = options.MarkerFilter,
                match_mode = options.MatchMode,
                truncated = truncated,
            });
        }

        private static object FindMarkerImpl(JObject @params)
        {
            var p = new ToolParams(@params);
            var markerFilterResult = p.GetRequired("marker_filter");
            if (!markerFilterResult.IsSuccess)
                return new ErrorResponse(markerFilterResult.ErrorMessage);

            var optionsResult = BuildOptions(p, true, false, DefaultTopN, DefaultMaxFrames);
            if (!optionsResult.IsSuccess)
                return new ErrorResponse(optionsResult.ErrorMessage);

            var options = optionsResult.Value;
            object rangeError;
            var range = ResolveFrameRange(options, out rangeError);
            if (rangeError != null)
                return rangeError;

            var searchedThreads = new ThreadCatalog();
            var matches = new List<MarkerFrameThreadMatch>();
            bool validThreadFound = false;
            bool matchedRequestedThread = false;
            bool truncated = false;
            int scannedFrameCount = 0;
            int scannedThreadCount = 0;
            int scannedItemCount = 0;

            for (int frameIndex = range.StartFrame; frameIndex <= range.EndFrame; frameIndex++)
            {
                if (scannedFrameCount >= options.MaxFrames)
                {
                    truncated = true;
                    break;
                }

                var frameResult = ScanFrameForMarkers(frameIndex, options, searchedThreads, null, matches);
                scannedFrameCount++;
                scannedThreadCount += frameResult.ScannedThreadCount;
                scannedItemCount += frameResult.ScannedItemCount;
                validThreadFound |= frameResult.ValidThreadFound;
                matchedRequestedThread |= frameResult.MatchedRequestedThread;
            }

            var threadError = ValidateThreadSearch(
                validThreadFound,
                matchedRequestedThread,
                options,
                range,
                searchedThreads,
                scannedFrameCount,
                scannedThreadCount);
            if (threadError != null)
                return threadError;

            if (matches.Count == 0)
                return NoMarkerError(range, options, searchedThreads, scannedFrameCount, scannedThreadCount, scannedItemCount, truncated);

            matches.Sort(CompareFindMarkerMatches);
            if (matches.Count > options.TopN)
                truncated = true;

            var rows = new List<object>();
            int rowsToReturn = Math.Min(options.TopN, matches.Count);
            for (int i = 0; i < rowsToReturn; i++)
                rows.Add(matches[i].ToResponse());

            return new SuccessResponse($"Found marker on {matches.Count} frame/thread row(s).", new
            {
                available_range = range.AvailableRange,
                requested_range = range.RequestedRange,
                searched_threads = searchedThreads.ToResponse(),
                matches = rows,
                match_count = matches.Count,
                rows_returned = rows.Count,
                scanned_frame_count = scannedFrameCount,
                scanned_thread_count = scannedThreadCount,
                scanned_item_count = scannedItemCount,
                max_frames = options.MaxFrames,
                marker_filter = options.MarkerFilter,
                match_mode = options.MatchMode,
                truncated = truncated,
            });
        }

        private static object ExportProfileTablesImpl(JObject @params)
        {
            var p = new ToolParams(@params);
            var optionsResult = BuildExportOptions(p);
            if (!optionsResult.IsSuccess)
                return new ErrorResponse(optionsResult.ErrorMessage);

            var options = optionsResult.Value;
            var rangeOptions = new RecordedOptions
            {
                StartFrame = options.StartFrame,
                EndFrame = options.EndFrame,
                ThreadName = options.ThreadName,
                ThreadIndex = options.ThreadIndex,
            };

            object rangeError;
            var range = ResolveFrameRange(rangeOptions, out rangeError);
            if (rangeError != null)
                return rangeError;

            var exportData = ScanExportData(range, options);

            if (options.IncludeMarkerTable)
            {
                var threadError = ValidateThreadSearch(
                    exportData.ValidThreadFound,
                    exportData.MatchedRequestedThread,
                    rangeOptions,
                    range,
                    exportData.SearchedThreads,
                    exportData.ScannedFrameCount,
                    exportData.ScannedThreadCount);
                if (threadError != null)
                    return threadError;
            }

            if (options.IncludeFrameTable && exportData.FrameRows.Count == 0)
            {
                return new ErrorResponse("No recorded frame timing data found for the requested range.", new
                {
                    available_range = range.AvailableRange,
                    requested_range = range.RequestedRange,
                    scanned_frame_count = exportData.ScannedFrameCount,
                });
            }

            var markerRows = BuildMarkerExportRows(exportData.MarkersByName, exportData.FrameSummaryMedianFrame);
            SortMarkerExportRows(markerRows);
            bool markerRowsTruncated = markerRows.Count > options.MaxMarkerRows;
            int markerRowsToWrite = Math.Min(markerRows.Count, options.MaxMarkerRows);

            var outputDirResult = ResolveExportOutputDirectory(options);
            if (!outputDirResult.IsSuccess)
                return new ErrorResponse(outputDirResult.ErrorMessage);

            string outputDir = outputDirResult.Value;
            Directory.CreateDirectory(outputDir);

            var files = new Dictionary<string, object>();
            if (options.IncludeFrameTable)
            {
                string framePath = Path.Combine(outputDir, "frameTime.csv");
                WriteFrameTimeCsv(framePath, exportData.FrameRows);
                files["frame_time"] = new Dictionary<string, object>
                {
                    ["path"] = framePath,
                    ["row_count"] = exportData.FrameRows.Count,
                    ["columns"] = FrameTimeColumns,
                };
            }

            if (options.IncludeMarkerTable)
            {
                string markerPath = Path.Combine(outputDir, "markerTable.csv");
                WriteMarkerTableCsv(markerPath, markerRows, markerRowsToWrite);
                files["marker_table"] = new Dictionary<string, object>
                {
                    ["path"] = markerPath,
                    ["row_count"] = markerRowsToWrite,
                    ["columns"] = MarkerTableColumns,
                };
            }

            int exportedEndFrame = exportData.ScannedFrameCount > 0
                ? range.StartFrame + exportData.ScannedFrameCount - 1
                : range.StartFrame;

            return new SuccessResponse("Exported recorded profiler profile tables.", new
            {
                output_dir = outputDir,
                files = files,
                available_range = range.AvailableRange,
                requested_range = range.RequestedRange,
                exported_range = new Dictionary<string, object>
                {
                    ["start_frame"] = range.StartFrame,
                    ["end_frame"] = exportedEndFrame,
                },
                searched_threads = exportData.SearchedThreads.ToResponse(),
                scanned_frame_count = exportData.ScannedFrameCount,
                scanned_thread_count = exportData.ScannedThreadCount,
                scanned_sample_count = exportData.ScannedItemCount,
                marker_count = markerRows.Count,
                max_frames = options.MaxFrames,
                max_marker_rows = options.MaxMarkerRows,
                truncated = exportData.FrameTruncated || markerRowsTruncated,
                truncation = new Dictionary<string, object>
                {
                    ["frames"] = exportData.FrameTruncated,
                    ["marker_rows"] = markerRowsTruncated,
                },
                profile_analyzer_compatibility = new Dictionary<string, object>
                {
                    ["exact"] = false,
                    ["missing_columns"] = new string[0],
                    ["notes"] = new[]
                    {
                        "CSV headers, order, semicolon separation, invariant-culture numeric formatting, and marker-name quoting mirror ProfileAnalyzerExportWindow.",
                        "Rows are computed directly from Unity RawFrameDataView instead of a Profile Analyzer ProfileDataView, so Profile Analyzer UI frame remapping and view-specific remove-marker/self-time settings are not applied.",
                    },
                },
            });
        }

        private static ErrorResponse NoMarkerError(
            ResolvedFrameRange range,
            RecordedOptions options,
            ThreadCatalog searchedThreads,
            int scannedFrameCount,
            int scannedThreadCount,
            int scannedItemCount,
            bool truncated)
        {
            return new ErrorResponse("No matching marker found.", new
            {
                available_range = range.AvailableRange,
                requested_range = range.RequestedRange,
                marker_filter = options.MarkerFilter,
                match_mode = options.MatchMode,
                searched_threads = searchedThreads.ToResponse(),
                scanned_frame_count = scannedFrameCount,
                scanned_thread_count = scannedThreadCount,
                scanned_item_count = scannedItemCount,
                match_count = 0,
                truncated = truncated,
            });
        }

        private static Result<RecordedOptions> BuildOptions(
            ToolParams p,
            bool requireMarkerFilter,
            bool includeSortBy,
            int defaultTopN,
            int defaultMaxFrames)
        {
            var startFrameResult = GetOptionalInt(p, "start_frame");
            if (!startFrameResult.IsSuccess)
                return Result<RecordedOptions>.Error(startFrameResult.ErrorMessage);

            var endFrameResult = GetOptionalInt(p, "end_frame");
            if (!endFrameResult.IsSuccess)
                return Result<RecordedOptions>.Error(endFrameResult.ErrorMessage);

            var threadIndexResult = GetOptionalInt(p, "thread_index");
            if (!threadIndexResult.IsSuccess)
                return Result<RecordedOptions>.Error(threadIndexResult.ErrorMessage);

            var topNResult = GetOptionalInt(p, "top_n");
            if (!topNResult.IsSuccess)
                return Result<RecordedOptions>.Error(topNResult.ErrorMessage);

            var maxFramesResult = GetOptionalInt(p, "max_frames");
            if (!maxFramesResult.IsSuccess)
                return Result<RecordedOptions>.Error(maxFramesResult.ErrorMessage);

            string markerFilter = p.Get("marker_filter");
            if (requireMarkerFilter && string.IsNullOrEmpty(markerFilter))
                return Result<RecordedOptions>.Error("'marker_filter' parameter is required.");

            string matchMode = p.Get("match_mode", "contains").ToLowerInvariant();
            if (matchMode != "contains" && matchMode != "exact" && matchMode != "regex")
                return Result<RecordedOptions>.Error(
                    $"Invalid match_mode '{matchMode}'. Valid values: contains, exact, regex.");

            Regex markerRegex = null;
            if (matchMode == "regex" && !string.IsNullOrEmpty(markerFilter))
            {
                try
                {
                    markerRegex = new Regex(markerFilter, RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    return Result<RecordedOptions>.Error($"Invalid marker_filter regex: {ex.Message}");
                }
            }

            string sortBy = p.Get("sort_by", "total_time").ToLowerInvariant();
            if (includeSortBy && !IsValidSortBy(sortBy))
            {
                return Result<RecordedOptions>.Error(
                    $"Invalid sort_by '{sortBy}'. Valid values: total_time, self_time, max_total_time, max_self_time, call_count, frame_count.");
            }

            return Result<RecordedOptions>.Success(new RecordedOptions
            {
                StartFrame = startFrameResult.Value,
                EndFrame = endFrameResult.Value,
                ThreadName = p.Get("thread_name"),
                ThreadIndex = threadIndexResult.Value,
                MarkerFilter = markerFilter,
                MatchMode = matchMode,
                MarkerRegex = markerRegex,
                SortBy = sortBy,
                TopN = Clamp(topNResult.Value ?? defaultTopN, 1, MaximumTopN),
                MaxFrames = Clamp(maxFramesResult.Value ?? defaultMaxFrames, 1, MaximumMaxFrames),
            });
        }

        private static Result<ExportOptions> BuildExportOptions(ToolParams p)
        {
            var startFrameResult = GetOptionalInt(p, "start_frame");
            if (!startFrameResult.IsSuccess)
                return Result<ExportOptions>.Error(startFrameResult.ErrorMessage);

            var endFrameResult = GetOptionalInt(p, "end_frame");
            if (!endFrameResult.IsSuccess)
                return Result<ExportOptions>.Error(endFrameResult.ErrorMessage);

            var threadIndexResult = GetOptionalInt(p, "thread_index");
            if (!threadIndexResult.IsSuccess)
                return Result<ExportOptions>.Error(threadIndexResult.ErrorMessage);

            var maxFramesResult = GetOptionalInt(p, "max_frames");
            if (!maxFramesResult.IsSuccess)
                return Result<ExportOptions>.Error(maxFramesResult.ErrorMessage);

            var maxMarkerRowsResult = GetOptionalInt(p, "max_marker_rows");
            if (!maxMarkerRowsResult.IsSuccess)
                return Result<ExportOptions>.Error(maxMarkerRowsResult.ErrorMessage);

            bool includeFrameTable = !p.Has("include_frame_table") || p.GetBool("include_frame_table", true);
            bool includeMarkerTable = !p.Has("include_marker_table") || p.GetBool("include_marker_table", true);
            if (!includeFrameTable && !includeMarkerTable)
                return Result<ExportOptions>.Error("At least one of include_frame_table or include_marker_table must be true.");

            string outputDir = p.Get("output_dir");
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                string fullPath;
                if (!TryGetFullPath(outputDir, out fullPath))
                    return Result<ExportOptions>.Error($"Invalid output_dir '{outputDir}'.");

                if (IsInsideAssetsDirectory(fullPath))
                    return Result<ExportOptions>.Error("output_dir must not be inside the Unity Assets directory.");

                outputDir = fullPath;
            }
            else
            {
                outputDir = null;
            }

            return Result<ExportOptions>.Success(new ExportOptions
            {
                OutputDir = outputDir,
                StartFrame = startFrameResult.Value,
                EndFrame = endFrameResult.Value,
                ThreadName = p.Get("thread_name"),
                ThreadIndex = threadIndexResult.Value,
                IncludeFrameTable = includeFrameTable,
                IncludeMarkerTable = includeMarkerTable,
                MaxFrames = Clamp(maxFramesResult.Value ?? DefaultExportMaxFrames, 1, MaximumMaxFrames),
                MaxMarkerRows = Clamp(maxMarkerRowsResult.Value ?? DefaultMaxMarkerRows, 1, MaximumMaxMarkerRows),
                Overwrite = p.GetBool("overwrite", false),
            });
        }

        private static ResolvedFrameRange ResolveFrameRange(RecordedOptions options, out object errorResponse)
        {
            errorResponse = null;
            int firstFrameIndex = ProfilerDriver.firstFrameIndex;
            int lastFrameIndex = ProfilerDriver.lastFrameIndex;
            var availableRange = new Dictionary<string, object>
            {
                ["first_frame"] = firstFrameIndex,
                ["last_frame"] = lastFrameIndex,
            };

            if (lastFrameIndex < firstFrameIndex)
            {
                errorResponse = new ErrorResponse("No recorded profiler frames available.", new
                {
                    available_range = availableRange,
                });
                return null;
            }

            int startFrame = options.StartFrame ?? firstFrameIndex;
            int endFrame = options.EndFrame ?? lastFrameIndex;
            var requestedRange = new Dictionary<string, object>
            {
                ["start_frame"] = startFrame,
                ["end_frame"] = endFrame,
            };

            if (startFrame > endFrame)
            {
                errorResponse = new ErrorResponse("Invalid frame range: start_frame must be <= end_frame.", new
                {
                    available_range = availableRange,
                    requested_range = requestedRange,
                });
                return null;
            }

            if (startFrame < firstFrameIndex || endFrame > lastFrameIndex)
            {
                errorResponse = new ErrorResponse("Requested frame range is outside recorded profiler data.", new
                {
                    available_range = availableRange,
                    requested_range = requestedRange,
                });
                return null;
            }

            return new ResolvedFrameRange
            {
                FirstFrame = firstFrameIndex,
                LastFrame = lastFrameIndex,
                StartFrame = startFrame,
                EndFrame = endFrame,
            };
        }

        private static FrameScanResult ScanFrameForSummary(
            int frameIndex,
            RecordedOptions options,
            ThreadCatalog searchedThreads)
        {
            var result = new FrameScanResult();
            if (options.ThreadIndex.HasValue)
            {
                ScanSummaryThread(frameIndex, options.ThreadIndex.Value, options, searchedThreads, result);
                return result;
            }

            int trailingInvalidThreads = 0;
            for (int threadIndex = 0; threadIndex < MaximumThreadScan; threadIndex++)
            {
                bool valid = ScanSummaryThread(frameIndex, threadIndex, options, searchedThreads, result);
                if (!valid)
                {
                    if (result.ValidThreadFound && ++trailingInvalidThreads >= MaximumTrailingInvalidThreads)
                        break;
                    continue;
                }

                trailingInvalidThreads = 0;
            }

            return result;
        }

        private static bool ScanSummaryThread(
            int frameIndex,
            int threadIndex,
            RecordedOptions options,
            ThreadCatalog searchedThreads,
            FrameScanResult result)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                       frameIndex,
                       threadIndex,
                       HierarchyFrameDataView.ViewModes.Default,
                       HierarchyFrameDataView.columnDontSort,
                       false))
            {
                result.ScannedThreadCount++;
                if (view == null || !view.valid)
                    return false;

                result.ValidThreadFound = true;
                bool threadMatches = ThreadMatches(view.threadName, options);
                searchedThreads.Add(view, threadMatches);

                if (!threadMatches)
                    return true;

                result.MatchedRequestedThread = true;
                if (!result.FrameTimeMs.HasValue)
                    result.FrameTimeMs = view.frameTimeMs;

                return true;
            }
        }

        private static FrameScanResult ScanFrameForMarkers(
            int frameIndex,
            RecordedOptions options,
            ThreadCatalog searchedThreads,
            Dictionary<string, MarkerAggregate> markersByName,
            List<MarkerFrameThreadMatch> matches)
        {
            var result = new FrameScanResult();
            if (options.ThreadIndex.HasValue)
            {
                ScanMarkerThread(frameIndex, options.ThreadIndex.Value, options, searchedThreads, markersByName, matches, result);
                return result;
            }

            int trailingInvalidThreads = 0;
            for (int threadIndex = 0; threadIndex < MaximumThreadScan; threadIndex++)
            {
                bool valid = ScanMarkerThread(frameIndex, threadIndex, options, searchedThreads, markersByName, matches, result);
                if (!valid)
                {
                    if (result.ValidThreadFound && ++trailingInvalidThreads >= MaximumTrailingInvalidThreads)
                        break;
                    continue;
                }

                trailingInvalidThreads = 0;
            }

            return result;
        }

        private static bool ScanMarkerThread(
            int frameIndex,
            int threadIndex,
            RecordedOptions options,
            ThreadCatalog searchedThreads,
            Dictionary<string, MarkerAggregate> markersByName,
            List<MarkerFrameThreadMatch> matches,
            FrameScanResult result)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                       frameIndex,
                       threadIndex,
                       HierarchyFrameDataView.ViewModes.Default,
                       HierarchyFrameDataView.columnDontSort,
                       false))
            {
                result.ScannedThreadCount++;
                if (view == null || !view.valid)
                    return false;

                result.ValidThreadFound = true;
                bool threadMatches = ThreadMatches(view.threadName, options);
                searchedThreads.Add(view, threadMatches);
                if (!threadMatches)
                    return true;

                result.MatchedRequestedThread = true;
                result.ScannedItemCount += ScanMarkerItems(frameIndex, view, options, markersByName, matches);
                return true;
            }
        }

        private static int ScanMarkerItems(
            int frameIndex,
            HierarchyFrameDataView view,
            RecordedOptions options,
            Dictionary<string, MarkerAggregate> markersByName,
            List<MarkerFrameThreadMatch> matches)
        {
            int rootItemId = view.GetRootItemID();
            if (rootItemId < 0)
                return 0;

            var threadMatch = matches != null
                ? new MarkerFrameThreadMatch(frameIndex, view.threadIndex, view.threadName)
                : null;
            int scannedItemCount = 0;
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            PushChildren(view, rootItemId, stack);

            while (stack.Count > 0)
            {
                int itemId = stack.Pop();
                if (!visited.Add(itemId))
                    continue;

                scannedItemCount++;
                string markerName = view.GetItemName(itemId);
                if (MarkerMatches(markerName, options))
                {
                    double totalTime = GetColumnDoubleOrZero(view, itemId, HierarchyFrameDataView.columnTotalTime);
                    double selfTime = GetColumnDoubleOrZero(view, itemId, HierarchyFrameDataView.columnSelfTime);
                    int calls = GetColumnIntOrZero(view, itemId, HierarchyFrameDataView.columnCalls);

                    if (markersByName != null)
                    {
                        MarkerAggregate aggregate;
                        if (!markersByName.TryGetValue(markerName, out aggregate))
                        {
                            aggregate = new MarkerAggregate(markerName);
                            markersByName[markerName] = aggregate;
                        }
                        aggregate.Add(frameIndex, totalTime, selfTime, calls);
                    }

                    if (threadMatch != null)
                        threadMatch.Add(markerName, totalTime, selfTime, calls);
                }

                PushChildren(view, itemId, stack);
            }

            if (threadMatch != null && threadMatch.MatchedItemCount > 0)
                matches.Add(threadMatch);

            return scannedItemCount;
        }

        private static ExportData ScanExportData(ResolvedFrameRange range, ExportOptions options)
        {
            var exportData = new ExportData();
            double timeFromFirstFrameMs = 0.0;

            for (int frameIndex = range.StartFrame; frameIndex <= range.EndFrame; frameIndex++)
            {
                if (exportData.ScannedFrameCount >= options.MaxFrames)
                {
                    exportData.FrameTruncated = true;
                    break;
                }

                if (options.IncludeFrameTable || options.IncludeMarkerTable)
                    CaptureFrameTime(frameIndex, exportData.ScannedFrameCount, exportData.FrameRows, ref timeFromFirstFrameMs);

                if (options.IncludeMarkerTable)
                {
                    var frameResult = ScanRawFrameForMarkers(frameIndex, options, exportData.SearchedThreads, exportData.MarkersByName);
                    exportData.ScannedThreadCount += frameResult.ScannedThreadCount;
                    exportData.ScannedItemCount += frameResult.ScannedItemCount;
                    exportData.ValidThreadFound |= frameResult.ValidThreadFound;
                    exportData.MatchedRequestedThread |= frameResult.MatchedRequestedThread;
                }

                exportData.ScannedFrameCount++;
            }

            exportData.FrameSummaryMedianFrame = GetMedianFrameIndexForExport(exportData.FrameRows);
            return exportData;
        }

        private static void CaptureFrameTime(
            int frameIndex,
            int frameOffset,
            List<FrameTimeExportRow> rows,
            ref double timeFromFirstFrameMs)
        {
            using (var view = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
            {
                if (view == null || !view.valid)
                    return;

                double frameTimeMs = view.frameTimeMs;
                if (double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs))
                    return;

                rows.Add(new FrameTimeExportRow(frameOffset, frameIndex, frameTimeMs, timeFromFirstFrameMs));
                timeFromFirstFrameMs += frameTimeMs;
            }
        }

        private static FrameScanResult ScanRawFrameForMarkers(
            int frameIndex,
            ExportOptions options,
            ThreadCatalog searchedThreads,
            Dictionary<string, ExportMarkerAggregate> markersByName)
        {
            var result = new FrameScanResult();
            if (options.ThreadIndex.HasValue)
            {
                ScanRawMarkerThread(frameIndex, options.ThreadIndex.Value, options, searchedThreads, markersByName, result);
                return result;
            }

            int trailingInvalidThreads = 0;
            for (int threadIndex = 0; threadIndex < MaximumThreadScan; threadIndex++)
            {
                bool valid = ScanRawMarkerThread(frameIndex, threadIndex, options, searchedThreads, markersByName, result);
                if (!valid)
                {
                    if (result.ValidThreadFound && ++trailingInvalidThreads >= MaximumTrailingInvalidThreads)
                        break;
                    continue;
                }

                trailingInvalidThreads = 0;
            }

            return result;
        }

        private static bool ScanRawMarkerThread(
            int frameIndex,
            int threadIndex,
            ExportOptions options,
            ThreadCatalog searchedThreads,
            Dictionary<string, ExportMarkerAggregate> markersByName,
            FrameScanResult result)
        {
            using (var view = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                result.ScannedThreadCount++;
                if (view == null || !view.valid)
                    return false;

                result.ValidThreadFound = true;
                bool threadMatches = ExportThreadMatches(view.threadName, options);
                searchedThreads.Add(view.threadIndex, view.threadName, view.threadGroupName, view.sampleCount, threadMatches);

                if (!threadMatches)
                    return true;

                result.MatchedRequestedThread = true;
                result.ScannedItemCount += ScanRawMarkerSamples(frameIndex, view, markersByName);
                return true;
            }
        }

        private static int ScanRawMarkerSamples(
            int frameIndex,
            RawFrameDataView view,
            Dictionary<string, ExportMarkerAggregate> markersByName)
        {
            int scannedSampleCount = 0;
            var depthStack = new Stack<int>();

            for (int sampleIndex = 1; sampleIndex < view.sampleCount; sampleIndex++)
            {
                double durationMs = view.GetSampleTimeMs(sampleIndex);
                if (!double.IsNaN(durationMs) && !double.IsInfinity(durationMs) && durationMs >= 0.0)
                {
                    int depth = 1 + depthStack.Count;
                    string markerName = view.GetSampleName(sampleIndex);
                    if (!string.IsNullOrEmpty(markerName))
                    {
                        ExportMarkerAggregate aggregate;
                        if (!markersByName.TryGetValue(markerName, out aggregate))
                        {
                            aggregate = new ExportMarkerAggregate(markerName);
                            markersByName[markerName] = aggregate;
                        }

                        aggregate.Add(frameIndex, durationMs, depth);
                        scannedSampleCount++;
                    }
                }

                UpdateRawSampleDepthStack(view.GetSampleChildrenCount(sampleIndex), depthStack);
            }

            return scannedSampleCount;
        }

        private static void UpdateRawSampleDepthStack(int childrenCount, Stack<int> depthStack)
        {
            if (childrenCount > 0)
            {
                depthStack.Push(childrenCount);
                return;
            }

            while (depthStack.Count > 0)
            {
                int remainingChildren = depthStack.Pop();
                if (remainingChildren > 1)
                {
                    depthStack.Push(remainingChildren - 1);
                    break;
                }
            }
        }

        private static void PushChildren(HierarchyFrameDataView view, int parentItemId, Stack<int> stack)
        {
            var children = new List<int>();
            view.GetItemChildren(parentItemId, children);
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }

        private static ErrorResponse ValidateThreadSearch(
            bool validThreadFound,
            bool matchedRequestedThread,
            RecordedOptions options,
            ResolvedFrameRange range,
            ThreadCatalog searchedThreads,
            int scannedFrameCount,
            int scannedThreadCount)
        {
            if (!validThreadFound)
            {
                return new ErrorResponse("No profiler thread data found for the requested frame range.", new
                {
                    available_range = range.AvailableRange,
                    requested_range = range.RequestedRange,
                    searched_threads = searchedThreads.ToResponse(),
                    scanned_frame_count = scannedFrameCount,
                    scanned_thread_count = scannedThreadCount,
                });
            }

            if (!matchedRequestedThread)
            {
                return new ErrorResponse("Requested profiler thread was not found or invalid.", new
                {
                    available_range = range.AvailableRange,
                    requested_range = range.RequestedRange,
                    requested_thread_index = options.ThreadIndex,
                    requested_thread_name = options.ThreadName,
                    searched_threads = searchedThreads.ToResponse(),
                    scanned_frame_count = scannedFrameCount,
                    scanned_thread_count = scannedThreadCount,
                });
            }

            return null;
        }

        private static Dictionary<string, object> BuildFrameTimeStats(List<FrameTimeSample> frameSamples)
        {
            var sorted = new List<FrameTimeSample>(frameSamples);
            sorted.Sort(CompareFrameTimeAscending);
            double total = 0.0;
            for (int i = 0; i < sorted.Count; i++)
                total += sorted[i].TimeMs;

            return new Dictionary<string, object>
            {
                ["min"] = RoundMs(sorted[0].TimeMs),
                ["lower_quartile"] = RoundMs(GetFramePercentile(sorted, 25).TimeMs),
                ["median"] = RoundMs(GetFramePercentile(sorted, 50).TimeMs),
                ["upper_quartile"] = RoundMs(GetFramePercentile(sorted, 75).TimeMs),
                ["p95"] = RoundMs(GetFramePercentile(sorted, 95).TimeMs),
                ["max"] = RoundMs(sorted[sorted.Count - 1].TimeMs),
                ["mean"] = RoundMs(total / sorted.Count),
            };
        }

        private static List<object> BuildWorstFrames(List<FrameTimeSample> frameSamples, int topN)
        {
            var sorted = new List<FrameTimeSample>(frameSamples);
            sorted.Sort(CompareFrameTimeDescending);
            var worstFrames = new List<object>();
            int count = Math.Min(topN, sorted.Count);
            for (int i = 0; i < count; i++)
            {
                worstFrames.Add(new Dictionary<string, object>
                {
                    ["frame_index"] = sorted[i].FrameIndex,
                    ["frame_time_ms"] = RoundMs(sorted[i].TimeMs),
                });
            }
            return worstFrames;
        }

        private static List<MarkerRow> BuildMarkerRows(Dictionary<string, MarkerAggregate> markersByName)
        {
            var rows = new List<MarkerRow>(markersByName.Count);
            foreach (var pair in markersByName)
                rows.Add(pair.Value.ToRow());
            return rows;
        }

        private static void SortMarkerRows(List<MarkerRow> rows, string sortBy)
        {
            rows.Sort((a, b) => CompareMarkerRows(a, b, sortBy));
        }

        private static List<MarkerExportRow> BuildMarkerExportRows(
            Dictionary<string, ExportMarkerAggregate> markersByName,
            int frameSummaryMedianFrame)
        {
            var rows = new List<MarkerExportRow>(markersByName.Count);
            foreach (var pair in markersByName)
                rows.Add(pair.Value.ToRow(frameSummaryMedianFrame));
            return rows;
        }

        private static void SortMarkerExportRows(List<MarkerExportRow> rows)
        {
            rows.Sort(CompareMarkerExportRows);
        }

        private static int CompareMarkerRows(MarkerRow a, MarkerRow b, string sortBy)
        {
            int result;
            switch (sortBy)
            {
                case "self_time":
                    result = b.SelfTimeMs.CompareTo(a.SelfTimeMs);
                    break;
                case "max_total_time":
                    result = b.MaxTotalTimeMs.CompareTo(a.MaxTotalTimeMs);
                    break;
                case "max_self_time":
                    result = b.MaxSelfTimeMs.CompareTo(a.MaxSelfTimeMs);
                    break;
                case "call_count":
                    result = b.CallCount.CompareTo(a.CallCount);
                    break;
                case "frame_count":
                    result = b.FrameCount.CompareTo(a.FrameCount);
                    break;
                default:
                    result = b.TotalTimeMs.CompareTo(a.TotalTimeMs);
                    break;
            }

            return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        private static int CompareMarkerExportRows(MarkerExportRow a, MarkerExportRow b)
        {
            int result = b.MedianTimeMs.CompareTo(a.MedianTimeMs);
            if (result != 0)
                return result;

            result = a.MedianFrameIndex.CompareTo(b.MedianFrameIndex);
            return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        private static int CompareFindMarkerMatches(MarkerFrameThreadMatch a, MarkerFrameThreadMatch b)
        {
            int result = b.TotalTimeMs.CompareTo(a.TotalTimeMs);
            if (result != 0)
                return result;
            result = b.SelfTimeMs.CompareTo(a.SelfTimeMs);
            if (result != 0)
                return result;
            result = a.FrameIndex.CompareTo(b.FrameIndex);
            return result != 0 ? result : a.ThreadIndex.CompareTo(b.ThreadIndex);
        }

        private static int CompareFrameTimeAscending(FrameTimeSample a, FrameTimeSample b)
        {
            int result = a.TimeMs.CompareTo(b.TimeMs);
            return result != 0 ? result : a.FrameIndex.CompareTo(b.FrameIndex);
        }

        private static int CompareFrameTimeDescending(FrameTimeSample a, FrameTimeSample b)
        {
            int result = b.TimeMs.CompareTo(a.TimeMs);
            return result != 0 ? result : a.FrameIndex.CompareTo(b.FrameIndex);
        }

        private static FrameTimeSample GetFramePercentile(List<FrameTimeSample> sortedFrames, int percent)
        {
            int index = (int)((sortedFrames.Count - 1) * percent / 100.0);
            return sortedFrames[index];
        }

        private static int GetMedianFrameIndexForExport(List<FrameTimeExportRow> rows)
        {
            if (rows.Count == 0)
                return 0;

            var sorted = new List<FrameTimeExportRow>(rows);
            sorted.Sort(CompareFrameTimeExportRowsAscending);
            int index = (int)((sorted.Count - 1) * 50 / 100.0);
            return sorted[index].FrameIndex;
        }

        private static int CompareFrameTimeExportRowsAscending(FrameTimeExportRow a, FrameTimeExportRow b)
        {
            int result = a.FrameTimeMs.CompareTo(b.FrameTimeMs);
            return result != 0 ? result : a.FrameIndex.CompareTo(b.FrameIndex);
        }

        private static bool ThreadMatches(string threadName, RecordedOptions options)
        {
            return string.IsNullOrEmpty(options.ThreadName) ||
                   string.Equals(threadName, options.ThreadName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ExportThreadMatches(string threadName, ExportOptions options)
        {
            return string.IsNullOrEmpty(options.ThreadName) ||
                   string.Equals(threadName, options.ThreadName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MarkerMatches(string markerName, RecordedOptions options)
        {
            if (string.IsNullOrEmpty(markerName))
                return false;
            if (string.IsNullOrEmpty(options.MarkerFilter))
                return true;

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

        private static bool IsValidSortBy(string sortBy)
        {
            return sortBy == "total_time" ||
                   sortBy == "self_time" ||
                   sortBy == "max_total_time" ||
                   sortBy == "max_self_time" ||
                   sortBy == "call_count" ||
                   sortBy == "frame_count";
        }

        private static double GetColumnDoubleOrZero(HierarchyFrameDataView view, int itemId, int column)
        {
            float? value = TryGetColumnSingle(view, itemId, column);
            return value.HasValue ? value.Value : 0.0;
        }

        private static int GetColumnIntOrZero(HierarchyFrameDataView view, int itemId, int column)
        {
            float? value = TryGetColumnSingle(view, itemId, column);
            return value.HasValue ? (int)Math.Round(value.Value) : 0;
        }

        private static float? TryGetColumnSingle(HierarchyFrameDataView view, int itemId, int column)
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
            return value > max ? max : value;
        }

        private static double RoundMs(double value)
        {
            return Math.Round(value, 4);
        }

        private static Result<string> ResolveExportOutputDirectory(ExportOptions options)
        {
            string outputDir = options.OutputDir;
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(
                    Path.GetTempPath(),
                    "unity-profiler-export",
                    "profile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            }

            string fullPath;
            if (!TryGetFullPath(outputDir, out fullPath))
                return Result<string>.Error($"Invalid output_dir '{outputDir}'.");

            if (IsInsideAssetsDirectory(fullPath))
                return Result<string>.Error("output_dir must not be inside the Unity Assets directory.");

            if (options.Overwrite)
                return Result<string>.Success(fullPath);

            string candidate = fullPath;
            for (int i = 0; i < MaximumOutputDirectoryAttempts; i++)
            {
                if (!ExportOutputFilesExist(candidate, options))
                    return Result<string>.Success(candidate);

                candidate = fullPath + "-" + (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            return Result<string>.Error("Could not find a unique output directory for profiler CSV export.");
        }

        private static bool ExportOutputFilesExist(string outputDir, ExportOptions options)
        {
            if (options.IncludeFrameTable && File.Exists(Path.Combine(outputDir, "frameTime.csv")))
                return true;

            return options.IncludeMarkerTable && File.Exists(Path.Combine(outputDir, "markerTable.csv"));
        }

        private static bool TryGetFullPath(string path, out string fullPath)
        {
            fullPath = null;
            try
            {
                fullPath = Path.GetFullPath(path);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }

        private static bool IsInsideAssetsDirectory(string path)
        {
            string assetsPath;
            if (!TryGetFullPath(UnityEngine.Application.dataPath, out assetsPath))
                return false;

            string normalizedPath = NormalizeDirectoryPath(path);
            string normalizedAssetsPath = NormalizeDirectoryPath(assetsPath);
            return string.Equals(normalizedPath, normalizedAssetsPath, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedAssetsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void WriteFrameTimeCsv(string path, List<FrameTimeExportRow> rows)
        {
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(string.Join("; ", FrameTimeColumns));
                for (int i = 0; i < rows.Count; i++)
                    writer.WriteLine(rows[i].ToCsvLine());
            }
        }

        private static void WriteMarkerTableCsv(string path, List<MarkerExportRow> rows, int rowCount)
        {
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(string.Join("; ", MarkerTableColumns));
                for (int i = 0; i < rowCount; i++)
                    writer.WriteLine(rows[i].ToCsvLine());
            }
        }

        private static string FormatCsvNumber(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatCsvInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string EscapeMarkerName(string markerName)
        {
            return "\"" + markerName.Replace('\"', '\'') + "\"";
        }

        private static ErrorResponse ProfilerApiUnavailable(Exception ex)
        {
            return new ErrorResponse("Unity profiler frame data API unavailable in this editor version.", new
            {
                exception_type = ex.GetType().Name,
                exception_message = ex.Message,
            });
        }

        private sealed class RecordedOptions
        {
            public int? StartFrame { get; set; }
            public int? EndFrame { get; set; }
            public string ThreadName { get; set; }
            public int? ThreadIndex { get; set; }
            public string MarkerFilter { get; set; }
            public string MatchMode { get; set; }
            public Regex MarkerRegex { get; set; }
            public string SortBy { get; set; }
            public int TopN { get; set; }
            public int MaxFrames { get; set; }
        }

        private sealed class ExportOptions
        {
            public string OutputDir { get; set; }
            public int? StartFrame { get; set; }
            public int? EndFrame { get; set; }
            public string ThreadName { get; set; }
            public int? ThreadIndex { get; set; }
            public bool IncludeFrameTable { get; set; }
            public bool IncludeMarkerTable { get; set; }
            public int MaxFrames { get; set; }
            public int MaxMarkerRows { get; set; }
            public bool Overwrite { get; set; }
        }

        private sealed class ResolvedFrameRange
        {
            public int FirstFrame { get; set; }
            public int LastFrame { get; set; }
            public int StartFrame { get; set; }
            public int EndFrame { get; set; }

            public object AvailableRange => new Dictionary<string, object>
            {
                ["first_frame"] = FirstFrame,
                ["last_frame"] = LastFrame,
            };

            public object RequestedRange => new Dictionary<string, object>
            {
                ["start_frame"] = StartFrame,
                ["end_frame"] = EndFrame,
            };
        }

        private sealed class FrameScanResult
        {
            public bool ValidThreadFound { get; set; }
            public bool MatchedRequestedThread { get; set; }
            public double? FrameTimeMs { get; set; }
            public int ScannedThreadCount { get; set; }
            public int ScannedItemCount { get; set; }
        }

        private sealed class ExportData
        {
            public List<FrameTimeExportRow> FrameRows { get; } = new List<FrameTimeExportRow>();
            public Dictionary<string, ExportMarkerAggregate> MarkersByName { get; } =
                new Dictionary<string, ExportMarkerAggregate>(StringComparer.Ordinal);
            public ThreadCatalog SearchedThreads { get; } = new ThreadCatalog();
            public bool ValidThreadFound { get; set; }
            public bool MatchedRequestedThread { get; set; }
            public bool FrameTruncated { get; set; }
            public int ScannedFrameCount { get; set; }
            public int ScannedThreadCount { get; set; }
            public int ScannedItemCount { get; set; }
            public int FrameSummaryMedianFrame { get; set; }
        }

        private sealed class ThreadCatalog
        {
            private readonly List<ThreadInfo> _threads = new List<ThreadInfo>();

            public void Add(HierarchyFrameDataView view, bool searched)
            {
                Add(view.threadIndex, view.threadName, view.threadGroupName, view.sampleCount, searched);
            }

            public void Add(int threadIndex, string threadName, string threadGroupName, int sampleCount, bool searched)
            {
                ThreadInfo existing = null;
                for (int i = 0; i < _threads.Count; i++)
                {
                    if (_threads[i].ThreadIndex == threadIndex)
                    {
                        existing = _threads[i];
                        break;
                    }
                }

                if (existing == null)
                {
                    _threads.Add(new ThreadInfo
                    {
                        ThreadIndex = threadIndex,
                        ThreadName = threadName,
                        ThreadGroupName = threadGroupName,
                        SampleCount = sampleCount,
                        Searched = searched,
                    });
                    return;
                }

                if (searched)
                    existing.Searched = true;
                if (sampleCount > existing.SampleCount)
                    existing.SampleCount = sampleCount;
            }

            public List<object> ToResponse()
            {
                var response = new List<object>();
                for (int i = 0; i < _threads.Count; i++)
                    response.Add(_threads[i].ToResponse());
                return response;
            }
        }

        private sealed class ThreadInfo
        {
            public int ThreadIndex { get; set; }
            public string ThreadName { get; set; }
            public string ThreadGroupName { get; set; }
            public int SampleCount { get; set; }
            public bool Searched { get; set; }

            public object ToResponse()
            {
                return new Dictionary<string, object>
                {
                    ["thread_index"] = ThreadIndex,
                    ["thread_name"] = ThreadName,
                    ["thread_group_name"] = ThreadGroupName,
                    ["sample_count"] = SampleCount,
                    ["searched"] = Searched,
                };
            }
        }

        private sealed class FrameTimeSample
        {
            public FrameTimeSample(int frameIndex, double timeMs)
            {
                FrameIndex = frameIndex;
                TimeMs = timeMs;
            }

            public int FrameIndex { get; }
            public double TimeMs { get; }
        }

        private sealed class FrameTimeExportRow
        {
            public FrameTimeExportRow(int frameOffset, int frameIndex, double frameTimeMs, double timeFromFirstFrameMs)
            {
                FrameOffset = frameOffset;
                FrameIndex = frameIndex;
                FrameTimeMs = frameTimeMs;
                TimeFromFirstFrameMs = timeFromFirstFrameMs;
            }

            public int FrameOffset { get; }
            public int FrameIndex { get; }
            public double FrameTimeMs { get; }
            public double TimeFromFirstFrameMs { get; }

            public string ToCsvLine()
            {
                return string.Join(";", new[]
                {
                    FormatCsvInt(FrameOffset),
                    FormatCsvInt(FrameIndex),
                    FormatCsvNumber(FrameTimeMs),
                    FormatCsvNumber(TimeFromFirstFrameMs),
                });
            }
        }

        private sealed class ExportMarkerAggregate
        {
            private readonly Dictionary<int, MarkerExportFrame> _frames = new Dictionary<int, MarkerExportFrame>();

            public ExportMarkerAggregate(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public void Add(int frameIndex, double durationMs, int depth)
            {
                MarkerExportFrame frame;
                if (!_frames.TryGetValue(frameIndex, out frame))
                {
                    frame = new MarkerExportFrame(frameIndex);
                    _frames[frameIndex] = frame;
                }

                frame.Add(durationMs, depth);
            }

            public MarkerExportRow ToRow(int frameSummaryMedianFrame)
            {
                var framesByTime = new List<MarkerExportFrame>(_frames.Values);
                framesByTime.Sort(CompareMarkerExportFrameByTimeAscending);

                double totalTime = 0.0;
                int countTotal = 0;
                int firstFrame = int.MaxValue;
                int minDepth = int.MaxValue;
                int maxDepth = 0;
                double minIndividualTime = double.MaxValue;
                double maxIndividualTime = double.MinValue;
                int minIndividualFrame = 0;
                int maxIndividualFrame = 0;

                for (int i = 0; i < framesByTime.Count; i++)
                {
                    var frame = framesByTime[i];
                    totalTime += frame.TotalTimeMs;
                    countTotal += frame.Count;
                    if (frame.FrameIndex < firstFrame)
                        firstFrame = frame.FrameIndex;
                    if (frame.MinDepth < minDepth)
                        minDepth = frame.MinDepth;
                    if (frame.MaxDepth > maxDepth)
                        maxDepth = frame.MaxDepth;
                    if (frame.MinIndividualTimeMs < minIndividualTime)
                    {
                        minIndividualTime = frame.MinIndividualTimeMs;
                        minIndividualFrame = frame.FrameIndex;
                    }
                    if (frame.MaxIndividualTimeMs > maxIndividualTime)
                    {
                        maxIndividualTime = frame.MaxIndividualTimeMs;
                        maxIndividualFrame = frame.FrameIndex;
                    }
                }

                var framesByCount = new List<MarkerExportFrame>(framesByTime);
                framesByCount.Sort(CompareMarkerExportFrameByCountAscending);

                MarkerExportFrame medianFrame = framesByTime.Count > 0
                    ? GetMarkerExportFramePercentile(framesByTime, 50)
                    : null;
                MarkerExportFrame minFrame = framesByTime.Count > 0
                    ? GetMarkerExportFramePercentile(framesByTime, 0)
                    : null;
                MarkerExportFrame maxFrame = framesByTime.Count > 0
                    ? GetMarkerExportFramePercentile(framesByTime, 100)
                    : null;
                MarkerExportFrame lowerQuartileFrame = framesByTime.Count > 0
                    ? GetMarkerExportFramePercentile(framesByTime, 25)
                    : null;
                MarkerExportFrame upperQuartileFrame = framesByTime.Count > 0
                    ? GetMarkerExportFramePercentile(framesByTime, 75)
                    : null;
                MarkerExportFrame countMedianFrame = framesByCount.Count > 0
                    ? GetMarkerExportFramePercentile(framesByCount, 50)
                    : null;
                MarkerExportFrame countMinFrame = framesByCount.Count > 0
                    ? GetMarkerExportFramePercentile(framesByCount, 0)
                    : null;
                MarkerExportFrame countMaxFrame = framesByCount.Count > 0
                    ? GetMarkerExportFramePercentile(framesByCount, 100)
                    : null;

                MarkerExportFrame medianSummaryFrame;
                double timeAtMedianFrame = _frames.TryGetValue(frameSummaryMedianFrame, out medianSummaryFrame)
                    ? medianSummaryFrame.TotalTimeMs
                    : 0.0;

                return new MarkerExportRow
                {
                    Name = Name,
                    MedianTimeMs = medianFrame != null ? medianFrame.TotalTimeMs : 0.0,
                    MinTimeMs = minFrame != null ? minFrame.TotalTimeMs : 0.0,
                    MaxTimeMs = maxFrame != null ? maxFrame.TotalTimeMs : 0.0,
                    MedianFrameIndex = medianFrame != null ? medianFrame.FrameIndex : 0,
                    MinFrameIndex = minFrame != null ? minFrame.FrameIndex : 0,
                    MaxFrameIndex = maxFrame != null ? maxFrame.FrameIndex : 0,
                    MinDepth = minDepth == int.MaxValue ? 0 : minDepth,
                    MaxDepth = maxDepth,
                    TotalTimeMs = totalTime,
                    MeanTimeMs = framesByTime.Count > 0 ? totalTime / framesByTime.Count : 0.0,
                    LowerQuartileTimeMs = lowerQuartileFrame != null ? lowerQuartileFrame.TotalTimeMs : 0.0,
                    UpperQuartileTimeMs = upperQuartileFrame != null ? upperQuartileFrame.TotalTimeMs : 0.0,
                    CountTotal = countTotal,
                    CountMedian = countMedianFrame != null ? countMedianFrame.Count : 0,
                    CountMin = countMinFrame != null ? countMinFrame.Count : 0,
                    CountMax = countMaxFrame != null ? countMaxFrame.Count : 0,
                    PresentOnFrameCount = framesByTime.Count,
                    FirstFrameIndex = firstFrame == int.MaxValue ? 0 : firstFrame,
                    MinIndividualTimeMs = minIndividualTime == double.MaxValue ? 0.0 : minIndividualTime,
                    MaxIndividualTimeMs = maxIndividualTime == double.MinValue ? 0.0 : maxIndividualTime,
                    MinIndividualFrameIndex = minIndividualFrame,
                    MaxIndividualFrameIndex = maxIndividualFrame,
                    TimeAtMedianFrameMs = timeAtMedianFrame,
                };
            }

            private static int CompareMarkerExportFrameByTimeAscending(MarkerExportFrame a, MarkerExportFrame b)
            {
                int result = a.TotalTimeMs.CompareTo(b.TotalTimeMs);
                return result != 0 ? result : a.FrameIndex.CompareTo(b.FrameIndex);
            }

            private static int CompareMarkerExportFrameByCountAscending(MarkerExportFrame a, MarkerExportFrame b)
            {
                int result = a.Count.CompareTo(b.Count);
                return result != 0 ? result : a.FrameIndex.CompareTo(b.FrameIndex);
            }

            private static MarkerExportFrame GetMarkerExportFramePercentile(List<MarkerExportFrame> frames, int percent)
            {
                int index = (int)((frames.Count - 1) * percent / 100.0);
                return frames[index];
            }
        }

        private sealed class MarkerExportFrame
        {
            public MarkerExportFrame(int frameIndex)
            {
                FrameIndex = frameIndex;
                MinDepth = int.MaxValue;
                MinIndividualTimeMs = double.MaxValue;
                MaxIndividualTimeMs = double.MinValue;
            }

            public int FrameIndex { get; }
            public double TotalTimeMs { get; private set; }
            public int Count { get; private set; }
            public int MinDepth { get; private set; }
            public int MaxDepth { get; private set; }
            public double MinIndividualTimeMs { get; private set; }
            public double MaxIndividualTimeMs { get; private set; }

            public void Add(double durationMs, int depth)
            {
                TotalTimeMs += durationMs;
                Count++;
                if (depth < MinDepth)
                    MinDepth = depth;
                if (depth > MaxDepth)
                    MaxDepth = depth;
                if (durationMs < MinIndividualTimeMs)
                    MinIndividualTimeMs = durationMs;
                if (durationMs > MaxIndividualTimeMs)
                    MaxIndividualTimeMs = durationMs;
            }
        }

        private sealed class MarkerExportRow
        {
            public string Name { get; set; }
            public double MedianTimeMs { get; set; }
            public double MinTimeMs { get; set; }
            public double MaxTimeMs { get; set; }
            public int MedianFrameIndex { get; set; }
            public int MinFrameIndex { get; set; }
            public int MaxFrameIndex { get; set; }
            public int MinDepth { get; set; }
            public int MaxDepth { get; set; }
            public double TotalTimeMs { get; set; }
            public double MeanTimeMs { get; set; }
            public double LowerQuartileTimeMs { get; set; }
            public double UpperQuartileTimeMs { get; set; }
            public int CountTotal { get; set; }
            public int CountMedian { get; set; }
            public int CountMin { get; set; }
            public int CountMax { get; set; }
            public int PresentOnFrameCount { get; set; }
            public int FirstFrameIndex { get; set; }
            public double MinIndividualTimeMs { get; set; }
            public double MaxIndividualTimeMs { get; set; }
            public int MinIndividualFrameIndex { get; set; }
            public int MaxIndividualFrameIndex { get; set; }
            public double TimeAtMedianFrameMs { get; set; }

            public string ToCsvLine()
            {
                return string.Join(";", new[]
                {
                    EscapeMarkerName(Name),
                    FormatCsvNumber(MedianTimeMs),
                    FormatCsvNumber(MinTimeMs),
                    FormatCsvNumber(MaxTimeMs),
                    FormatCsvInt(MedianFrameIndex),
                    FormatCsvInt(MinFrameIndex),
                    FormatCsvInt(MaxFrameIndex),
                    FormatCsvInt(MinDepth),
                    FormatCsvInt(MaxDepth),
                    FormatCsvNumber(TotalTimeMs),
                    FormatCsvNumber(MeanTimeMs),
                    FormatCsvNumber(LowerQuartileTimeMs),
                    FormatCsvNumber(UpperQuartileTimeMs),
                    FormatCsvInt(CountTotal),
                    FormatCsvInt(CountMedian),
                    FormatCsvInt(CountMin),
                    FormatCsvInt(CountMax),
                    FormatCsvInt(PresentOnFrameCount),
                    FormatCsvInt(FirstFrameIndex),
                    FormatCsvNumber(MinIndividualTimeMs),
                    FormatCsvNumber(MaxIndividualTimeMs),
                    FormatCsvInt(MinIndividualFrameIndex),
                    FormatCsvInt(MaxIndividualFrameIndex),
                    FormatCsvNumber(TimeAtMedianFrameMs),
                });
            }
        }

        private sealed class MarkerAggregate
        {
            private readonly Dictionary<int, MarkerFrameAggregate> _frames = new Dictionary<int, MarkerFrameAggregate>();

            public MarkerAggregate(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public void Add(int frameIndex, double totalTimeMs, double selfTimeMs, int calls)
            {
                MarkerFrameAggregate frame;
                if (!_frames.TryGetValue(frameIndex, out frame))
                {
                    frame = new MarkerFrameAggregate(frameIndex);
                    _frames[frameIndex] = frame;
                }

                frame.TotalTimeMs += totalTimeMs;
                frame.SelfTimeMs += selfTimeMs;
                frame.Calls += calls;
            }

            public MarkerRow ToRow()
            {
                var frames = new List<MarkerFrameAggregate>(_frames.Values);
                frames.Sort(CompareMarkerFrameByTotalTimeAscending);

                double totalTime = 0.0;
                double selfTime = 0.0;
                int callCount = 0;
                int firstFrame = int.MaxValue;
                double maxTotalTime = 0.0;
                double maxSelfTime = 0.0;
                int maxTotalFrame = 0;
                int maxSelfFrame = 0;

                for (int i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    totalTime += frame.TotalTimeMs;
                    selfTime += frame.SelfTimeMs;
                    callCount += frame.Calls;
                    if (frame.FrameIndex < firstFrame)
                        firstFrame = frame.FrameIndex;
                    if (frame.TotalTimeMs > maxTotalTime)
                    {
                        maxTotalTime = frame.TotalTimeMs;
                        maxTotalFrame = frame.FrameIndex;
                    }
                    if (frame.SelfTimeMs > maxSelfTime)
                    {
                        maxSelfTime = frame.SelfTimeMs;
                        maxSelfFrame = frame.FrameIndex;
                    }
                }

                return new MarkerRow
                {
                    Name = Name,
                    TotalTimeMs = totalTime,
                    SelfTimeMs = selfTime,
                    MeanTimeMs = frames.Count > 0 ? totalTime / frames.Count : 0.0,
                    MedianTimeMs = frames.Count > 0 ? GetMarkerFramePercentile(frames, 50).TotalTimeMs : 0.0,
                    LowerQuartileTimeMs = frames.Count > 0 ? GetMarkerFramePercentile(frames, 25).TotalTimeMs : 0.0,
                    UpperQuartileTimeMs = frames.Count > 0 ? GetMarkerFramePercentile(frames, 75).TotalTimeMs : 0.0,
                    MaxTotalTimeMs = maxTotalTime,
                    MaxSelfTimeMs = maxSelfTime,
                    FrameCount = frames.Count,
                    CallCount = callCount,
                    FirstFrame = firstFrame == int.MaxValue ? 0 : firstFrame,
                    MaxTotalFrame = maxTotalFrame,
                    MaxSelfFrame = maxSelfFrame,
                    SampleFrames = BuildSampleFrames(frames, maxTotalFrame),
                };
            }

            private static int CompareMarkerFrameByTotalTimeAscending(MarkerFrameAggregate a, MarkerFrameAggregate b)
            {
                int result = a.TotalTimeMs.CompareTo(b.TotalTimeMs);
                return result != 0 ? result : a.FrameIndex.CompareTo(b.FrameIndex);
            }

            private static MarkerFrameAggregate GetMarkerFramePercentile(List<MarkerFrameAggregate> sortedFrames, int percent)
            {
                int index = (int)((sortedFrames.Count - 1) * percent / 100.0);
                return sortedFrames[index];
            }

            private static List<int> BuildSampleFrames(List<MarkerFrameAggregate> frames, int maxTotalFrame)
            {
                var byFrameIndex = new List<MarkerFrameAggregate>(frames);
                byFrameIndex.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
                var sampleFrames = new List<int>();
                int firstCount = Math.Min(3, byFrameIndex.Count);
                for (int i = 0; i < firstCount; i++)
                    sampleFrames.Add(byFrameIndex[i].FrameIndex);

                if (maxTotalFrame != 0 && !sampleFrames.Contains(maxTotalFrame))
                    sampleFrames.Add(maxTotalFrame);

                sampleFrames.Sort();
                return sampleFrames;
            }
        }

        private sealed class MarkerFrameAggregate
        {
            public MarkerFrameAggregate(int frameIndex)
            {
                FrameIndex = frameIndex;
            }

            public int FrameIndex { get; }
            public double TotalTimeMs { get; set; }
            public double SelfTimeMs { get; set; }
            public int Calls { get; set; }
        }

        private sealed class MarkerRow
        {
            public string Name { get; set; }
            public double TotalTimeMs { get; set; }
            public double SelfTimeMs { get; set; }
            public double MeanTimeMs { get; set; }
            public double MedianTimeMs { get; set; }
            public double LowerQuartileTimeMs { get; set; }
            public double UpperQuartileTimeMs { get; set; }
            public double MaxTotalTimeMs { get; set; }
            public double MaxSelfTimeMs { get; set; }
            public int FrameCount { get; set; }
            public int CallCount { get; set; }
            public int FirstFrame { get; set; }
            public int MaxTotalFrame { get; set; }
            public int MaxSelfFrame { get; set; }
            public List<int> SampleFrames { get; set; }

            public object ToResponse()
            {
                return new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["total_time_ms"] = RoundMs(TotalTimeMs),
                    ["self_time_ms"] = RoundMs(SelfTimeMs),
                    ["mean_time_ms"] = RoundMs(MeanTimeMs),
                    ["median_time_ms"] = RoundMs(MedianTimeMs),
                    ["lower_quartile_time_ms"] = RoundMs(LowerQuartileTimeMs),
                    ["upper_quartile_time_ms"] = RoundMs(UpperQuartileTimeMs),
                    ["max_total_time_ms"] = RoundMs(MaxTotalTimeMs),
                    ["max_self_time_ms"] = RoundMs(MaxSelfTimeMs),
                    ["frame_count"] = FrameCount,
                    ["call_count"] = CallCount,
                    ["first_frame"] = FirstFrame,
                    ["max_total_frame"] = MaxTotalFrame,
                    ["max_self_frame"] = MaxSelfFrame,
                    ["sample_frames"] = SampleFrames,
                };
            }
        }

        private sealed class MarkerFrameThreadMatch
        {
            private readonly List<string> _markerNames = new List<string>();

            public MarkerFrameThreadMatch(int frameIndex, int threadIndex, string threadName)
            {
                FrameIndex = frameIndex;
                ThreadIndex = threadIndex;
                ThreadName = threadName;
            }

            public int FrameIndex { get; }
            public int ThreadIndex { get; }
            public string ThreadName { get; }
            public double TotalTimeMs { get; private set; }
            public double SelfTimeMs { get; private set; }
            public int Calls { get; private set; }
            public int MatchedItemCount { get; private set; }

            public void Add(string markerName, double totalTimeMs, double selfTimeMs, int calls)
            {
                TotalTimeMs += totalTimeMs;
                SelfTimeMs += selfTimeMs;
                Calls += calls;
                MatchedItemCount++;
                if (!_markerNames.Contains(markerName))
                    _markerNames.Add(markerName);
            }

            public object ToResponse()
            {
                _markerNames.Sort(StringComparer.Ordinal);
                return new Dictionary<string, object>
                {
                    ["frame_index"] = FrameIndex,
                    ["thread_index"] = ThreadIndex,
                    ["thread_name"] = ThreadName,
                    ["marker_names"] = _markerNames,
                    ["total_time_ms"] = RoundMs(TotalTimeMs),
                    ["self_time_ms"] = RoundMs(SelfTimeMs),
                    ["calls"] = Calls,
                };
            }
        }
    }
}
