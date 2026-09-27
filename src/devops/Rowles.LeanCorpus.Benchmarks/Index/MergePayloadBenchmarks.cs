using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures merge payload remapping for wide schemas with sparse field values.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class MergePayloadBenchmarks
{
    private const int DocumentCount = 4_096;
    private const int SegmentCount = 8;
    private const int VectorFieldCount = 32;
    private string _indexPath = string.Empty;
    private List<SegmentInfo> _segments = [];

    [Params(64, 256)]
    public int SparseFieldCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _indexPath = BenchmarkHelpers.CreateTempDirectory("lc-merge-wide-sparse-payload");
        int documentsPerSegment = DocumentCount / SegmentCount;
        using var directory = new MMapDirectory(_indexPath);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxBufferedDocs = documentsPerSegment,
            MergeThreshold = int.MaxValue,
            BuildHnswOnFlush = false,
        }))
        {
            byte[] binaryPayload = [1, 2, 3, 4, 5, 6, 7, 8];
            for (int documentId = 0; documentId < DocumentCount; documentId++)
            {
                int fieldId = documentId % SparseFieldCount;
                var document = new LeanDocument();
                document.Add(new StringField("id", documentId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new NumericField($"number-{fieldId}", documentId));
                document.Add(new Int64Field($"int64-{fieldId}", documentId));
                document.Add(new StringField($"sorted-{fieldId}", $"value-{documentId:D5}"));
                document.Add(new StringField($"set-{fieldId}", $"a-{documentId:D5}"));
                document.Add(new StringField($"set-{fieldId}", $"b-{documentId:D5}"));
                document.Add(new NumericField($"multi-{fieldId}", documentId));
                document.Add(new NumericField($"multi-{fieldId}", documentId + 1));
                document.Add(new BinaryField($"binary-{fieldId}", binaryPayload));
                document.Add(new TextField($"text-{fieldId}", $"payload value-{documentId}"));

                int vectorFieldId = fieldId % VectorFieldCount;
                document.Add(new VectorField($"vector-{vectorFieldId}",
                    new ReadOnlyMemory<float>([documentId, vectorFieldId, 1f, 0.5f])));
                writer.AddDocument(document);

                if ((documentId + 1) % documentsPerSegment == 0)
                    writer.Commit();
            }
        }

        _segments = Directory.GetFiles(_indexPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
        if (_segments.Count != SegmentCount)
            throw new InvalidOperationException($"Expected {SegmentCount} source segments, found {_segments.Count}.");
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkHelpers.DeleteDirectory(_indexPath);
        _indexPath = string.Empty;
        _segments = [];
    }

    [Benchmark(Description = "Merge wide sparse payload columns")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int MergeWideSparsePayloadColumns()
    {
        using var directory = new MMapDirectory(_indexPath);
        var merger = new SegmentMerger(directory, mergeThreshold: SegmentCount + 1);
        int nextOrdinal = _segments.Max(static segment => SegmentOrdinal(segment.SegmentId)) + 1;
        SegmentInfo? merged = merger.MergeAll(_segments, ref nextOrdinal);
        return merged?.DocCount ?? 0;
    }

    private static int SegmentOrdinal(string segmentId)
        => int.Parse(segmentId.AsSpan("seg_".Length), System.Globalization.CultureInfo.InvariantCulture);
}
