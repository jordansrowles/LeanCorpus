using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares count-only retention with a byte budget while reading large binary
/// DocValues from the same set of small segments.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class SegmentReaderResourceCacheBenchmarks
{
    private const int SegmentCount = 32;
    private const int PayloadBytes = 1024 * 1024;
    private const long ResourceBudgetBytes = 4L * 1024 * 1024;

    [Params("Count only", "4 MiB byte budget")]
    public string CacheMode { get; set; } = "Count only";

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private IndexSearcher? _searcher;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot,
            $"leancorpus-bench-segment-resource-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);

        byte[] payload = new byte[PayloadBytes];
        new Random(20260927).NextBytes(payload);
        using (var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        }))
        {
            for (int documentId = 0; documentId < SegmentCount; documentId++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", documentId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new BinaryField("payload", payload));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        _searcher = new IndexSearcher(_directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            MaxCachedSegmentReaders = 256,
            MaxCachedSegmentReaderBytes = CacheMode == "Count only"
                ? long.MaxValue
                : ResourceBudgetBytes,
            ParallelSearch = false,
        });
        WarmAllPayloads();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_searcher is not null)
        {
            var metrics = _searcher.SegmentReaderCacheMetrics;
            Console.WriteLine(
                $"Segment-reader resource cache: mode={CacheMode}; entries={metrics.EntryCount}; " +
                $"retainedBytes={metrics.RetainedBytes}; maxBytes={metrics.MaximumRetainedBytes}; " +
                $"docValuesBytes={metrics.DocValuesBytes}; evictions={metrics.EvictionCount}");
            _searcher.Dispose();
        }
        _directory?.Dispose();
        BenchmarkHelpers.DeleteDirectory(_indexPath);
    }

    [Benchmark(Description = "Read large binary DocValues from all cached segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long ReadAllPayloadDocValues()
    {
        long bytes = 0;
        foreach (var reader in _searcher!.GetSegmentReaders())
        {
            byte[][][] values = reader.GetBinaryDocValues("payload")!;
            bytes += values[0][0].Length;
        }
        return bytes;
    }

    private void WarmAllPayloads()
    {
        foreach (var reader in _searcher!.GetSegmentReaders())
            _ = reader.GetBinaryDocValues("payload");
    }
}
