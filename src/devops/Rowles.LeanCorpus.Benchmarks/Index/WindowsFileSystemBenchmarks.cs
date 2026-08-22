using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures the issue #59 durability and physical-file-count matrix on Windows.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[InvocationCount(1)]
public class WindowsFileSystemBenchmarks
{
    private string[] _documents = [];
    private string _path = string.Empty;
    private DefaultMetricsCollector _metrics = new();
    private FileSystemDiagnosticsSnapshot _before;
    private string _scenario = string.Empty;
    private IDisposable? _detailedDiagnostics;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(false, true)]
    public bool DurableCommits { get; set; }

    [Params(false, true)]
    public bool UseCompoundFile { get; set; }

    [GlobalSetup]
    public void GlobalSetup() => _documents = BenchmarkData.BuildDocuments(DocumentCount);

    [IterationSetup]
    public void IterationSetup()
    {
        _path = Path.Combine(BenchmarkHelpers.TempRoot, $"windows-filesystem-{Guid.NewGuid():N}");
        _metrics = new DefaultMetricsCollector();
        _detailedDiagnostics = FileSystemDiagnostics.BeginDetailedMeasurement();
        _before = FileSystemDiagnostics.GetSnapshot();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        var after = FileSystemDiagnostics.GetSnapshot();
        var metrics = _metrics.GetSnapshot();
        int physicalFiles = Directory.Exists(_path) ? Directory.EnumerateFiles(_path).Count() : 0;
        Console.WriteLine(
            $"LCFS scenario={_scenario} durable={DurableCommits} compound={UseCompoundFile} " +
            $"fsync_ms={after.SyncElapsedMilliseconds - _before.SyncElapsedMilliseconds:F3} " +
            $"fsync_ops={after.SyncOperationCount - _before.SyncOperationCount} " +
            $"file_sync_ms={after.FileSyncElapsedMilliseconds - _before.FileSyncElapsedMilliseconds:F3} " +
            $"file_sync_ops={after.FileSyncCount - _before.FileSyncCount} " +
            $"directory_sync_ms={after.DirectorySyncElapsedMilliseconds - _before.DirectorySyncElapsedMilliseconds:F3} " +
            $"directory_sync_attempts={after.DirectorySyncAttemptCount - _before.DirectorySyncAttemptCount} " +
            $"directory_sync_successes={after.DirectorySyncSuccessCount - _before.DirectorySyncSuccessCount} " +
            $"directory_sync_unsupported={after.DirectorySyncUnsupportedCount - _before.DirectorySyncUnsupportedCount} " +
            $"directory_sync_skipped={after.DirectorySyncSkippedCount - _before.DirectorySyncSkippedCount} " +
            $"dirty_registrations={after.DirtyRegistrations - _before.DirtyRegistrations} " +
            $"dirty_snapshots={after.DirtySnapshotCount - _before.DirtySnapshotCount} " +
            $"dirty_scanned={after.DirtySnapshotEntriesScanned - _before.DirtySnapshotEntriesScanned} " +
            $"dirty_returned={after.DirtySnapshotEntriesReturned - _before.DirtySnapshotEntriesReturned} " +
            $"immediate_durable_atomic_writes={after.ImmediateDurableAtomicWriteCount - _before.ImmediateDurableAtomicWriteCount} " +
            $"changed_sync_files={metrics.FileSyncFileCount} changed_sync_bytes={metrics.FileSyncBytes} " +
            $"retries={after.RetryCount - _before.RetryCount} " +
            $"retry_delay_ms={after.RetryDelayMilliseconds - _before.RetryDelayMilliseconds} " +
            $"files_created={after.FilesCreated - _before.FilesCreated} physical_files={physicalFiles}");
        _detailedDiagnostics?.Dispose();
        _detailedDiagnostics = null;
        RecentFeatureBenchmarkIndex.Delete(_path);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FreshCommit()
    {
        _scenario = "fresh";
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using var writer = CreateWriter(directory);

        for (int i = 0; i < _documents.Length; i++)
            writer.AddDocument(CreateDocument(i, _documents[i]));
        writer.Commit();
        return _documents.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int RepeatedSmallCommits()
    {
        _scenario = "repeated";
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using var writer = CreateWriter(directory);

        int baselineCount = Math.Max(1, _documents.Length - 2);
        for (int i = 0; i < baselineCount; i++)
            writer.AddDocument(CreateDocument(i, _documents[i]));
        writer.Commit();

        int indexed = baselineCount;
        for (int i = 0; i < 2; i++)
        {
            int sourceIndex = Math.Min(indexed, _documents.Length - 1);
            writer.AddDocument(CreateDocument(indexed, _documents[sourceIndex]));
            indexed++;
            writer.Commit();
        }

        writer.Commit();
        return indexed;
    }

    private IndexWriter CreateWriter(MMapDirectory directory) => new(directory, new IndexWriterConfig
    {
        MaxBufferedDocs = 1_000,
        RamBufferSizeMB = 256,
        DurableCommits = DurableCommits,
        UseCompoundFile = UseCompoundFile,
        MergePolicy = NoMergePolicy.Instance,
        Metrics = _metrics
    });

    private static LeanDocument CreateDocument(int id, string body)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        document.Add(new TextField("body", body, stored: true));
        document.Add(new StringField("category", $"category-{id % 32}"));
        document.Add(new NumericField("rank", id % 1_000));
        return document;
    }
}
