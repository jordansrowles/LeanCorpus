using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
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
    private readonly ConcurrentDictionary<string, TelemetryTestSession> sessionsByTestId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ActivityTraceId, TelemetryTestSession> sessionsByTraceId = new();
    private readonly ActivityListener? activityListener;
    private readonly MeterListener? meterListener;
    private readonly string telemetryMode;
    private readonly TelemetryExecutionStore? executionStore;
    private Timer? runtimeTimer;
    private bool disposed;

    public LeanCorpusTestTelemetryFixture()
        : this(Environment.GetEnvironmentVariable("LEANCORPUS_TELEMETRY")?.ToLowerInvariant() ?? "off",
            Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR"))
    {
    }

    internal LeanCorpusTestTelemetryFixture(string mode, string? executionDirectory, TelemetryStreamLimits? limits = null)
    {
        telemetryMode = mode is "summary" or "full" ? mode : "off";
        if (telemetryMode == "off" || string.IsNullOrWhiteSpace(executionDirectory))
            return;

        try
        {
            executionStore = new TelemetryExecutionStore(executionDirectory, telemetryMode, limits ?? TelemetryStreamLimits.Default);
        }
        catch (Exception exception)
        {
            TryAddWarning($"LeanCorpus telemetry startup failed: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        activityListener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is ApplicationSourceName or TestSourceName,
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

        if (telemetryMode == "full")
        {
            WriteRuntimeSnapshot();
            try
            {
                runtimeTimer = new Timer(_ => WriteRuntimeSnapshot(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            catch (Exception exception)
            {
                executionStore.RecordTelemetryError(exception);
                TryAddWarning($"LeanCorpus runtime telemetry was disabled: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    public void OnTestStarting(IXunitTest test)
    {
        TelemetryExecutionStore? store = executionStore;
        if (store is null)
            return;

        TelemetryTestSession session = store.StartTest(test.UniqueID, test.TestDisplayName);
        if (!sessionsByTestId.TryAdd(test.UniqueID, session))
        {
            session.RecordTelemetryError(new InvalidOperationException("Duplicate telemetry session."));
            TryAddWarning($"Duplicate telemetry session for test '{test.UniqueID}'.");
            return;
        }

        try
        {
            Activity? root = testSource.StartActivity("leancorpus.test", ActivityKind.Internal);
            if (root is null)
                throw new InvalidOperationException("The LeanCorpus test telemetry root activity could not be created.");

            root.SetTag("test.id", test.UniqueID);
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
            session.RecordTelemetryError(exception);
            TryAddWarning($"LeanCorpus test telemetry setup failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public void OnTestFinished(IXunitTest test)
    {
        TelemetryExecutionStore? store = executionStore;
        if (store is null || !sessionsByTestId.TryRemove(test.UniqueID, out TelemetryTestSession? session))
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

            Xunit.ITestContext? testContext = TestContext.Current;
            Xunit.TestResult result = testContext?.TestState?.Result ?? Xunit.TestResult.NotRun;
            string[] warnings = testContext?.Warnings?.ToArray() ?? [];
            string? attachment = store.FinishTest(session, result, warnings);
            if (attachment is not null)
                testContext?.AddAttachment("leancorpus-telemetry-summary.json", attachment);
        }
        catch (Exception exception)
        {
            session.RecordTelemetryError(exception);
            TryAddWarning($"LeanCorpus test telemetry finalisation failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            session.Root?.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        WriteRuntimeSnapshot();
        disposed = true;
        try { runtimeTimer?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        runtimeTimer = null;

        TelemetryExecutionStore? store = executionStore;
        if (store is not null)
        {
            foreach ((string testId, TelemetryTestSession session) in sessionsByTestId.ToArray())
            {
                if (!sessionsByTestId.TryRemove(testId, out _))
                    continue;
                try { session.Root?.Stop(); } catch (Exception exception) { session.RecordTelemetryError(exception); }
                if (session.Root is not null)
                    sessionsByTraceId.TryRemove(session.Root.TraceId, out _);
                session.MarkOrphaned();
                store.FinishInterruptedTest(session);
                try { session.Root?.Dispose(); } catch { }
            }
            sessionsByTraceId.Clear();
        }

        meterListener?.Dispose();
        activityListener?.Dispose();
        testSource.Dispose();
        store?.Dispose();
    }

    private void RecordActivity(Activity activity)
    {
        if (sessionsByTraceId.TryGetValue(activity.TraceId, out TelemetryTestSession? session))
        {
            try { session.RecordActivity(activity); }
            catch (Exception exception) { session.RecordTelemetryError(exception); }
        }
    }

    private void RecordMeasurement<T>(Instrument instrument, T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct
    {
        TelemetryExecutionStore? store = executionStore;
        if (store is null)
            return;

        try
        {
            Activity? current = Activity.Current;
            if (instrument.Meter.Name == RuntimeMeterName)
            {
                store.RecordRuntimeMeasurement(instrument, measurement, tags, current?.TraceId.ToHexString());
                return;
            }

            TelemetryTestSession? session = null;
            if (current is not null)
                sessionsByTraceId.TryGetValue(current.TraceId, out session);
            session?.RecordMeasurement(instrument, measurement, tags);
        }
        catch (Exception exception)
        {
            if (instrument.Meter.Name == RuntimeMeterName)
                store.RecordTelemetryError(exception);
            else if (Activity.Current is Activity current && sessionsByTraceId.TryGetValue(current.TraceId, out TelemetryTestSession? session))
                session.RecordTelemetryError(exception);
        }
    }

    private void WriteRuntimeSnapshot()
    {
        TelemetryExecutionStore? store = executionStore;
        if (store is null || disposed || telemetryMode != "full")
            return;

        try
        {
            meterListener?.RecordObservableInstruments();
            using Process process = Process.GetCurrentProcess();
            store.RecordRuntimeSnapshot(new
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
        catch (Exception exception)
        {
            store.RecordTelemetryError(exception);
            TryAddWarning($"LeanCorpus runtime telemetry snapshot failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string GetTraitValues(IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits, string name) =>
        traits.TryGetValue(name, out IReadOnlyCollection<string>? values) ? string.Join(',', values) : string.Empty;

    private static void TryAddWarning(string message)
    {
        try { TestContext.Current.AddWarning(message); }
        catch { }
    }
}

internal sealed class TelemetryExecutionStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string mode;
    private readonly string summaryPath;
    private readonly BoundedNdjsonStream testStream;
    private readonly BoundedNdjsonStream? activityStream;
    private readonly BoundedNdjsonStream? metricStream;
    private readonly BoundedNdjsonStream? runtimeStream;
    private long testsStarted;
    private long testsFinished;
    private long testsPassed;
    private long testsFailed;
    private long testsSkipped;
    private long retainedTestSummaries;
    private long activities;
    private long metrics;
    private long swallowedExceptions;
    private long orphanedActivities;
    private long telemetryErrors;
    private bool disposed;

    public TelemetryExecutionStore(string executionDirectory, string mode, TelemetryStreamLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionDirectory);
        if (mode is not ("summary" or "full"))
            throw new ArgumentOutOfRangeException(nameof(mode));

        this.mode = mode;
        string telemetryDirectory = Path.Combine(executionDirectory, "telemetry");
        Directory.CreateDirectory(telemetryDirectory);
        summaryPath = Path.Combine(telemetryDirectory, "summary.json");
        testStream = new BoundedNdjsonStream(Path.Combine(telemetryDirectory, "tests.ndjson"), limits.TestsBytes, eager: mode == "full");
        if (mode == "full")
        {
            activityStream = new BoundedNdjsonStream(Path.Combine(telemetryDirectory, "activities.ndjson"), limits.ActivitiesBytes, eager: true);
            metricStream = new BoundedNdjsonStream(Path.Combine(telemetryDirectory, "metrics.ndjson"), limits.MetricsBytes, eager: true);
            string runtimeDirectory = Path.Combine(executionDirectory, "runtime");
            runtimeStream = new BoundedNdjsonStream(Path.Combine(runtimeDirectory, "counters.ndjson"), limits.RuntimeBytes, eager: true);
        }
    }

    internal string TelemetryDirectory => Path.GetDirectoryName(summaryPath)!;

    internal TelemetryTestSession StartTest(string testId, string testName)
    {
        Interlocked.Increment(ref testsStarted);
        return new TelemetryTestSession(this, testId, testName);
    }

    internal string? FinishTest(TelemetryTestSession session, Xunit.TestResult result, IReadOnlyList<string> warnings)
    {
        Interlocked.Increment(ref testsFinished);
        switch (result)
        {
            case Xunit.TestResult.Passed: Interlocked.Increment(ref testsPassed); break;
            case Xunit.TestResult.Failed: Interlocked.Increment(ref testsFailed); break;
            case Xunit.TestResult.Skipped: Interlocked.Increment(ref testsSkipped); break;
        }

        return RetainTestSummary(session, result.ToString(), warnings, force: false);
    }

    internal void FinishInterruptedTest(TelemetryTestSession session) =>
        RetainTestSummary(session, Xunit.TestResult.NotRun.ToString(), [], force: true);

    internal void RecordActivity(TelemetryTestSession session, Activity activity, int swallowedCount)
    {
        Interlocked.Increment(ref activities);
        if (swallowedCount > 0)
            Interlocked.Add(ref swallowedExceptions, swallowedCount);
        if (activityStream is null)
            return;

        object record = new
        {
            testId = session.TestId,
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
        };
        Record(activityStream, record, session);
    }

    internal void RecordMeasurement<T>(TelemetryTestSession session, Instrument instrument, T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
    {
        Interlocked.Increment(ref metrics);
        if (metricStream is null)
            return;

        object record = new
        {
            testId = session.TestId,
            instrument = instrument.Name,
            timestampUtc = DateTimeOffset.UtcNow,
            value = Convert.ToString(measurement, CultureInfo.InvariantCulture),
            traceId = Activity.Current?.TraceId.ToHexString(),
            tags = tags.ToArray().ToDictionary(item => item.Key, item => item.Value),
        };
        Record(metricStream, record, session);
    }

    internal void RecordRuntimeMeasurement<T>(Instrument instrument, T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, string? traceId) where T : struct
    {
        if (runtimeStream is null)
            return;
        Record(runtimeStream, new
        {
            type = "measurement",
            instrument = instrument.Name,
            unit = instrument.Unit,
            timestampUtc = DateTimeOffset.UtcNow,
            value = Convert.ToString(measurement, CultureInfo.InvariantCulture),
            testTraceId = traceId,
            tags = tags.ToArray().ToDictionary(item => item.Key, item => item.Value),
        }, null);
    }

    internal void RecordRuntimeSnapshot(object snapshot)
    {
        if (runtimeStream is not null)
            Record(runtimeStream, new { type = "snapshot", data = snapshot }, null);
    }

    internal void RecordTelemetryError(Exception exception)
    {
        _ = exception;
        Interlocked.Increment(ref telemetryErrors);
    }

    internal void RecordOrphanedActivity() => Interlocked.Increment(ref orphanedActivities);

    private string? RetainTestSummary(TelemetryTestSession session, string result, IReadOnlyList<string> warnings, bool force)
    {
        TelemetryTestSummary summary = session.CreateSummary(result, warnings);
        bool anomalous = force || summary.Result == Xunit.TestResult.Failed.ToString() || warnings.Count > 0 ||
            summary.SwallowedExceptions > 0 || summary.TelemetryErrorCount > 0 || summary.OrphanedActivities > 0;
        if (mode == "full" || anomalous)
        {
            string json = JsonSerializer.Serialize(summary, JsonOptions);
            Record(testStream, json, session);
            Interlocked.Increment(ref retainedTestSummaries);
            return anomalous ? json : null;
        }

        return null;
    }

    private void Record(BoundedNdjsonStream stream, object record, TelemetryTestSession? session)
    {
        string json = JsonSerializer.Serialize(record, JsonOptions);
        Record(stream, json, session);
    }

    private void Record(BoundedNdjsonStream stream, string json, TelemetryTestSession? session)
    {
        Exception? error = stream.TryAppend(json);
        if (error is null)
            return;
        if (session is null)
            RecordTelemetryError(error);
        else
            session.RecordTelemetryError(error);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        testStream.Dispose();
        activityStream?.Dispose();
        metricStream?.Dispose();
        runtimeStream?.Dispose();
        try
        {
            var summary = new
            {
                mode,
                testsStarted = Interlocked.Read(ref testsStarted),
                testsFinished = Interlocked.Read(ref testsFinished),
                testsPassed = Interlocked.Read(ref testsPassed),
                testsFailed = Interlocked.Read(ref testsFailed),
                testsSkipped = Interlocked.Read(ref testsSkipped),
                retainedTestSummaries = Interlocked.Read(ref retainedTestSummaries),
                activities = Interlocked.Read(ref activities),
                metrics = Interlocked.Read(ref metrics),
                swallowedExceptions = Interlocked.Read(ref swallowedExceptions),
                orphanedActivities = Interlocked.Read(ref orphanedActivities),
                telemetryErrors = Interlocked.Read(ref telemetryErrors),
                streams = new
                {
                    tests = testStream.Snapshot(),
                    activities = activityStream?.Snapshot() ?? TelemetryStreamStatistics.Empty,
                    metrics = metricStream?.Snapshot() ?? TelemetryStreamStatistics.Empty,
                    runtimeCounters = runtimeStream?.Snapshot() ?? TelemetryStreamStatistics.Empty,
                },
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
        }
        catch
        {
            // Telemetry finalisation must not turn a test result into a failure.
        }
    }
}

internal sealed class TelemetryTestSession(TelemetryExecutionStore owner, string testId, string testName)
{
    private readonly object sync = new();
    private readonly Dictionary<string, TelemetryOperationSummary> operations = new(StringComparer.Ordinal);
    private readonly List<string> telemetryErrors = [];
    private int activityCount;
    private int metricCount;
    private int swallowedExceptions;
    private int orphanedActivities;
    private int telemetryErrorCount;

    internal string TestId { get; } = testId;
    internal string TestName { get; } = testName;
    internal Activity? Root { get; set; }
    internal int SwallowedExceptions { get { lock (sync) return swallowedExceptions; } }
    internal int OrphanedActivities { get { lock (sync) return orphanedActivities; } }
    internal int TelemetryErrorCount { get { lock (sync) return telemetryErrorCount; } }

    internal void RecordActivity(Activity activity)
    {
        int swallowed = activity.Events.Count(item => item.Name == "exception.swallowed");
        lock (sync)
        {
            activityCount++;
            swallowedExceptions += swallowed;
            if (!operations.TryGetValue(activity.OperationName, out TelemetryOperationSummary? operation))
                operations[activity.OperationName] = operation = new TelemetryOperationSummary();
            operation.Count++;
            operation.DurationMs += activity.Duration.TotalMilliseconds;
        }
        owner.RecordActivity(this, activity, swallowed);
    }

    internal void RecordMeasurement<T>(Instrument instrument, T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
    {
        lock (sync) metricCount++;
        owner.RecordMeasurement(this, instrument, measurement, tags);
    }

    internal void RecordTelemetryError(Exception exception)
    {
        lock (sync)
        {
            telemetryErrorCount++;
            if (telemetryErrors.Count < 8)
                telemetryErrors.Add($"{exception.GetType().Name}: {exception.Message}");
        }
        owner.RecordTelemetryError(exception);
    }

    internal void MarkOrphaned()
    {
        lock (sync) orphanedActivities++;
        owner.RecordOrphanedActivity();
    }

    internal TelemetryTestSummary CreateSummary(string result, IReadOnlyList<string> warnings)
    {
        lock (sync)
        {
            return new TelemetryTestSummary
            {
                TestId = TestId,
                TestName = TestName,
                Result = result,
                Warnings = [.. warnings],
                ActivityCount = activityCount,
                MetricCount = metricCount,
                DurationMs = Root?.Duration.TotalMilliseconds ?? 0,
                Operations = operations.ToDictionary(item => item.Key,
                    item => new TelemetryOperationSummary { Count = item.Value.Count, DurationMs = item.Value.DurationMs }),
                SwallowedExceptions = swallowedExceptions,
                OrphanedActivities = orphanedActivities,
                TelemetryErrorCount = telemetryErrorCount,
                TelemetryErrors = [.. telemetryErrors],
            };
        }
    }
}

internal sealed class TelemetryOperationSummary
{
    public int Count { get; set; }
    public double DurationMs { get; set; }
}

internal sealed class TelemetryTestSummary
{
    public required string TestId { get; init; }
    public required string TestName { get; init; }
    public required string Result { get; init; }
    public required string[] Warnings { get; init; }
    public int ActivityCount { get; init; }
    public int MetricCount { get; init; }
    public double DurationMs { get; init; }
    public required Dictionary<string, TelemetryOperationSummary> Operations { get; init; }
    public int SwallowedExceptions { get; init; }
    public int OrphanedActivities { get; init; }
    public int TelemetryErrorCount { get; init; }
    public required string[] TelemetryErrors { get; init; }
}

internal sealed class BoundedNdjsonStream : IDisposable
{
    private readonly object sync = new();
    private readonly string path;
    private readonly long maximumBytes;
    private FileStream? stream;
    private long recordsWritten;
    private long recordsDropped;
    private long bytesWritten;
    private bool truncated;
    private bool disposed;

    internal BoundedNdjsonStream(string path, long maximumBytes, bool eager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        this.path = path;
        this.maximumBytes = maximumBytes;
        if (eager)
            EnsureCreated();
    }

    internal TelemetryStreamStatistics Snapshot()
    {
        lock (sync)
            return new TelemetryStreamStatistics(recordsWritten, recordsDropped, bytesWritten, truncated);
    }

    internal Exception? TryAppend(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        lock (sync)
        {
            if (disposed)
            {
                recordsDropped++;
                truncated = true;
                return null;
            }
            if (truncated || bytes.Length > maximumBytes - bytesWritten)
            {
                recordsDropped++;
                truncated = true;
                return null;
            }

            try
            {
                EnsureCreated();
                stream!.Write(bytes);
                bytesWritten += bytes.Length;
                recordsWritten++;
                return null;
            }
            catch (Exception exception)
            {
                recordsDropped++;
                truncated = true;
                try { stream?.Dispose(); } catch { }
                stream = null;
                return exception;
            }
        }
    }

    private void EnsureCreated()
    {
        if (stream is not null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            try { stream?.Flush(); } catch { truncated = true; }
            try { stream?.Dispose(); } catch { truncated = true; }
            stream = null;
        }
    }
}

internal readonly record struct TelemetryStreamStatistics(long RecordsWritten, long RecordsDropped, long BytesWritten, bool Truncated)
{
    internal static TelemetryStreamStatistics Empty => new(0, 0, 0, false);
}

internal sealed record TelemetryStreamLimits(long ActivitiesBytes, long MetricsBytes, long RuntimeBytes, long TestsBytes)
{
    internal static TelemetryStreamLimits Default { get; } = new(
        ActivitiesBytes: 64L * 1024 * 1024,
        MetricsBytes: 32L * 1024 * 1024,
        RuntimeBytes: 16L * 1024 * 1024,
        TestsBytes: 16L * 1024 * 1024);

    internal long MaximumTotalBytes => ActivitiesBytes + MetricsBytes + RuntimeBytes + TestsBytes;
}
