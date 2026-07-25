using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Adapter that wraps <see cref="HunspellStemFilter"/>'s behaviour behind <see cref="ISpanStemmer"/>.
/// Used to plug Hunspell into the <see cref="StemmerAnalyser"/> pipeline.
/// </summary>
internal sealed class HunspellStemmerAdapter : ISpanStemmer
{
    private readonly HunspellDictionary _dictionary;

    public HunspellStemmerAdapter(HunspellDictionary dictionary)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    }

    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        string wordString = word.ToString();
        var stems = _dictionary.Stem(wordString);
        string result = stems.Count > 0 ? stems[0] : wordString;
        result.AsSpan().CopyTo(output);
        return result.Length;
    }

    public string Stem(string word)
    {
        var stems = _dictionary.Stem(word);
        return stems.Count > 0 ? stems[0] : word;
    }
}
