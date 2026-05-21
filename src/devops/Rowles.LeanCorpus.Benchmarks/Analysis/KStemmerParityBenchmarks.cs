using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures KStemmer throughput via the <see cref="StemmerAnalyser"/> pipeline.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class KStemmerParityBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private StemmerAnalyser _analyser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);

        var lexiconPath = FindKStemLexiconPath();
        _analyser = StemmerAnalyser.KStem(lexiconPath);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int KStem_Analyse()
    {
        int total = 0;
        foreach (var doc in _documents)
            total += _analyser.Analyse(doc.AsSpan()).Count;
        return total;
    }

    private static string FindKStemLexiconPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "lexicons", "kstem-dict.txt")))
            dir = dir.Parent;
        return dir is not null
            ? Path.Combine(dir.FullName, "lexicons", "kstem-dict.txt")
            : throw new InvalidOperationException("Could not find lexicons/kstem-dict.txt.");
    }
}
