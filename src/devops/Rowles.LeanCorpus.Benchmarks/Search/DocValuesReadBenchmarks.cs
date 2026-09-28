using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanBinaryField = Rowles.LeanCorpus.Document.Fields.BinaryField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures explicit DocValues read paths across dense, sparse, and multi-valued columns.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(2)]
[IterationCount(5)]
public class DocValuesReadBenchmarks
{
    private const int BinaryPayloadLength = 32;
    private static readonly byte[] BinaryPayload = Enumerable.Range(0, BinaryPayloadLength)
        .Select(static value => (byte)value)
        .ToArray();

    public static IEnumerable<int> DocumentCounts => BenchmarkData.GetDocCounts(100_000, 1_000_000);

    [ParamsSource(nameof(DocumentCounts))]
    public int DocumentCount { get; set; }

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private SegmentReader? _reader;
    private NumericDocValuesColumn? _denseNumeric;
    private NumericDocValuesColumn? _sparseNumeric;
    private int[] _accessOrder = [];

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-dv-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);

        using var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            MaxBufferedDocs = Math.Min(DocumentCount, 16_384),
            RamBufferSizeMB = 256
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new LeanStringField("id", i.ToString(CultureInfo.InvariantCulture), stored: false,
                boost: 1.0f, docValues: StringDocValues.None, indexOptions: FieldIndexOptions.DocsOnly));
            document.Add(new LeanNumericField("numeric-dense", i, stored: false));
            if ((i & 7) == 0)
                document.Add(new LeanNumericField("numeric-sparse", i, stored: false));
            document.Add(new LeanStringField("sorted-high-card", $"term-{i:D8}", stored: false,
                boost: 1.0f, docValues: StringDocValues.Sorted, indexOptions: FieldIndexOptions.DocsOnly));
            document.Add(new LeanStringField("set-singleton", $"tag-{i % 256:D4}", stored: false,
                boost: 1.0f, docValues: StringDocValues.SortedSet, indexOptions: FieldIndexOptions.DocsOnly));
            for (int value = 0; value < 4; value++)
            {
                document.Add(new LeanStringField("set-multi", $"tag-{(i * 4 + value) % 4096:D4}", stored: false,
                    boost: 1.0f, docValues: StringDocValues.SortedSet, indexOptions: FieldIndexOptions.DocsOnly));
                document.Add(new LeanNumericField("numeric-multi", i * 4L + value, stored: false));
            }
            document.Add(new LeanNumericField("numeric-single", i, stored: false));
            document.Add(new LeanBinaryField("binary", BinaryPayload));
            writer.AddDocument(document);
        }
        writer.Commit();
        writer.ForceMerge(1);

        var segments = writer.GetNrtSegments();
        if (segments.Count != 1 || segments[0].DocCount != DocumentCount)
            throw new InvalidOperationException(
                $"DocValues benchmark expected one {DocumentCount}-document segment but found " +
                $"{segments.Count} segment(s) with {string.Join(", ", segments.Select(static segment => segment.DocCount))} documents.");
        _reader = new SegmentReader(_directory, segments[0]);

        if (!_reader.TryGetNumericDocValues("numeric-dense", out _denseNumeric)
            || !_reader.TryGetNumericDocValues("numeric-sparse", out _sparseNumeric))
            throw new InvalidOperationException("The benchmark index is missing its numeric DocValues columns.");

        // Explicitly warm the separately measured numeric index and each mapped DocValues reader.
        _ = _reader.TryGetNumericValue("numeric-dense", 0, out _);
        _ = _reader.TryGetSortedDocOrdinal("sorted-high-card", 0, out _);
        _ = _reader.TryGetSortedSetDocOrdinals("set-singleton", 0, out _);
        _ = _reader.TryGetSortedSetDocOrdinals("set-multi", 0, out _);
        _ = _reader.TryGetSortedNumericDocValues("numeric-single", 0, out _);
        _ = _reader.TryGetSortedNumericDocValues("numeric-multi", 0, out _);
        _ = _reader.TryGetBinaryDocValues("binary", 0, out _);

        _accessOrder = Enumerable.Range(0, DocumentCount).ToArray();
        var accessRandom = BenchmarkDeterministicRandom.Create("benchmark/docvalues/access-order");
        for (int i = _accessOrder.Length - 1; i > 0; i--)
        {
            int swapIndex = accessRandom.NextInt32(i + 1);
            (_accessOrder[i], _accessOrder[swapIndex]) = (_accessOrder[swapIndex], _accessOrder[i]);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader?.Dispose();
        _directory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_indexPath) && IODirectory.Exists(_indexPath))
            IODirectory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(Description = "Numeric DV direct dense sequential")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long NumericDocValues_DenseSequential()
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (_denseNumeric!.TryGetValue(docId, out double value))
                checksum += (long)value;
        }
        return checksum;
    }

    [Benchmark(Description = "Numeric DV direct dense random")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long NumericDocValues_DenseRandom()
    {
        long checksum = 0;
        foreach (int docId in _accessOrder)
        {
            if (_denseNumeric!.TryGetValue(docId, out double value))
                checksum += (long)value;
        }
        return checksum;
    }

    [Benchmark(Description = "Numeric DV direct sparse presence")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long NumericDocValues_SparsePresence()
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (_sparseNumeric!.TryGetValue(docId, out double value))
                checksum += (long)value;
        }
        return checksum;
    }

    [Benchmark(Description = "Numeric index dictionary lookup")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long NumericIndex_DenseLookup()
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (_reader!.TryGetNumericValue("numeric-dense", docId, out double value))
                checksum += (long)value;
        }
        return checksum;
    }

    [Benchmark(Description = "Sorted DV high-cardinality ordinal")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SortedDocValues_HighCardinalityOrdinal()
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (_reader!.TryGetSortedDocOrdinal("sorted-high-card", docId, out int ordinal))
                checksum += ordinal;
        }
        return checksum;
    }

    [Benchmark(Description = "Sorted-set DV singleton")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SortedSetDocValues_Singleton()
        => SumSortedSetOrdinals("set-singleton");

    [Benchmark(Description = "Sorted-set DV four values per document")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SortedSetDocValues_Multi()
        => SumSortedSetOrdinals("set-multi");

    [Benchmark(Description = "Sorted-numeric DV singleton")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SortedNumericDocValues_Singleton()
        => SumSortedNumericValues("numeric-single");

    [Benchmark(Description = "Sorted-numeric DV four values per document")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long SortedNumericDocValues_Multi()
        => SumSortedNumericValues("numeric-multi");

    [Benchmark(Description = "Binary DV payload reads and copies")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long BinaryDocValues_PayloadReads()
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (!_reader!.TryGetBinaryDocValues("binary", docId, out var values))
                continue;
            foreach (byte[] value in values)
                checksum += value.Length + value[0] + value[^1];
        }
        return checksum;
    }

    private long SumSortedSetOrdinals(string field)
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (!_reader!.TryGetSortedSetDocOrdinals(field, docId, out var ordinals))
                continue;
            foreach (int ordinal in ordinals)
                checksum += ordinal;
        }
        return checksum;
    }

    private long SumSortedNumericValues(string field)
    {
        long checksum = 0;
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            if (!_reader!.TryGetSortedNumericDocValues(field, docId, out var values))
                continue;
            foreach (double value in values)
                checksum += (long)value;
        }
        return checksum;
    }
}
