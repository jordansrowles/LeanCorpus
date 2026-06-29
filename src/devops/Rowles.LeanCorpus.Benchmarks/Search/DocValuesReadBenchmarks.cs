using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures DocValues read throughput via <see cref="SegmentReader"/> for
/// numeric, sorted, sorted-set, and sorted-numeric doc-value types.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class DocValuesReadBenchmarks
{
    [Params(10_000, 100_000)]
    public int DocumentCount { get; set; }

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private SegmentReader? _reader;

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
            doc.Add(new LeanTextField("body", BenchmarkData.BuildDocuments(1)[0]));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var segments = writer.GetNrtSegments();
        _reader = new SegmentReader(_directory, segments[0]);
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
        var rnd = new Random(7);
        int count = 0;
        for (int i = 0; i < DocumentCount; i++)
        {
            int docId = rnd.Next(DocumentCount);
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
}