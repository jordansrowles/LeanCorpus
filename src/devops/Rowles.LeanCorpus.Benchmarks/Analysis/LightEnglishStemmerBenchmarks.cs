using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures LightEnglishStemmer throughput against Porter stemmer.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class LightEnglishStemmerBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _words = [];
    private LightEnglishStemmer _lightStemmer = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Extract individual words from benchmark documents
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        var wordList = new List<string>();
        foreach (var doc in documents)
            wordList.AddRange(doc.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        _words = wordList.ToArray();

        _lightStemmer = new LightEnglishStemmer();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LightEnglish_Stem()
    {
        int count = 0;
        foreach (var word in _words)
        {
            _lightStemmer.Stem(word);
            count++;
        }
        return count;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Porter_Stem()
    {
        // Use the StemTokenFilter wrapping the PorterStemmer adapter
        var filter = new StemTokenFilter(new PorterStemmer());
        int count = 0;

        // Process in batches to simulate filter pipeline
        var batch = new List<Token>();
        foreach (var word in _words)
        {
            batch.Add(new Token(word, 0, word.Length));
            if (batch.Count >= 1000)
            {
                var matSink = new MaterialisingTokenSink();
                foreach (var t in batch) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
                count += matSink.Tokens.Count;
                batch.Clear();
                batch.AddRange(matSink.Tokens);
            }
        }

        if (batch.Count > 0)
        {
            var matSink = new MaterialisingTokenSink();
            foreach (var t in batch) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
            count += matSink.Tokens.Count;
        }

        return count;
    }
}
