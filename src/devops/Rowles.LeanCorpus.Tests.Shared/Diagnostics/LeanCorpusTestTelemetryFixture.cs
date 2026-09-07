using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Xunit;
using Xunit.v3;

[assembly: AssemblyFixture<Rowles.LeanCorpus.Tests.Shared.Diagnostics.LeanCorpusTestTelemetryFixture>]
[assembly: CaptureConsole]
[assembly: CaptureTrace]

namespace Rowles.LeanCorpus.Tests.Shared.Diagnostics;

public sealed class LeanCorpusTestTelemetryFixture : INotifyTestLifecycle, IDisposable
{
    private const string ApplicationSourceName = "Rowles.LeanCorpus";
    private const string TestSourceName = "Rowles.LeanCorpus.Tests";
    private const string RuntimeMeterName = "System.Runtime";
    private readonly ActivitySource testSource = new(TestSourceName);
    private readonly ConcurrentDictionary<string, TestTelemetrySession> sessionsByTestId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ActivityTraceId, TestTelemetrySession> sessionsByTraceId = new();
    private readonly ActivityListener? activityListener;
    private readonly MeterListener? meterListener;
    private readonly Lock runtimeSync = new();
    private readonly StreamWriter? runtimeWriter;
    private readonly Timer? runtimeTimer;
    private readonly string telemetryMode;
    private bool disposed;

    public LeanCorpusTestTelemetryFixture()
    {
        telemetryMode = Environment.GetEnvironmentVariable("LEANCORPUS_TELEMETRY")?.ToLowerInvariant() ?? "off";
        if (telemetryMode == "off")
            return;

        if (telemetryMode == "full")
        {
            string? executionDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
            if (!string.IsNullOrWhiteSpace(executionDirectory))
            {
                string runtimeDirectory = Path.Combine(executionDirectory, "runtime");
                Directory.CreateDirectory(runtimeDirectory);
                runtimeWriter = CreateWriter(Path.Combine(runtimeDirectory, "counters.ndjson"));
            }
        }

        activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is ApplicationSourceName or TestSourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = RecordActivity,
        };
        ActivitySource.AddActivityListener(activityListener);

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationSourceName ||
                    telemetryMode == "full" && instrument.Meter.Name == RuntimeMeterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        meterListener.SetMeasurementEventCallback<byte>(RecordMeasurement);
        meterListener.SetMeasurementEventCallback<short>(RecordMeasurement);
        meterListener.SetMeasurementEventCallback<int>(RecordMeasurement);
        meterListener.SetMeasurementEventCallback<long>(RecordMeasurement);
        meterListener.SetMeasurementEventCallback<float>(RecordMeasurement);
        meterListener.SetMeasurementEventCallback<double>(RecordMeasurement);
        meterListener.SetMeasurementEventCallback<decimal>(RecordMeasurement);
        meterListener.Start();
        if (runtimeWriter is not null)
        {
            WriteRuntimeSnapshot();
            runtimeTimer = new Timer(_ => WriteRuntimeSnapshot(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    public void OnTestStarting(IXunitTest test)
    {
        if (telemetryMode == "off")
            return;

        string? executionDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(executionDirectory))
            return;

        string testId = test.UniqueID;
        string testDirectory = Path.Combine(executionDirectory, "telemetry", "tests", SanitisePath(testId));
        var session = new TestTelemetrySession(testId, test.TestDisplayName, testDirectory, telemetryMode == "full");
        if (!sessionsByTestId.TryAdd(testId, session))
        {
            session.Dispose();
            TestContext.Current.AddWarning($"Duplicate telemetry session for test '{testId}'.");
            return;
        }

        Activity? root = testSource.StartActivity("leancorpus.test", ActivityKind.Internal);
        if (root is null)
        {
            sessionsByTestId.TryRemove(testId, out _);
            session.Dispose();
            TestContext.Current.AddWarning("The LeanCorpus test telemetry root activity could not be created.");
            return;
        }

        root.SetTag("test.id", testId);
        root.SetTag("test.name", test.TestDisplayName);
        root.SetTag("test.label", test.TestLabel);
        root.SetTag("test.run_id", Environment.GetEnvironmentVariable("LEANCORPUS_RUN_ID"));
        root.SetTag("test.target", Environment.GetEnvironmentVariable("LEANCORPUS_TARGET"));
        root.SetTag("test.iteration", Environment.GetEnvironmentVariable("LEANCORPUS_ITERATION"));
        root.SetTag("dotnet.framework", AppContext.TargetFrameworkName);
        root.SetTag("os.type", Environment.OSVersion.Platform.ToString());
        root.SetTag("os.arch", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        root.SetTag("process.id", Environment.ProcessId);
        root.SetTag("ci", Environment.GetEnvironmentVariable("LEANCORPUS_CI"));
        session.Root = root;
        sessionsByTraceId[root.TraceId] = session;
    }

    public void OnTestFinished(IXunitTest test)
    {
        if (!sessionsByTestId.TryRemove(test.UniqueID, out TestTelemetrySession? session))
            return;

        session.Root?.Stop();
        if (session.Root is not null)
            sessionsByTraceId.TryRemove(session.Root.TraceId, out _);
        session.Complete();

        if (session.SwallowedExceptions > 0)
            TestContext.Current.AddWarning($"LeanCorpus recorded {session.SwallowedExceptions} swallowed exception event(s).");

        TestContext.Current.AddAttachment("leancorpus-telemetry-summary.json", File.ReadAllBytes(session.SummaryPath), "application/json");
        if (session.FullTelemetry)
        {
            TestContext.Current.AddAttachment("leancorpus-activities.ndjson", File.ReadAllBytes(session.ActivitiesPath), "application/x-ndjson");
            TestContext.Current.AddAttachment("leancorpus-metrics.ndjson", File.ReadAllBytes(session.MetricsPath), "application/x-ndjson");
        }
        session.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        runtimeTimer?.Dispose();
        meterListener?.Dispose();
        activityListener?.Dispose();
        testSource.Dispose();
        runtimeWriter?.Dispose();
        foreach (TestTelemetrySession session in sessionsByTestId.Values)
        {
            session.MarkOrphaned();
            session.Dispose();
        }
        sessionsByTestId.Clear();
        sessionsByTraceId.Clear();
    }

    private void RecordActivity(Activity activity)
    {
        if (sessionsByTraceId.TryGetValue(activity.TraceId, out TestTelemetrySession? session))
            session.RecordActivity(activity);
    }

    private void RecordMeasurement<T>(Instrument instrument, T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct
    {
        if (instrument.Meter.Name == RuntimeMeterName && runtimeWriter is not null)
        {
            lock (runtimeSync)
            {
                WriteLine(runtimeWriter, new
                {
                    instrument = instrument.Name,
                    unit = instrument.Unit,
                    timestampUtc = DateTimeOffset.UtcNow,
                    value = Convert.ToString(measurement, CultureInfo.InvariantCulture),
                    testTraceId = Activity.Current?.TraceId.ToHexString(),
                    tags = tags.ToArray().ToDictionary(item => item.Key, item => item.Value),
                });
            }
            return;
        }

        Activity? current = Activity.Current;
        if (current is not null && sessionsByTraceId.TryGetValue(current.TraceId, out TestTelemetrySession? session))
            session.RecordMeasurement(instrument, measurement, tags);
    }

    private static string SanitisePath(string value)
    {
        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (length == 96)
                break;
            buffer[length++] = char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_';
        }
        return new string(buffer[..length]);
    }

    private static StreamWriter CreateWriter(string path) =>
        new(path, append: false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };

    private static void WriteLine(StreamWriter writer, object value) =>
        writer.WriteLine(JsonSerializer.Serialize(value, TestTelemetrySession.JsonOptions));

    private void WriteRuntimeSnapshot()
    {
        if (runtimeWriter is null || disposed)
            return;

        try
        {
            meterListener?.RecordObservableInstruments();
            using Process process = Process.GetCurrentProcess();
            lock (runtimeSync)
            {
                WriteLine(runtimeWriter, new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    processId = Environment.ProcessId,
                    workingSetBytes = process.WorkingSet64,
                    cpuTimeMs = process.TotalProcessorTime.TotalMilliseconds,
                    threadCount = process.Threads.Count,
                    threadPoolThreadCount = ThreadPool.ThreadCount,
                    threadPoolPendingWorkItems = ThreadPool.PendingWorkItemCount,
                    gcTotalMemoryBytes = GC.GetTotalMemory(false),
                    gen0Collections = GC.CollectionCount(0),
                    gen1Collections = GC.CollectionCount(1),
                    gen2Collections = GC.CollectionCount(2),
                });
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced with the periodic sample.
        }
    }

    private sealed class TestTelemetrySession : IDisposable
    {
        internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private readonly Lock sync = new();
        private readonly Dictionary<string, OperationSummary> operations = new(StringComparer.Ordinal);
        private readonly StreamWriter? activityWriter;
        private readonly StreamWriter? metricWriter;
        private bool completed;

        public TestTelemetrySession(string testId, string testName, string directory, bool fullTelemetry)
        {
            TestId = testId;
            TestName = testName;
            Directory.CreateDirectory(directory);
            FullTelemetry = fullTelemetry;
            ActivitiesPath = Path.Combine(directory, "activities.ndjson");
            MetricsPath = Path.Combine(directory, "metrics.ndjson");
            SummaryPath = Path.Combine(directory, "summary.json");
            if (fullTelemetry)
            {
                activityWriter = CreateWriter(ActivitiesPath);
                metricWriter = CreateWriter(MetricsPath);
            }
            else
            {
                File.WriteAllText(ActivitiesPath, string.Empty);
                File.WriteAllText(MetricsPath, string.Empty);
            }
        }

        public string TestId { get; }
        public string TestName { get; }
        public bool FullTelemetry { get; }
        public string ActivitiesPath { get; }
        public string MetricsPath { get; }
        public string SummaryPath { get; }
        public Activity? Root { get; set; }
        public int ActivityCount { get; private set; }
        public int MetricCount { get; private set; }
        public int SwallowedExceptions { get; private set; }
        public int OrphanedActivities { get; private set; }

        public void RecordActivity(Activity activity)
        {
            lock (sync)
            {
                ActivityCount++;
                if (!operations.TryGetValue(activity.OperationName, out OperationSummary? operation))
                    operations[activity.OperationName] = operation = new OperationSummary();
                operation.Count++;
                operation.DurationMs += activity.Duration.TotalMilliseconds;
                int swallowed = activity.Events.Count(item => item.Name == "exception.swallowed");
                SwallowedExceptions += swallowed;
                if (activityWriter is not null)
                {
                    WriteLine(activityWriter, new
                    {
                        testId = TestId,
                        traceId = activity.TraceId.ToHexString(),
                        spanId = activity.SpanId.ToHexString(),
                        parentSpanId = activity.ParentSpanId.ToHexString(),
                        operation = activity.OperationName,
                        startedAtUtc = activity.StartTimeUtc,
                        durationMs = activity.Duration.TotalMilliseconds,
                        status = activity.Status.ToString(),
                        tags = activity.TagObjects.ToDictionary(item => item.Key, item => item.Value),
                        events = activity.Events.Select(item => new { item.Name, item.Timestamp }).ToArray(),
                    });
                }
            }
        }

        public void RecordMeasurement<T>(Instrument instrument, T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
        {
            lock (sync)
            {
                MetricCount++;
                if (metricWriter is not null)
                {
                    WriteLine(metricWriter, new
                    {
                        testId = TestId,
                        instrument = instrument.Name,
                        timestampUtc = DateTimeOffset.UtcNow,
                        value = Convert.ToString(measurement, CultureInfo.InvariantCulture),
                        traceId = Activity.Current?.TraceId.ToHexString(),
                        tags = tags.ToArray().ToDictionary(item => item.Key, item => item.Value),
                    });
                }
            }
        }

        public void MarkOrphaned()
        {
            lock (sync) { OrphanedActivities++; }
            Complete();
        }

        public void Complete()
        {
            lock (sync)
            {
                if (completed)
                    return;
                completed = true;
                activityWriter?.Flush();
                metricWriter?.Flush();
                var summary = new
                {
                    testId = TestId,
                    testName = TestName,
                    activityCount = ActivityCount,
                    metricCount = MetricCount,
                    durationMs = Root?.Duration.TotalMilliseconds ?? 0,
                    operations = operations.ToDictionary(
                        item => item.Key,
                        item => new { item.Value.Count, durationMs = item.Value.DurationMs }),
                    swallowedExceptions = SwallowedExceptions,
                    orphanedActivities = OrphanedActivities,
                };
                File.WriteAllText(SummaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
            }
        }

        public void Dispose()
        {
            Complete();
            activityWriter?.Dispose();
            metricWriter?.Dispose();
        }

        private sealed class OperationSummary
        {
            public int Count { get; set; }
            public double DurationMs { get; set; }
        }
    }
}
