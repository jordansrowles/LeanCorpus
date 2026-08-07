using System.Buffers;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures LightEnglishStemmer throughput against Porter stemmer.
/// Both paths use the zero-allocation <see cref="ISpanStemmer"/> contract
/// so the allocation column reflects only unavoidable overhead.
/// For the reusable Lucene.NET pipeline comparison, see
/// <see cref="StemmerParityBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class LightEnglishStemmerBenchmarks
{
    private const int MaxWordLength = 256;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _words = [];
    private LightEnglishStemmer _lightStemmer = null!;
    private StemTokenFilter _porterFilter = null!;
    private CountingTokenSink _porterSink = null!;

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
        _porterFilter = new StemTokenFilter(new PorterStemmer());
        _porterSink = new CountingTokenSink();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LightEnglish_Stem()
    {
        int count = 0;
        char[]? rented = null;
        try
        {
            // Reuse a single pooled buffer for the entire benchmark iteration.
            Span<char> buf = (rented = ArrayPool<char>.Shared.Rent(MaxWordLength)).AsSpan(0, MaxWordLength);
            foreach (var word in _words)
            {
                if (word.Length > buf.Length)
                {
                    // Rare: word exceeds the pre-rented buffer. Grow and re-rent.
                    ArrayPool<char>.Shared.Return(rented);
                    buf = (rented = ArrayPool<char>.Shared.Rent(word.Length)).AsSpan(0, word.Length);
                }

                _lightStemmer.Stem(word.AsSpan(), buf);
                count++;
            }
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
        return count;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Porter_Stem()
    {
        _porterSink.Reset();
        foreach (var word in _words)
            _porterFilter.Apply(word.AsSpan(), 0, word.Length, Token.DefaultType, 1, null, _porterSink);
        return _porterSink.Count;
    }

}
