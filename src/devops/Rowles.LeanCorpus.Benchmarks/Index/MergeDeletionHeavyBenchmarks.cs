using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Exercises a full merge when half of the source documents are deleted.
/// </summary>
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class MergeDeletionHeavyBenchmarks
{
    public static IEnumerable<int> DocCounts => [1_000];

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(5, 20)]
    public int SegmentCount { get; set; }

    private string[] _documents = [];
    private HashSet<string> _expectedIds = new(StringComparer.Ordinal);
    private string _path = string.Empty;
    private List<SegmentInfo> _segments = [];
    private SegmentInfo? _mergedSegment;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _expectedIds = Enumerable.Range(0, DocumentCount)
            .Where(static id => id % 2 == 0)
            .Select(FormatId)
            .ToHashSet(StringComparer.Ordinal);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _path = BenchmarkHelpers.CreateTempDirectory("lc-merge-deletion-heavy");
        _mergedSegment = null;
        int documentsPerSegment = DocumentCount / SegmentCount;
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = documentsPerSegment,
            RamBufferSizeMB = 64,
            MergeThreshold = int.MaxValue,
            MergePolicy = NoMergePolicy.Instance,
        };

        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, config))
        {
            for (int id = 0; id < DocumentCount; id++)
            {
                var document = new LeanDocument();
                document.Add(new LeanStringField("id", FormatId(id)));
                document.Add(new LeanTextField("body", _documents[id]));
                writer.AddDocument(document);
            }

            writer.Commit();
            for (int id = 1; id < DocumentCount; id += 2)
                writer.DeleteDocuments(new LeanTermQuery("id", FormatId(id)));

            writer.Commit();
        }

        var commit = IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)
            ?? throw new InvalidOperationException("Deletion-heavy merge fixture did not publish a commit.");
        _segments = commit.SegmentInfos
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();

        int sourceDocumentCount = _segments.Sum(static segment => segment.DocCount);
        int sourceLiveDocumentCount = _segments.Sum(static segment => segment.LiveDocCount);
        if (_segments.Count != SegmentCount || sourceDocumentCount != DocumentCount)
        {
            throw new InvalidOperationException(
                $"Deletion-heavy merge fixture expected {SegmentCount} segments and {DocumentCount} source documents, " +
                $"but found {_segments.Count} segments and {sourceDocumentCount} documents.");
        }

        if (sourceLiveDocumentCount != _expectedIds.Count)
        {
            throw new InvalidOperationException(
                $"Deletion-heavy merge fixture expected {_expectedIds.Count} live source documents after deleting odd IDs, " +
                $"but found {sourceLiveDocumentCount}.");
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        try
        {
            if (_mergedSegment is not null)
                VerifyMergedIds(_path, _mergedSegment, _expectedIds);
        }
        finally
        {
            _mergedSegment = null;
            _segments = [];
            BenchmarkHelpers.DeleteDirectory(_path);
            _path = string.Empty;
        }
    }

    [Benchmark(Description = "Merge segments with 50 per cent deleted documents")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Merge_DeletionHeavy()
    {
        using var directory = new MMapDirectory(_path);
        var merger = new SegmentMerger(directory, mergeThreshold: SegmentCount + 1);
        int nextOrdinal = NextOrdinal(_segments);
        _mergedSegment = merger.MergeAll(_segments, ref nextOrdinal)
            ?? throw new InvalidOperationException("Deletion-heavy merge produced no live segment.");
        return _mergedSegment.DocCount;
    }

    private static void VerifyMergedIds(string path, SegmentInfo mergedSegment, HashSet<string> expectedIds)
    {
        if (mergedSegment.DocCount != expectedIds.Count || mergedSegment.LiveDocCount != expectedIds.Count)
        {
            throw new InvalidOperationException(
                $"Deletion-heavy merge expected {expectedIds.Count} merged live documents, " +
                $"but found DocCount={mergedSegment.DocCount}, LiveDocCount={mergedSegment.LiveDocCount}.");
        }

        using var directory = new MMapDirectory(path);
        using var reader = new SegmentReader(directory, mergedSegment);
        var actualIds = new HashSet<string>(StringComparer.Ordinal);
        for (int documentId = 0; documentId < mergedSegment.DocCount; documentId++)
            actualIds.Add(reader.GetStoredFields(documentId)["id"][0]);

        if (actualIds.Count != expectedIds.Count || !expectedIds.SetEquals(actualIds))
        {
            throw new InvalidOperationException(
                $"Deletion-heavy merge returned {actualIds.Count} distinct IDs; expected the exact " +
                $"{expectedIds.Count}-ID survivor set.");
        }
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

    private static string FormatId(int id) => id.ToString(CultureInfo.InvariantCulture);
}
