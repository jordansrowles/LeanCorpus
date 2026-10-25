using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Rowles.LeanCorpus.Tests.Shared.Diagnostics;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Diagnostics.Unit;

[Category(TestCategory.Unit)]
[Area(TestArea.Diagnostics)]
public sealed class TelemetryExecutionStoreTests
{
    [Fact]
    public void SummaryModeDoesNotPersistCleanPassingTest()
    {
        string directory = NewDirectory();
        try
        {
            using (var store = new TelemetryExecutionStore(directory, "summary", TelemetryStreamLimits.Default))
            {
                TelemetryTestSession session = store.StartTest("clean-test", "CleanTest");
                Assert.Null(store.FinishTest(session, TestResult.Passed, []));
            }

            string telemetryDirectory = Path.Combine(directory, "telemetry");
            Assert.Equal(["summary.json"], Directory.GetFiles(telemetryDirectory).Select(Path.GetFileName).ToArray());
            Assert.True(File.Exists(Path.Combine(telemetryDirectory, "summary.json")));
            Assert.False(File.Exists(Path.Combine(telemetryDirectory, "tests.ndjson")));
            Assert.Empty(Directory.EnumerateDirectories(telemetryDirectory, "*", SearchOption.AllDirectories));

            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(telemetryDirectory, "summary.json")));
            Assert.Equal(1, summary.RootElement.GetProperty("testsStarted").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("testsFinished").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("testsPassed").GetInt64());
            Assert.Equal(0, summary.RootElement.GetProperty("retainedTestSummaries").GetInt64());
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public void SummaryModeRetainsFailedWarningAndTelemetryAnomalyTests()
    {
        string directory = NewDirectory();
        try
        {
            string? failedAttachment;
            using (var store = new TelemetryExecutionStore(directory, "summary", TelemetryStreamLimits.Default))
            {
                failedAttachment = store.FinishTest(store.StartTest("failed", "Failed"), TestResult.Failed, []);
                Assert.NotNull(failedAttachment);
                Assert.NotNull(store.FinishTest(store.StartTest("warning", "Warning"), TestResult.Passed, ["warning text"]));

                TelemetryTestSession error = store.StartTest("telemetry-error", "TelemetryError");
                error.RecordTelemetryError(new InvalidOperationException("sample telemetry error"));
                Assert.NotNull(store.FinishTest(error, TestResult.Passed, []));

                TelemetryTestSession swallowed = store.StartTest("swallowed", "Swallowed");
                using (Activity activity = new("operation"))
                {
                    activity.Start();
                    activity.AddEvent(new ActivityEvent("exception.swallowed"));
                    activity.Stop();
                    swallowed.RecordActivity(activity);
                }
                Assert.NotNull(store.FinishTest(swallowed, TestResult.Passed, []));

                TelemetryTestSession orphaned = store.StartTest("orphaned", "Orphaned");
                orphaned.MarkOrphaned();
                Assert.NotNull(store.FinishTest(orphaned, TestResult.Passed, []));
            }

            string telemetryDirectory = Path.Combine(directory, "telemetry");
            string[] lines = File.ReadAllLines(Path.Combine(telemetryDirectory, "tests.ndjson"));
            Assert.Equal(5, lines.Length);
            Assert.Contains(failedAttachment, lines);
            Assert.Contains(lines, line => JsonDocument.Parse(line).RootElement.GetProperty("testId").GetString() == "failed");
            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(telemetryDirectory, "summary.json")));
            Assert.Equal(5, summary.RootElement.GetProperty("retainedTestSummaries").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("testsFailed").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("swallowedExceptions").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("orphanedActivities").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("telemetryErrors").GetInt64());
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public void FullModeWritesSharedStreamsAndDoesNotAttachCleanPassingTests()
    {
        string directory = NewDirectory();
        try
        {
            using (var store = new TelemetryExecutionStore(directory, "full", TelemetryStreamLimits.Default))
            using (var meter = new Meter("telemetry-test"))
            {
                Counter<long> counter = meter.CreateCounter<long>("test.counter");
                TelemetryTestSession session = store.StartTest("full-test", "FullTest");
                using Activity activity = new("operation");
                activity.Start();
                activity.Stop();
                session.RecordActivity(activity);
                session.RecordMeasurement(counter, 3L, ReadOnlySpan<KeyValuePair<string, object?>>.Empty);
                store.RecordRuntimeSnapshot(new { value = 42 });
                Assert.Null(store.FinishTest(session, TestResult.Passed, []));
            }

            string telemetryDirectory = Path.Combine(directory, "telemetry");
            string[] names = Directory.GetFiles(telemetryDirectory).Select(Path.GetFileName).Order().ToArray()!;
            Assert.Equal(["activities.ndjson", "metrics.ndjson", "summary.json", "tests.ndjson"], names);
            Assert.True(File.Exists(Path.Combine(directory, "runtime", "counters.ndjson")));
            Assert.Single(File.ReadAllLines(Path.Combine(telemetryDirectory, "activities.ndjson")));
            Assert.Single(File.ReadAllLines(Path.Combine(telemetryDirectory, "metrics.ndjson")));
            Assert.Single(File.ReadAllLines(Path.Combine(telemetryDirectory, "tests.ndjson")));
            Assert.Contains("full-test", File.ReadAllText(Path.Combine(telemetryDirectory, "activities.ndjson")));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public void BoundedStreamCountsDroppedRecordsAndNeverExceedsItsLimit()
    {
        string directory = NewDirectory();
        try
        {
            string path = Path.Combine(directory, "bounded.ndjson");
            using (var stream = new BoundedNdjsonStream(path, maximumBytes: 8, eager: false))
            {
                Assert.Null(stream.TryAppend("{\"x\":1}"));
                Assert.Null(stream.TryAppend("{\"x\":1}"));
                Assert.Null(stream.TryAppend("{\"x\":1}"));
                TelemetryStreamStatistics statistics = stream.Snapshot();
                Assert.Equal(1, statistics.RecordsWritten);
                Assert.Equal(2, statistics.RecordsDropped);
                Assert.Equal(8, statistics.BytesWritten);
                Assert.True(statistics.Truncated);
            }
            Assert.Equal(8, new FileInfo(path).Length);
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public void FullSummaryReportsStreamTruncationWithoutFailingRecording()
    {
        string directory = NewDirectory();
        try
        {
            var limits = new TelemetryStreamLimits(ActivitiesBytes: 0, MetricsBytes: 0, RuntimeBytes: 0, TestsBytes: 4096);
            using (var store = new TelemetryExecutionStore(directory, "full", limits))
            using (var meter = new Meter("telemetry-test"))
            {
                Counter<long> counter = meter.CreateCounter<long>("test.counter");
                TelemetryTestSession session = store.StartTest("bounded-test", "BoundedTest");
                using Activity activity = new("operation");
                activity.Start();
                activity.Stop();
                session.RecordActivity(activity);
                session.RecordMeasurement(counter, 1L, ReadOnlySpan<KeyValuePair<string, object?>>.Empty);
                store.RecordRuntimeSnapshot(new { value = 1 });
                Assert.Null(store.FinishTest(session, TestResult.Passed, []));
            }

            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "telemetry", "summary.json")));
            JsonElement streams = summary.RootElement.GetProperty("streams");
            foreach (string name in new[] { "activities", "metrics", "runtimeCounters" })
            {
                JsonElement stream = streams.GetProperty(name);
                Assert.True(stream.GetProperty("truncated").GetBoolean());
                Assert.True(stream.GetProperty("recordsDropped").GetInt64() > 0);
                Assert.Equal(0, stream.GetProperty("bytesWritten").GetInt64());
            }
            Assert.Equal(1, summary.RootElement.GetProperty("activities").GetInt64());
            Assert.Equal(1, summary.RootElement.GetProperty("metrics").GetInt64());
            Assert.Equal(128L * 1024 * 1024, TelemetryStreamLimits.Default.MaximumTotalBytes);
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public void ConcurrentTestWritersKeepSharedNdjsonValidAndCountersAccurate()
    {
        const int testCount = 64;
        string directory = NewDirectory();
        try
        {
            using (var store = new TelemetryExecutionStore(directory, "full", TelemetryStreamLimits.Default))
            using (var meter = new Meter("telemetry-test"))
            {
                Counter<long> counter = meter.CreateCounter<long>("test.counter");
                Parallel.For(0, testCount, index =>
                {
                    TelemetryTestSession session = store.StartTest($"test-{index}", $"Test{index}");
                    using Activity activity = new("operation");
                    activity.Start();
                    activity.Stop();
                    session.RecordActivity(activity);
                    session.RecordMeasurement(counter, 1L, ReadOnlySpan<KeyValuePair<string, object?>>.Empty);
                    Assert.Null(store.FinishTest(session, TestResult.Passed, []));
                });
            }

            string telemetryDirectory = Path.Combine(directory, "telemetry");
            foreach (string file in new[] { "activities.ndjson", "metrics.ndjson", "tests.ndjson" })
            {
                string[] lines = File.ReadAllLines(Path.Combine(telemetryDirectory, file));
                Assert.Equal(testCount, lines.Length);
                foreach (string line in lines)
                {
                    using JsonDocument _ = JsonDocument.Parse(line);
                }
            }

            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(telemetryDirectory, "summary.json")));
            Assert.Equal(testCount, summary.RootElement.GetProperty("testsStarted").GetInt64());
            Assert.Equal(testCount, summary.RootElement.GetProperty("testsFinished").GetInt64());
            Assert.Equal(testCount, summary.RootElement.GetProperty("activities").GetInt64());
            Assert.Equal(testCount, summary.RootElement.GetProperty("metrics").GetInt64());
            Assert.Equal(testCount, summary.RootElement.GetProperty("retainedTestSummaries").GetInt64());
        }
        finally { DeleteDirectory(directory); }
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
