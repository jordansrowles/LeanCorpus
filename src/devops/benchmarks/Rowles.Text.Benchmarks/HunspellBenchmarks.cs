using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis.Filters;
using LeanHunspellStemFilter = Rowles.LeanCorpus.Analysis.Filters.HunspellStemFilter;
using LuceneDictionary = Lucene.Net.Analysis.Hunspell.Dictionary;
using LuceneHunspellStemFilter = Lucene.Net.Analysis.Hunspell.HunspellStemFilter;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures Hunspell in three separate pairs (Sprint 4 PR 2):
/// <list type="bullet">
/// <item><b>Build:</b> LeanCorpus <c>Parse</c> with <c>useCache: false</c> against a Lucene.NET
/// <c>Dictionary</c>, so neither row is a cache hit.</item>
/// <item><b>Lookup:</b> stemming words with prebuilt dictionaries; expected stems are asserted in
/// setup. Lucene.NET's <c>Stemmer</c> is internal, so its lookup row reuses one
/// <c>KeywordTokenizer</c> and <c>HunspellStemFilter</c>, the closest public lookup path.</item>
/// <item><b>Pipeline:</b> <c>HunspellStemFilter</c> on both engines over the same words.</item>
/// </list>
/// <c>Dictionary</c> selects the small fixture or the real Debian <c>en_US</c> dictionary in
/// <c>lexicons/</c>. Directional head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class HunspellBenchmarks
{
    private const string FixtureAffix = """
SET UTF-8
PFX R Y 1
PFX R 0 re .
SFX D Y 2
SFX D 0 ed .
SFX D 0 ing .
SFX N Y 2
SFX N 0 ness .
SFX N 0 ment .
""";

    private const string FixtureDictionary = """
5
play/RD
work/RD
run/DN
jump/DN
walk/RD
""";

    /// <summary>The dictionary measured: the small fixture or Debian <c>en_US</c>.</summary>
    [Params("fixture", "en_US")]
    public string Dictionary { get; set; } = "fixture";

    private string _affixText = string.Empty;
    private string _dictionaryText = string.Empty;
    private byte[] _affixBytes = [];
    private byte[] _dictionaryBytes = [];
    private string[] _words = [];
    private HunspellDictionary _leanDictionary = null!;
    private LuceneDictionary _luceneDictionary = null!;
    private KeywordTokenizer _luceneLookupTokeniser = null!;
    private LuceneHunspellStemFilter _luceneLookupFilter = null!;
    private ICharTermAttribute _luceneLookupTerm = null!;
    private LeanHunspellStemFilter _leanFilter = null!;
    private CountingTokenSink _sink = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (Dictionary == "en_US")
        {
            string affixPath = TextLexicons.RequireAndRecord("hunspell-en_US-aff", "en_US.aff");
            string dictionaryPath = TextLexicons.RequireAndRecord("hunspell-en_US-dic", "en_US.dic");
            _affixText = File.ReadAllText(affixPath);
            _dictionaryText = File.ReadAllText(dictionaryPath);
            _words = ["walked", "running", "nations", "governments", "markets", "happier", "people", "presidents"];
        }
        else
        {
            _affixText = FixtureAffix;
            _dictionaryText = FixtureDictionary;
            _words = ["playing", "reworked", "running", "walking", "jumpness", "replay"];
        }

        _affixBytes = Encoding.UTF8.GetBytes(_affixText);
        _dictionaryBytes = Encoding.UTF8.GetBytes(_dictionaryText);

        long before = GC.GetTotalMemory(forceFullCollection: true);
        _leanDictionary = HunspellDictionary.Parse(_affixText, _dictionaryText, useCache: false);
        long leanRetained = GC.GetTotalMemory(forceFullCollection: true) - before;
        before = GC.GetTotalMemory(forceFullCollection: true);
        _luceneDictionary = BuildLuceneDictionary();
        long luceneRetained = GC.GetTotalMemory(forceFullCollection: true) - before;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark evidence hunspell {Dictionary}: retained LeanCorpus={leanRetained} B, Lucene.NET={luceneRetained} B"));

        ValidateUncachedBuild(LeanCorpus_Build(), LeanCorpus_Build());
        _luceneLookupTokeniser = new KeywordTokenizer(new StringReader(string.Empty));
        _luceneLookupFilter = new LuceneHunspellStemFilter(_luceneLookupTokeniser, _luceneDictionary, dedup: true, longestOnly: false);
        _luceneLookupTerm = _luceneLookupFilter.AddAttribute<ICharTermAttribute>();
        _leanFilter = new LeanHunspellStemFilter(_leanDictionary);
        _sink = new CountingTokenSink();
        ValidateLookups();
    }

    /// <summary>Fails setup if a build row returned a cached instance.</summary>
    internal static void ValidateUncachedBuild(HunspellDictionary first, HunspellDictionary second)
    {
        if (ReferenceEquals(first, second))
            throw new InvalidOperationException("The Hunspell build row returned a cached instance; it must parse every time.");
    }

    private void ValidateLookups()
    {
        foreach (string word in _words)
        {
            var lean = _leanDictionary.Stem(word).Order(StringComparer.Ordinal).ToArray();
            var lucene = LuceneStems(word).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (lean.Length == 0 || !lean.SequenceEqual(lucene, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Hunspell {Dictionary} lookup '{word}' differs: LeanCorpus [{string.Join(", ", lean)}], Lucene.NET [{string.Join(", ", lucene)}].");
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark preflight hunspell {Dictionary}: {_words.Length} lookups equal on both engines"));
    }

    private List<string> LuceneStems(string word)
    {
        var stems = new List<string>();
        _luceneLookupTokeniser.SetReader(new StringReader(word));
        _luceneLookupFilter.Reset();
        while (_luceneLookupFilter.IncrementToken())
            stems.Add(_luceneLookupTerm.ToString());
        _luceneLookupFilter.End();
        _luceneLookupFilter.Dispose();
        return stems;
    }

    [GlobalCleanup]
    public void Cleanup() => _luceneLookupFilter?.Dispose();

    private LuceneDictionary BuildLuceneDictionary()
    {
        using var affix = new MemoryStream(_affixBytes, writable: false);
        using var dictionary = new MemoryStream(_dictionaryBytes, writable: false);
        return new LuceneDictionary(affix, dictionary);
    }

    // --- Build ---

    [Benchmark(Description = "LeanCorpus build, uncached")]
    [BenchmarkCategory("build")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public HunspellDictionary LeanCorpus_Build()
        => HunspellDictionary.Parse(_affixText, _dictionaryText, useCache: false);

    [Benchmark(Description = "Lucene.NET build")]
    [BenchmarkCategory("build")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public LuceneDictionary LuceneNet_Build() => BuildLuceneDictionary();

    // --- Lookup ---

    [Benchmark(Description = "LeanCorpus lookup")]
    [BenchmarkCategory("lookup")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Lookup()
    {
        int count = 0;
        foreach (var word in _words)
            count += _leanDictionary.Stem(word).Count;
        return count;
    }

    [Benchmark(Description = "Lucene.NET lookup")]
    [BenchmarkCategory("lookup")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Lookup()
    {
        int count = 0;
        foreach (var word in _words)
        {
            _luceneLookupTokeniser.SetReader(new StringReader(word));
            _luceneLookupFilter.Reset();
            while (_luceneLookupFilter.IncrementToken())
                count++;
            _luceneLookupFilter.End();
            _luceneLookupFilter.Dispose();
        }
        return count;
    }

    // --- Pipeline ---

    [Benchmark(Description = "LeanCorpus HunspellStemFilter")]
    [BenchmarkCategory("pipeline")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Pipeline()
    {
        _sink.Reset();
        int offset = 0;
        foreach (var word in _words)
        {
            _leanFilter.Apply(word.AsSpan(), offset, offset + word.Length, Token.DefaultType, 1, null, _sink);
            offset += word.Length + 1;
        }

        return _sink.Count;
    }

    [Benchmark(Description = "Lucene.NET HunspellStemFilter")]
    [BenchmarkCategory("pipeline")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Pipeline()
    {
        int count = 0;
        foreach (var word in _words)
        {
            using var tokeniser = new KeywordTokenizer(new StringReader(word));
            using var filter = new LuceneHunspellStemFilter(tokeniser, _luceneDictionary, dedup: true, longestOnly: false);
            filter.Reset();
            while (filter.IncrementToken())
                count++;
            filter.End();
        }

        return count;
    }
}
