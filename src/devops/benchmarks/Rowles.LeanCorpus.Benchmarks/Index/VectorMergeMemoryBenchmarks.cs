using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures managed allocation and throughput for a high-dimensional HNSW merge.</summary>
[MemoryDiagnoser]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class VectorMergeMemoryBenchmarks
{
    private const int DocumentCount = 1000;
    private const int VectorDimension = 768;
    private readonly HnswBuildConfig _hnswConfig = new() { M = 4, M0 = 8, EfConstruction = 12 };
    private float[][] _vectors = [];
    private string _path = string.Empty;
    private List<SegmentInfo> _segments = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        var random = new Random(61032);
        _vectors = new float[DocumentCount][];
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            var vector = new float[VectorDimension];
            for (int dimension = 0; dimension < vector.Length; dimension++)
                vector[dimension] = (float)(random.NextDouble() * 2.0 - 1.0);
            _vectors[docId] = vector;
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _path = BenchmarkHelpers.CreateTempDirectory("lc-vector-merge-memory");
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = DocumentCount / 2,
            RamBufferSizeMB = 512,
            MergeThreshold = int.MaxValue,
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = _hnswConfig,
            HnswSeed = 1L,
        }))
        {
            for (int docId = 0; docId < DocumentCount; docId++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", docId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(_vectors[docId])));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        _segments = IODirectory.GetFiles(_path, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        BenchmarkHelpers.DeleteDirectory(_path);
        _path = string.Empty;
        _segments = [];
    }

    [Benchmark(Description = "Merge 1000 768D HNSW vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int MergeHighDimensionalVectors()
    {
        using var directory = new MMapDirectory(_path);
        var merger = new SegmentMerger(
            directory,
            mergeThreshold: 100,
            softDeleteRetentionSeconds: 0,
            hnswBuildConfig: _hnswConfig);
        int nextOrdinal = _segments.Count;
        return merger.MergeAll(_segments, ref nextOrdinal)?.DocCount ?? 0;
    }
}
