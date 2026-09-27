using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LeanBinaryField = Rowles.LeanCorpus.Document.Fields.BinaryField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures DocValues read throughput via <see cref="SegmentReader"/> for
/// numeric, sorted, sorted-set, sorted-numeric, and binary doc-value types.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(2)]
[IterationCount(5)]
public class DocValuesReadBenchmarks
{
    private static readonly byte[] BinaryPayload = [1, 2, 3, 4, 5, 6, 7, 8];

    [Params(10_000, 100_000)]
    public int DocumentCount { get; set; }

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private SegmentReader? _reader;
    private int[] _accessOrder = [];

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-dv-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);

        using var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            MaxBufferedDocs = DocumentCount,
            RamBufferSizeMB = 256
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanNumericField("price", i * 1.5));
            doc.Add(new LeanStringField("category", $"cat{i % 50}"));
            doc.Add(new LeanBinaryField("payload", BinaryPayload));
            doc.Add(new LeanTextField("body", BenchmarkData.BuildDocuments(1)[0]));
            writer.AddDocument(doc);
        }
        writer.Commit();
        writer.ForceMerge(1);

        var segments = writer.GetNrtSegments();
        if (segments.Count != 1 || segments[0].DocCount != DocumentCount)
            throw new InvalidOperationException(
                $"DocValues read benchmark expected one {DocumentCount}-document segment but found " +
                $"{segments.Count} segment(s) with {string.Join(", ", segments.Select(static segment => segment.DocCount))} documents.");
        _reader = new SegmentReader(_directory, segments[0]);
        var accessRandom = BenchmarkDeterministicRandom.Create("benchmark/docvalues/access-order");
        _accessOrder = new int[DocumentCount];
        for (int i = 0; i < _accessOrder.Length; i++)
            _accessOrder[i] = accessRandom.NextInt32(DocumentCount);

        // Prime compatibility materialisations so each benchmark measures the
        // public read path, including any defensive copy, rather than first-load work.
        _ = _reader.GetNumericDocValues("price");
        _ = _reader.GetSortedDocValues("category");
        _ = _reader.GetBinaryDocValues("payload");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader?.Dispose();
        if (!string.IsNullOrWhiteSpace(_indexPath) && IODirectory.Exists(_indexPath))
            IODirectory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(Baseline = true, Description = "Numeric DV sequential read")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NumericDvSequential()
    {
        int count = 0;
        for (int i = 0; i < DocumentCount; i++)
        {
            if (_reader!.TryGetNumericValue("price", i, out _))
                count++;
        }
        return count;
    }

    [Benchmark(Description = "Numeric DV random access")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NumericDvRandom()
    {
        int count = 0;
        for (int i = 0; i < DocumentCount; i++)
        {
            int docId = _accessOrder[i];
            if (_reader!.TryGetNumericValue("price", docId, out _))
                count++;
        }
        return count;
    }

    [Benchmark(Description = "Sorted DV lookup")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SortedDvLookup()
    {
        int count = 0;
        for (int i = 0; i < DocumentCount; i++)
        {
            if (_reader!.TryGetSortedDocValue("category", i, out _))
                count++;
        }
        return count;
    }

    [Benchmark(Description = "Numeric DV dense array read")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NumericDvDense()
    {
        var arr = _reader!.GetNumericDocValues("price");
        if (arr is null) return 0;
        double sum = 0;
        foreach (var v in arr) sum += v;
        return (int)sum;
    }

    [Benchmark(Description = "Sorted DV dense array read")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SortedDvDense()
    {
        var arr = _reader!.GetSortedDocValues("category");
        if (arr is null) return 0;
        int count = 0;
        foreach (var v in arr)
            if (v is not null) count++;
        return count;
    }

    [Benchmark(Description = "Binary DV dense array read")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BinaryDvDense()
    {
        var values = _reader!.GetBinaryDocValues("payload");
        if (values is null) return 0;
        int byteCount = 0;
        foreach (var documentValues in values)
            foreach (var value in documentValues)
                byteCount += value.Length;
        return byteCount;
    }
}
