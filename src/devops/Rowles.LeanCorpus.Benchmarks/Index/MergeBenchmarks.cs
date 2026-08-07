using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LeanVectorField = Rowles.LeanCorpus.Document.Fields.VectorField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="SegmentMerger"/> throughput for small and large merges,
/// with and without HNSW vectors.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class MergeBenchmarks
{
    [Params(1_000, 10_000)]
    public int DocumentCount { get; set; }

    [Params(5, 20)]
    public int SegmentCount { get; set; }

    private string[] _documents = [];
    private string _plainPath = string.Empty;
    private string _hnswPath = string.Empty;
    private List<SegmentInfo> _plainSegments = [];
    private List<SegmentInfo> _hnswSegments = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _plainPath = BenchmarkHelpers.CreateTempDirectory("lc-merge-plain");
        _hnswPath = BenchmarkHelpers.CreateTempDirectory("lc-merge-hnsw");
        _plainSegments = BuildSegments(_plainPath, withHnswVectors: false);
        _hnswSegments = BuildSegments(_hnswPath, withHnswVectors: true);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        BenchmarkHelpers.DeleteDirectory(_plainPath);
        BenchmarkHelpers.DeleteDirectory(_hnswPath);
        _plainSegments = [];
        _hnswSegments = [];
    }

    [Benchmark(Baseline = true, Description = "Merge plain text segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Merge_PlainText()
    {
        using var directory = new MMapDirectory(_plainPath);
        var merger = new SegmentMerger(directory, mergeThreshold: SegmentCount + 1);
        int nextOrdinal = NextOrdinal(_plainSegments);
        _ = merger.MergeAll(_plainSegments, ref nextOrdinal);
        return _plainSegments.Sum(static segment => segment.DocCount);
    }

    [Benchmark(Description = "Merge segments with HNSW vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Merge_WithHnswVectors()
    {
        using var directory = new MMapDirectory(_hnswPath);
        var merger = new SegmentMerger(directory, mergeThreshold: SegmentCount + 1);
        int nextOrdinal = NextOrdinal(_hnswSegments);
        _ = merger.MergeAll(_hnswSegments, ref nextOrdinal);
        return _hnswSegments.Sum(static segment => segment.DocCount);
    }

    private List<SegmentInfo> BuildSegments(string path, bool withHnswVectors)
    {
        int docsPerSegment = Math.Max(1, _documents.Length / SegmentCount);
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = docsPerSegment,
            RamBufferSizeMB = 64,
            MergeThreshold = int.MaxValue,
            BuildHnswOnFlush = withHnswVectors,
            HnswSeed = 1L,
        };
        if (withHnswVectors)
            config.HnswBuildConfig = new HnswBuildConfig { M = 8, M0 = 16, EfConstruction = 50 };

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, config))
        {
            var random = new Random(7);
            for (int i = 0; i < _documents.Length; i++)
            {
                var document = new LeanDocument();
                document.Add(new LeanStringField("id", i.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new LeanTextField("body", _documents[i]));

                if (withHnswVectors)
                {
                    var vector = new float[64];
                    for (int dimension = 0; dimension < vector.Length; dimension++)
                        vector[dimension] = (float)(random.NextDouble() * 2 - 1);
                    document.Add(new LeanVectorField("emb", new ReadOnlyMemory<float>(vector)));
                }

                writer.AddDocument(document);
            }

            writer.Commit();
        }

        return IODirectory.GetFiles(path, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
    }

    private static int NextOrdinal(IReadOnlyList<SegmentInfo> segments)
    {
        int max = -1;
        foreach (var segment in segments)
        {
            if (!segment.SegmentId.StartsWith("seg_", StringComparison.Ordinal)
                || !int.TryParse(segment.SegmentId.AsSpan(4), out int ordinal))
            {
                throw new InvalidOperationException($"Unexpected segment ID '{segment.SegmentId}'.");
            }

            max = Math.Max(max, ordinal);
        }

        return checked(max + 1);
    }
}
