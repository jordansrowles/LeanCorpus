using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures Hunspell dictionary parsing and stemming throughput.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class HunspellBenchmarks
{
    private const string AffixText = """
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

    private const string DictionaryText = """
5
play/RD
work/RD
run/DN
jump/DN
walk/RD
""";

    private string[] _words = [];

    private HunspellDictionary _dictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dictionary = HunspellDictionary.Parse(AffixText, DictionaryText);
        _words = ["playing", "reworked", "running", "walking", "jumpless", "reread"];
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public HunspellDictionary Parse_Dictionary()
    {
        return HunspellDictionary.Parse(AffixText, DictionaryText);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Stem_Words()
    {
        int count = 0;
        foreach (var word in _words)
        {
            var stems = _dictionary.Stem(word);
            count += stems.Count;
        }
        return count;
    }
}
