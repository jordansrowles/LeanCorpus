using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures the allocation and time difference when oversized query text is rejected at the input boundary.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QueryParserBenchmarks
{
    private const int OversizedInputCharacterCount = 65_537;

    private string _oversizedInput = string.Empty;
    private QueryParser _compatibilityParser = null!;
    private QueryParser _boundedParser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _oversizedInput = new string('a', OversizedInputCharacterCount);
        _compatibilityParser = new QueryParser("body", new KeywordAnalyser());
        _boundedParser = new QueryParser(
            "body",
            new KeywordAnalyser(),
            QueryParserOptions.Default with { MaxInputChars = 16_384 });
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query CompatibilityParserAcceptsLargeInput() => _compatibilityParser.Parse(_oversizedInput);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool BoundedParserRejectsOversizedInput()
    {
        try
        {
            _boundedParser.Parse(_oversizedInput);
            return false;
        }
        catch (QueryParseException)
        {
            return true;
        }
    }
}

/// <summary>Measures parsing time and allocations without including search execution.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QueryParserHotPathBenchmarks
{
    private QueryParser _parser = null!;
    private ComplexPhraseQueryParser _complexPhraseParser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new QueryParser("body", new StandardAnalyser());
        _complexPhraseParser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseSingleTerm() => _parser.Parse("quick");

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseBooleanQuery() => _parser.Parse("quick AND brown OR fox");

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseQuotedPhrase() => _parser.Parse("\"quick brown fox\"");

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Query ParseFlatComplexPhrase() =>
        _complexPhraseParser.Parse("\"quick (fast OR swift) brown\"");
}
