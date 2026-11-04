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
/// Measures <see cref="SegmentMerger"/> throughput for small and large merges.
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
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(1_000, 10_000);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(5, 20)]
    public int SegmentCount { get; set; }

    private string[] _documents = [];
    private string _plainPath = string.Empty;
    private List<SegmentInfo> _plainSegments = [];
    private SegmentInfo? _plainMergedSegment;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _plainMergedSegment = null;
        _plainPath = BenchmarkHelpers.CreateTempDirectory("lc-merge-plain");
        _plainSegments = BuildSegments(_plainPath);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        try
        {
            if (_plainMergedSegment is not null)
                VerifyMergedIds(_plainPath, _plainMergedSegment, DocumentCount);
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(_plainPath);
            _plainSegments = [];
            _plainMergedSegment = null;
        }
    }

    [Benchmark(Baseline = true, Description = "Merge plain text segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Merge_PlainText()
    {
        using var directory = new MMapDirectory(_plainPath);
        var merger = new SegmentMerger(directory, mergeThreshold: SegmentCount + 1);
        int nextOrdinal = NextOrdinal(_plainSegments);
        _plainMergedSegment = merger.MergeAll(_plainSegments, ref nextOrdinal)
            ?? throw new InvalidOperationException("Plain-text merge produced no live segment.");
        return _plainMergedSegment.DocCount;
    }

    private static void VerifyMergedIds(string path, SegmentInfo mergedSegment, int expectedCount)
    {
        if (mergedSegment.DocCount != expectedCount || mergedSegment.LiveDocCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Merge expected {expectedCount} live documents, but found " +
                $"DocCount={mergedSegment.DocCount}, LiveDocCount={mergedSegment.LiveDocCount}.");
        }

        using var directory = new MMapDirectory(path);
        using var reader = new SegmentReader(directory, mergedSegment);
        var actualIds = new HashSet<string>(StringComparer.Ordinal);
        for (int documentId = 0; documentId < mergedSegment.DocCount; documentId++)
            actualIds.Add(reader.GetStoredFields(documentId)["id"][0]);

        if (actualIds.Count != expectedCount
            || Enumerable.Range(0, expectedCount)
                .Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Any(id => !actualIds.Contains(id)))
        {
            throw new InvalidOperationException(
                $"Merge returned {actualIds.Count} distinct IDs; expected the complete " +
                $"{expectedCount}-ID set.");
        }
    }

    private List<SegmentInfo> BuildSegments(string path)
    {
        int docsPerSegment = Math.Max(1, _documents.Length / SegmentCount);
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = docsPerSegment,
            RamBufferSizeMB = 64,
            MergeThreshold = int.MaxValue,
        };

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, config))
        {
            for (int i = 0; i < _documents.Length; i++)
            {
                var document = new LeanDocument();
                document.Add(new LeanStringField("id", i.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new LeanTextField("body", _documents[i]));

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
