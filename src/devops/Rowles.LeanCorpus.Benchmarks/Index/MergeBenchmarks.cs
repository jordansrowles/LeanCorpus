using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="SegmentMerger"/> throughput for small and large merges,
/// with and without HNSW vectors, and with soft-delete markers.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class MergeBenchmarks
{
    [Params(1_000, 10_000)]
    public int DocumentCount { get; set; }

    [Params(5, 20)]
    public int SegmentCount { get; set; }

    private string[] _documents = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
    }

    [Benchmark(Baseline = true, Description = "Merge plain text segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Merge_PlainText()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-merge-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);

        try
        {
            var dir = new MMapDirectory(path);
            int docsPerSegment = Math.Max(1, _documents.Length / SegmentCount);
            int nextOrdinal = 0;

            // Build segments with small MaxBufferedDocs to force many flushes.
            var segments = new List<SegmentInfo>();
            int offset = 0;
            for (int s = 0; s < SegmentCount && offset < _documents.Length; s++)
            {
                int count = Math.Min(docsPerSegment, _documents.Length - offset);
                using var writer = new IndexWriter(dir, new IndexWriterConfig
                {
                    MaxBufferedDocs = count,
                    RamBufferSizeMB = 64
                });
                for (int i = offset; i < offset + count; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new LeanStringField("id",
                        i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    doc.Add(new LeanTextField("body", _documents[i]));
                    writer.AddDocument(doc);
                }
                writer.Commit();
                segments = writer.GetNrtSegments().ToList();
                offset += count;
            }

            // Time the merge itself.
            var merger = new SegmentMerger(dir, mergeThreshold: SegmentCount + 1);
            _ = merger.MergeAll(segments, ref nextOrdinal);
            return segments.Sum(s => s.DocCount);
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    [Benchmark(Description = "Merge segments with HNSW vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Merge_WithHnswVectors()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-merge-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);

        try
        {
            var dir = new MMapDirectory(path);
            int docsPerSegment = Math.Max(1, _documents.Length / SegmentCount);
            int nextOrdinal = 0;
            var rnd = new Random(7);

            var segments = new List<SegmentInfo>();
            int offset = 0;
            for (int s = 0; s < SegmentCount && offset < _documents.Length; s++)
            {
                int count = Math.Min(docsPerSegment, _documents.Length - offset);
                using var writer = new IndexWriter(dir, new IndexWriterConfig
                {
                    MaxBufferedDocs = count,
                    RamBufferSizeMB = 64,
                    BuildHnswOnFlush = true,
                    HnswBuildConfig = new Rowles.LeanCorpus.Codecs.Hnsw.HnswBuildConfig
                        { M = 8, M0 = 16, EfConstruction = 50 },
                    HnswSeed = 1L,
                });
                for (int i = offset; i < offset + count; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new LeanStringField("id",
                        i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    doc.Add(new LeanTextField("body", _documents[i]));
                    // Add a 64-dim vector to trigger HNSW build on flush.
                    var vec = new float[64];
                    for (int d = 0; d < 64; d++)
                        vec[d] = (float)(rnd.NextDouble() * 2 - 1);
                    doc.Add(new Rowles.LeanCorpus.Document.Fields.VectorField("emb",
                        new ReadOnlyMemory<float>(vec)));
                    writer.AddDocument(doc);
                }
                writer.Commit();
                segments = writer.GetNrtSegments().ToList();
                offset += count;
            }

            var merger = new SegmentMerger(dir, mergeThreshold: SegmentCount + 1);
            _ = merger.MergeAll(segments, ref nextOrdinal);
            return segments.Sum(s => s.DocCount);
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }
}