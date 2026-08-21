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
        _before = FileSystemDiagnostics.GetSnapshot();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        var after = FileSystemDiagnostics.GetSnapshot();
        var metrics = _metrics.GetSnapshot();
        int physicalFiles = Directory.Exists(_path) ? Directory.EnumerateFiles(_path).Count() : 0;
        Console.WriteLine(
            $"LCFS durable={DurableCommits} compound={UseCompoundFile} " +
            $"fsync_ms={after.SyncElapsedMilliseconds - _before.SyncElapsedMilliseconds:F3} " +
            $"fsync_ops={after.SyncOperationCount - _before.SyncOperationCount} " +
            $"changed_sync_files={metrics.FileSyncFileCount} changed_sync_bytes={metrics.FileSyncBytes} " +
            $"retries={after.RetryCount - _before.RetryCount} " +
            $"retry_delay_ms={after.RetryDelayMilliseconds - _before.RetryDelayMilliseconds} " +
            $"files_created={after.FilesCreated - _before.FilesCreated} physical_files={physicalFiles}");
        RecentFeatureBenchmarkIndex.Delete(_path);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int IndexAndCommit()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            RamBufferSizeMB = 256,
            DurableCommits = DurableCommits,
            UseCompoundFile = UseCompoundFile,
            Metrics = _metrics
        });

        for (int i = 0; i < _documents.Length; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            document.Add(new TextField("body", _documents[i], stored: true));
            document.Add(new StringField("category", $"category-{i % 32}"));
            document.Add(new NumericField("rank", i % 1_000));
            writer.AddDocument(document);
        }
        writer.Commit();
        return _documents.Length;
    }
}
