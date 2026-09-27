using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures repeated field/term convenience lookup and qualified-term construction costs.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QualifiedTermCacheBenchmarks
{
    private const string Field = "body";
    private const string Term = "cacheterm";
    private const string QualifiedTerm = "body\0cacheterm";

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private SegmentReader? _reader;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-qualified-term-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);

        SegmentInfo segment;
        using (var writer = new IndexWriter(_directory, new IndexWriterConfig { DurableCommits = false }))
        {
            var document = new LeanDocument();
            document.Add(new TextField(Field, Term));
            writer.AddDocument(document);
            writer.Commit();
            segment = writer.GetNrtSegments().Single();
        }

        _reader = new SegmentReader(_directory, segment);
        if (_reader.GetDocFreq(Field, Term) != 1)
            throw new InvalidOperationException("The qualified-term cache benchmark fixture is invalid.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader?.Dispose();
        _directory?.Dispose();
        BenchmarkHelpers.DeleteDirectory(_indexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int RepeatedFieldTermLookup()
        => _reader!.GetDocFreq(Field, Term);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CallerBuildsQualifiedTermEachTime()
        => _reader!.GetDocFreqByQualified(string.Concat(Field, "\0", Term));

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ReusedQualifiedTermLookup()
        => _reader!.GetDocFreqByQualified(QualifiedTerm);
}
