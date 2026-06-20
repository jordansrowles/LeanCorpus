using System.Buffers;
using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures LightEnglishStemmer throughput against Porter stemmer.
/// Both paths use the zero-allocation <see cref="ISpanStemmer"/> contract
/// so the allocation column reflects only unavoidable overhead.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class LightEnglishStemmerBenchmarks
{
    private const int MaxWordLength = 256;

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
                var sink = new CountingTokenSink();
                foreach (var t in batch) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, sink);
                count += sink.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            var sink = new CountingTokenSink();
            foreach (var t in batch) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, sink);
            count += sink.Count;
        }

        return count;
    }

    [Benchmark(Description = "Lucene.NET PorterStemFilter")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PorterStem()
    {
        int count = 0;
        foreach (var word in _words)
        {
            // Use the PorterStemFilter from Lucene.NET's analysis-en module.
            using var reader = new System.IO.StringReader(word);
            var tokeniser = new WhitespaceTokenizer(LuceneVersion.LUCENE_48, reader);
            var filter = new PorterStemFilter(tokeniser);
            filter.Reset();
            while (filter.IncrementToken())
                count++;
            filter.End();
        }
        return count;
    }
}
