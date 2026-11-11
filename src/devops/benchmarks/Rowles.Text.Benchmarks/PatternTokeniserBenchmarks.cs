using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares regex-based tokenisation throughput between LeanCorpus
/// <see cref="PatternTokeniser"/> and Lucene.NET
/// <c>PatternTokenizer</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class PatternTokeniserBenchmarks
{
    [Params(
        "comma-short",
        "comma-long",
        "whitespace-short",
        "whitespace-long")]
    public string Scenario { get; set; } = "comma-short";

    // LeanCorpus state
    private ISpanTokeniser _tokeniser = null!;
    private string _input = string.Empty;
    private CountingTokenSink _sink = null!;

    // Lucene.NET state
    private Regex _lucenePattern = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sink = new CountingTokenSink();
        (_tokeniser, _input, _lucenePattern) = Scenario switch
        {
            "comma-short" => (
                (ISpanTokeniser)new PatternTokeniser(@"[^,]+"),
                "alpha,beta,gamma,delta,epsilon,zeta,eta,theta",
                new Regex(@"[^,]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            "comma-long" => (
                (ISpanTokeniser)new PatternTokeniser(@"[^,]+"),
                string.Join(",", Enumerable.Range(0, 500).Select(i => $"token{i}")),
                new Regex(@"[^,]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            "whitespace-short" => (
                (ISpanTokeniser)new PatternTokeniser(@"\S+"),
                "the quick brown fox jumped over the lazy dog",
                new Regex(@"\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            "whitespace-long" => (
                (ISpanTokeniser)new PatternTokeniser(@"\S+"),
                GenerateLoremIpsum(200),
                new Regex(@"\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };

        TextOutputPreflight.Compare(string.Concat("pattern/", Scenario), [_input],
            input => TextOutputPreflight.CaptureLean(sink => _tokeniser.Tokenise(input.AsSpan(), sink)),
            input =>
            {
                using var stream = new Lucene.Net.Analysis.Pattern.PatternTokenizer(
                    Lucene.Net.Util.AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY,
                    new System.IO.StringReader(input), _lucenePattern, 0);
                return TextOutputPreflight.CaptureLucene(stream);
            },
            PreflightAllowList.None);
    }

    private readonly ReadingTokenSink _readingSink = new();

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Tokenise_Reading()
    {
        _readingSink.Reset();
        _tokeniser.Tokenise(_input.AsSpan(), _readingSink);
        return _readingSink.Count;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Tokenise_Reading()
    {
        using var tokeniser = new Lucene.Net.Analysis.Pattern.PatternTokenizer(
            Lucene.Net.Util.AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY,
            new System.IO.StringReader(_input), _lucenePattern, 0);
        return LuceneTermReader.ReadAll(tokeniser);
    }

    // --- LeanCorpus benchmark ---

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Tokenise_CountOnly()
    {
        _sink.Reset();
        _tokeniser.Tokenise(_input.AsSpan(), _sink);
        return _sink.Count;
    }

    // --- Lucene.NET benchmark ---

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Tokenise_CountOnly()
    {
        using var tokeniser = new Lucene.Net.Analysis.Pattern.PatternTokenizer(
            Lucene.Net.Util.AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY,
            new System.IO.StringReader(_input),
            _lucenePattern,
            0); // group = 0 (entire match)

        int total = 0;
        tokeniser.Reset();
        while (tokeniser.IncrementToken())
            total++;
        tokeniser.End();
        return total;
    }

    private static string GenerateLoremIpsum(int wordCount)
    {
        string[] words =
        [
            "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing",
            "elit", "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore",
            "et", "dolore", "magna", "aliqua", "enim", "ad", "minim", "veniam",
            "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi",
            "aliquip", "ex", "ea", "commodo", "consequat", "duis", "aute",
            "irure", "in", "reprehenderit", "voluptate", "velit", "esse",
            "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
            "occaecat", "cupidatat", "non", "proident", "sunt", "culpa",
            "qui", "officia", "deserunt", "mollit", "anim", "id", "est", "laborum"
        ];

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < wordCount; i++)
        {
            if (i > 0)
                result.Append(' ');
            result.Append(words[i % words.Length]);
        }

        return result.ToString();
    }
}
