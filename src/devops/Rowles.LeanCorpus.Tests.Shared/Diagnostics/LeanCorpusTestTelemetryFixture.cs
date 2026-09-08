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
    private StreamWriter? runtimeWriter;
    private Timer? runtimeTimer;
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
                try
                {
                    string runtimeDirectory = Path.Combine(executionDirectory, "runtime");
                    Directory.CreateDirectory(runtimeDirectory);
                    runtimeWriter = CreateWriter(Path.Combine(runtimeDirectory, "counters.ndjson"));
                }
                catch (Exception exception)
                {
                    TryAddWarning($"LeanCorpus runtime telemetry startup failed: {exception.GetType().Name}: {exception.Message}");
                }
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
            if (runtimeWriter is not null)
            {
                try
                {
                    runtimeTimer = new Timer(_ => WriteRuntimeSnapshot(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                }
                catch (Exception exception)
                {
                    DisableRuntimeTelemetry(exception);
                }
            }
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
        TestTelemetrySession? session = null;
        Activity? root = null;
        try
        {
            string testDirectory = Path.Combine(executionDirectory, "telemetry", "tests", SanitisePath(testId));
            session = new TestTelemetrySession(testId, test.TestDisplayName, testDirectory, telemetryMode == "full");
            if (!sessionsByTestId.TryAdd(testId, session))
            {
                session.Dispose();
                TryAddWarning($"Duplicate telemetry session for test '{testId}'.");
                return;
            }

            root = testSource.StartActivity("leancorpus.test", ActivityKind.Internal);
            if (root is null)
                throw new InvalidOperationException("The LeanCorpus test telemetry root activity could not be created.");

            root.SetTag("test.id", testId);
            root.SetTag("test.name", test.TestDisplayName);
            root.SetTag("test.label", test.TestLabel);
            root.SetTag("test.class", test.TestCase.TestClassName);
            root.SetTag("test.method", test.TestCase.TestMethodName);
            root.SetTag("test.suite", Environment.GetEnvironmentVariable("LEANCORPUS_SUITE"));
            root.SetTag("test.category", GetTraitValues(test.TestCase.Traits, "Category"));
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
        catch (Exception exception)
        {
            sessionsByTestId.TryRemove(testId, out _);
            if (root is not null)
                sessionsByTraceId.TryRemove(root.TraceId, out _);
            try { root?.Dispose(); } catch { }
            try { session?.Dispose(); } catch { }
            TryAddWarning($"LeanCorpus test telemetry setup failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public void OnTestFinished(IXunitTest test)
    {
        if (!sessionsByTestId.TryRemove(test.UniqueID, out TestTelemetrySession? session))
            return;

        try
        {
            try
            {
                session.Root?.Stop();
            }
            catch (Exception exception)
            {
                session.RecordTelemetryError(exception);
                TryAddWarning($"LeanCorpus test telemetry root shutdown failed: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                if (session.Root is not null)
                    sessionsByTraceId.TryRemove(session.Root.TraceId, out _);
            }

            session.CloseWriters();
            session.Complete();
            if (session.SwallowedExceptions > 0)
                TryAddWarning($"LeanCorpus recorded {session.SwallowedExceptions} swallowed exception event(s).");
            TestContext.Current.AddAttachment(
                "leancorpus-telemetry-summary.json",
                File.ReadAllBytes(session.SummaryPath),
                "application/json");
        }
        catch (Exception exception)
        {
            session.RecordTelemetryError(exception);
            try
            {
                session.CloseWriters();
                session.Complete();
            }
            catch { }
            TryAddWarning($"LeanCorpus test telemetry finalisation failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            session.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        DisableRuntimeTelemetry(null);
        meterListener?.Dispose();
        activityListener?.Dispose();
        testSource.Dispose();
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
        {
            try { session.RecordActivity(activity); }
            catch (Exception exception) { session.RecordTelemetryError(exception); }
        }
    }

    private void RecordMeasurement<T>(Instrument instrument, T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct
    {
        TestTelemetrySession? session = null;
        try
        {
            Activity? current = Activity.Current;
            if (current is not null)
                sessionsByTraceId.TryGetValue(current.TraceId, out session);

            if (instrument.Meter.Name == RuntimeMeterName)
            {
                lock (runtimeSync)
                {
                    StreamWriter? writer = runtimeWriter;
                    if (writer is null)
                        return;
                    WriteLine(writer, new
                    {
                        instrument = instrument.Name,
                        unit = instrument.Unit,
                        timestampUtc = DateTimeOffset.UtcNow,
                        value = Convert.ToString(measurement, CultureInfo.InvariantCulture),
                        testTraceId = current?.TraceId.ToHexString(),
                        tags = tags.ToArray().ToDictionary(item => item.Key, item => item.Value),
                    });
                }
                return;
            }

            session?.RecordMeasurement(instrument, measurement, tags);
        }
        catch (Exception exception)
        {
            if (instrument.Meter.Name == RuntimeMeterName)
                DisableRuntimeTelemetry(exception);
            else
                session?.RecordTelemetryError(exception);
        }
    }

    private static void TryAddWarning(string message)
    {
        try { TestContext.Current.AddWarning(message); }
        catch { }
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

    private static string GetTraitValues(IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits, string name) =>
        traits.TryGetValue(name, out IReadOnlyCollection<string>? values) ? string.Join(',', values) : string.Empty;

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
        catch (Exception exception)
        {
            if (!disposed)
                DisableRuntimeTelemetry(exception);
        }
    }

    private void DisableRuntimeTelemetry(Exception? exception)
    {
        Timer? timer;
        StreamWriter? writer;
        lock (runtimeSync)
        {
            timer = runtimeTimer;
            writer = runtimeWriter;
            runtimeTimer = null;
            runtimeWriter = null;
        }

        try { timer?.Dispose(); } catch { }
        try { writer?.Dispose(); } catch { }
        if (exception is not null && !disposed)
            TryAddWarning($"LeanCorpus runtime telemetry was disabled: {exception.GetType().Name}: {exception.Message}");
    }

    private sealed class TestTelemetrySession : IDisposable
    {
        internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private readonly Lock sync = new();
        private readonly Dictionary<string, OperationSummary> operations = new(StringComparer.Ordinal);
        private readonly StreamWriter? activityWriter;
        private readonly StreamWriter? metricWriter;
        private readonly List<string> telemetryErrors = [];
        private bool completed;
        private bool writersClosed;

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
        public int TelemetryErrorCount { get; private set; }

        public void RecordTelemetryError(Exception exception)
        {
            try
            {
                lock (sync)
                {
                    TelemetryErrorCount++;
                    if (telemetryErrors.Count < 8)
                        telemetryErrors.Add($"{exception.GetType().Name}: {exception.Message}");
                    completed = false;
                }
            }
            catch { }
        }

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
                        events = activity.Events.Select(item => new
                        {
                            item.Name,
                            item.Timestamp,
                            tags = item.Tags.ToDictionary(tag => tag.Key, tag => tag.Value),
                        }).ToArray(),
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
                if (!writersClosed)
                {
                    activityWriter?.Flush();
                    metricWriter?.Flush();
                }
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
                    telemetryErrorCount = TelemetryErrorCount,
                    telemetryErrors = telemetryErrors.ToArray(),
                    activitiesFile = FullTelemetry ? Path.GetFileName(ActivitiesPath) : null,
                    metricsFile = FullTelemetry ? Path.GetFileName(MetricsPath) : null,
                };
                File.WriteAllText(SummaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
                completed = true;
            }
        }

        public void CloseWriters()
        {
            Exception? activityError = null;
            Exception? metricError = null;
            lock (sync)
            {
                if (writersClosed)
                    return;
                writersClosed = true;
                try { activityWriter?.Dispose(); }
                catch (Exception exception) { activityError = exception; }
                try { metricWriter?.Dispose(); }
                catch (Exception exception) { metricError = exception; }
            }
            if (activityError is not null)
                RecordTelemetryError(activityError);
            if (metricError is not null)
                RecordTelemetryError(metricError);
        }

        public void Dispose()
        {
            CloseWriters();
            try { Complete(); }
            catch { }
        }

        private sealed class OperationSummary
        {
            public int Count { get; set; }
            public double DurationMs { get; set; }
        }
    }
}
