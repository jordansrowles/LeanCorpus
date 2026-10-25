using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

/// <summary>One opt-in investigation run. Trace identity excludes concurrent tests.</summary>
internal sealed class BackupRestoreMeasurement : IDisposable
{
    private static readonly AsyncLocal<BackupRestoreMeasurement?> Ambient = new();
    private readonly Activity _root;
    private readonly ActivityListener _listener;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CategoryTotal> _categories = new(StringComparer.Ordinal);
    private readonly List<object> _paths = [];
    private readonly List<string> _operations = [];
    private readonly string _outputPath;
    private readonly BackupRestoreMeasurement? _previous;
    internal static BackupRestoreMeasurement? Current => Ambient.Value;
    internal bool Compound { get; }
    internal bool SourceDurable { get; }
    internal string? TempRoot { get; }
    internal bool Passed { get; set; }
    internal ulong Seed { get; }
    internal ulong Gamma { get; }

    internal BackupRestoreMeasurement(string outputPath, ulong seed, ulong gamma)
    {
        _outputPath = Path.GetFullPath(outputPath);
        Seed = seed;
        Gamma = gamma;
        Compound = ReadFlag("LEANCORPUS_BACKUP_COMPOUND", false);
        SourceDurable = ReadFlag("LEANCORPUS_BACKUP_SOURCE_DURABLE", true);
        TempRoot = Environment.GetEnvironmentVariable("LEANCORPUS_BACKUP_TEMP_ROOT");
        _root = new Activity("backup-restore.measurement").SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom()).Start();
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Rowles.LeanCorpus.FileSystem" or "Rowles.LeanCorpus",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == _root.TraceId ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = Record
        };
        ActivitySource.AddActivityListener(_listener);
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    internal static Activity? Time(string category) => Current is null
        ? null : FileSystemMeasurement.Start("harness." + category);

    internal static Activity? Operation(string operation)
    {
        var current = Current;
        if (current is null)
            return null;
        lock (current._lock)
            current._operations.Add(operation);
        return Time("operation");
    }

    internal void RecordPaths(string source, string backup, string restore)
    {
        lock (_lock)
            _paths.Add(new { Source = DescribePath(source), Backup = DescribePath(backup), Restore = DescribePath(restore) });
    }

    private static object DescribePath(string path)
    {
        var full = Path.GetFullPath(path);
        var volume = DriveInfo.GetDrives().Where(drive => drive.IsReady &&
                full.StartsWith(drive.RootDirectory.FullName, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length).FirstOrDefault();
        return new { Path = full, Volume = volume?.Name, Format = volume?.DriveFormat, Type = volume?.DriveType.ToString() };
    }

    private void Record(Activity activity)
    {
        if (activity.TraceId != _root.TraceId)
            return;
        string category = activity.OperationName;
        if (category == "file.atomic_write")
            category = activity.GetTagItem("durable") is true ? "durable.atomic_write" : "nondurable.atomic_write";
        if (category == "backup.file_copy")
        {
            bool restore = false;
            for (Activity? parent = activity.Parent; parent is not null; parent = parent.Parent)
                restore |= parent.OperationName is "harness.restore" or "harness.corrupt_restore";
            category = restore ? "restore.file_copy" : "backup.file_copy";
        }
        lock (_lock)
        {
            if (!_categories.TryGetValue(category, out var total))
                _categories[category] = total = new CategoryTotal();
            total.Count++;
            total.ElapsedMilliseconds += activity.Duration.TotalMilliseconds;
            if (activity.GetTagItem("bytes") is long bytes)
                total.Bytes += bytes;
            if (activity.GetTagItem("delay_ms") is int delay)
                total.RetryDelayMilliseconds += delay;
            if (activity.GetTagItem("result") is string result)
                total.Results[result] = total.Results.GetValueOrDefault(result) + 1;
        }
    }

    public void Dispose()
    {
        Ambient.Value = _previous;
        _elapsed.Stop();
        _listener.Dispose();
        _root.Dispose();
        var report = new
        {
            SchemaVersion = 1, Passed, Seed, Gamma,
            EffectiveIterations = Infrastructure.ChaosConfig.FsCheckIterations,
            StartSize = 1, EndSize = 30, MaxTraceLength = 30,
            Compound, SourceDurable, BackupRestoreDurable = true, TempRoot,
            Framework = AppContext.TargetFrameworkName,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Machine = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            SourceCommit = Environment.GetEnvironmentVariable("LEANCORPUS_BACKUP_SOURCE_COMMIT"),
            SourceFingerprint = Environment.GetEnvironmentVariable("LEANCORPUS_BACKUP_SOURCE_FINGERPRINT"),
            CoreAssemblySha256 = HashFile(typeof(IndexWriter).Assembly.Location),
            TestAssemblySha256 = HashFile(typeof(BackupRestoreMeasurement).Assembly.Location),
            ElapsedMilliseconds = _elapsed.Elapsed.TotalMilliseconds,
            OperationCount = _operations.Count,
            OperationFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', _operations)))),
            TimingSemantics = "Inclusive nested durations; categories must not be added together. Counts include failed operations. Copy bytes are actual transferred bytes, including failed copies; tracked output bytes are final stream lengths.",
            Categories = _categories, Paths = _paths
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        File.WriteAllText(_outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool ReadFlag(string name, bool fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return value is null ? fallback : bool.Parse(value);
    }

    private sealed class CategoryTotal
    {
        public long Count { get; set; }
        public long Bytes { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public long RetryDelayMilliseconds { get; set; }
        public Dictionary<string, long> Results { get; } = new(StringComparer.Ordinal);
    }
}
