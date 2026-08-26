using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Tools.Profiler;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Characterizes the Editor-update profiler analysis job scheduler.
    /// The original pump gave every active job the same global deadline, so the
    /// first job could consume the whole budget and starve later jobs. The pump
    /// now slices the budget per job: every active job must make bounded
    /// progress on every pump.
    /// </summary>
    public class ProfilerJobPumpTests
    {
        /// <summary>
        /// Deterministic non-terminating job: each step busy-waits ~2ms, so a
        /// single job can easily exhaust the whole pump budget by itself.
        /// </summary>
        private sealed class SlowStepJob : ProfilerAnalysisJobScheduler.ProfilerAnalysisJob
        {
            private const double StepDurationSeconds = 0.002;

            public int Steps { get; private set; }

            public SlowStepJob()
                : base(
                    "test_slow_step",
                    new RecordedFrameAnalysisOps.ResolvedFrameRange
                    {
                        FirstFrame = 0,
                        LastFrame = 999,
                        StartFrame = 0,
                        EndFrame = 999,
                    },
                    1000)
            {
            }

            protected override void StepOnce()
            {
                Steps++;
                double start = EditorApplication.timeSinceStartup;
                while (EditorApplication.timeSinceStartup - start < StepDurationSeconds)
                {
                    // Busy-wait: the pump budget check relies on wall-clock time.
                }
            }
        }

        private static void CancelAndDrain(params SlowStepJob[] jobs)
        {
            foreach (var job in jobs)
            {
                RecordedFrameAnalysisOps.CancelProfilerJob(new JObject { ["job_id"] = job.JobId });
            }

            // No active jobs left: this pump unregisters the EditorApplication.update hook.
            ProfilerAnalysisJobScheduler.ProcessJobs();
        }

        [Test]
        public void ProcessProfilerJobs_MultipleActiveJobs_AllMakeProgressEachPump()
        {
            var jobs = new[] { new SlowStepJob(), new SlowStepJob(), new SlowStepJob() };

            try
            {
                foreach (var job in jobs)
                {
                    var pending = ToJObject(ProfilerAnalysisJobScheduler.Start(job));
                    Assert.AreEqual("pending", pending.Value<string>("_mcp_status"), pending.ToString());
                    Assert.AreEqual(job.JobId, pending["data"]?["job_id"]?.ToString());
                }

                ProfilerAnalysisJobScheduler.ProcessJobs();

                foreach (var job in jobs)
                {
                    Assert.Greater(job.Steps, 0,
                        $"Job {job.JobId} made no progress in a pump with {jobs.Length} active jobs (scheduler starvation).");
                }
            }
            finally
            {
                CancelAndDrain(jobs);
            }
        }

        [Test]
        public void ProcessProfilerJobs_StatusPolling_KeepsJobIdAndPendingShape()
        {
            var job = new SlowStepJob();

            try
            {
                ProfilerAnalysisJobScheduler.Start(job);
                ProfilerAnalysisJobScheduler.ProcessJobs();

                var status = ToJObject(RecordedFrameAnalysisOps.GetProfilerJobStatus(new JObject
                {
                    ["job_id"] = job.JobId,
                }));

                Assert.AreEqual("pending", status.Value<string>("_mcp_status"), status.ToString());
                Assert.AreEqual(job.JobId, status["data"]?["job_id"]?.ToString());
                Assert.AreEqual("running", status["data"]?["state"]?.ToString());
                Assert.IsNotNull(status["data"]?["progress"]?["scanned_frames"],
                    "Pending status must keep the structured progress payload.");
            }
            finally
            {
                CancelAndDrain(job);
            }
        }

        [Test]
        public void ResolveOutputDirectory_ExistingExportFile_UsesUniqueSibling()
        {
            string outputDir = Path.Combine(Path.GetTempPath(), "unity-mcp-profiler-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDir);

            try
            {
                File.WriteAllText(Path.Combine(outputDir, "frameTime.csv"), "existing");

                var result = ProfilerTableExportWriter.ResolveOutputDirectory(
                    outputDir,
                    false,
                    true,
                    false);

                Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
                Assert.AreEqual(outputDir + "-1", result.Value);
            }
            finally
            {
                Directory.Delete(outputDir, true);
            }
        }

        [Test]
        public void ResolveOutputDirectory_AssetsPath_IsRejected()
        {
            var result = ProfilerTableExportWriter.ResolveOutputDirectory(
                UnityEngine.Application.dataPath,
                true,
                true,
                true);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("must not be inside", result.ErrorMessage);
        }
    }
}
