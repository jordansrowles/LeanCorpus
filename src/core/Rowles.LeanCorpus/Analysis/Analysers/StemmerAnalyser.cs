using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Generic analyser that runs tokenise → lowercase → stop-word removal → stem
/// using the supplied <see cref="IStemmer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safety: the inner <see cref="Analyser"/> pipeline maintains per-instance
/// buffers. Create one instance per thread, or use the indexer's built-in per-thread
/// analyser cloning.
/// </para>
/// <para>
/// Convenience factory methods are provided for common stemmer configurations.
/// </para>
/// </remarks>
public sealed class StemmerAnalyser : IAnalyser
{
    private readonly Analyser _inner;

    /// <summary>
    /// Initialises a new <see cref="StemmerAnalyser"/> with the given stemmer.
    /// </summary>
    /// <param name="stemmer">The stemmer to apply after tokenisation, lowercasing, and stop-word removal.</param>
    public StemmerAnalyser(IStemmer stemmer)
    {
        ArgumentNullException.ThrowIfNull(stemmer);
        _inner = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new StopWordFilter(),
            new StemTokenFilter(stemmer));
    }

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input) => _inner.Analyse(input);

    /// <summary>
    /// Creates a <see cref="StemmerAnalyser"/> using the Porter stemmer.
    /// </summary>
    public static StemmerAnalyser Porter() => new(new PorterStemmer());

    /// <summary>
    /// Creates a <see cref="StemmerAnalyser"/> using the Krovetz stemmer with a file-based lexicon.
    /// </summary>
    /// <param name="lexiconPath">Path to the kstem-dict.txt lexicon file.</param>
    public static StemmerAnalyser KStem(string lexiconPath)
    {
        var lexicon = KStemLexicon.FromFile(lexiconPath);
        return new StemmerAnalyser(new KStemmer(lexicon));
    }

    /// <summary>
    /// Creates a <see cref="StemmerAnalyser"/> using the Krovetz stemmer with an in-memory lexicon.
    /// </summary>
    public static StemmerAnalyser KStem(IKStemLexicon lexicon) => new(new KStemmer(lexicon));

    /// <summary>
    /// Creates a <see cref="StemmerAnalyser"/> using the lightweight English stemmer.
    /// </summary>
    public static StemmerAnalyser LightEnglish() => new(new LightEnglishStemmer());

    /// <summary>
    /// Creates a <see cref="StemmerAnalyser"/> using a Hunspell dictionary.
    /// </summary>
    public static StemmerAnalyser Hunspell(HunspellDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        return new StemmerAnalyser(new HunspellStemmerAdapter(dictionary));
    }

    /// <summary>
    /// Creates a <see cref="StemmerAnalyser"/> that uses a named stemmer.
    /// Resolves the stemmer from the provided callback, useful with DI.
    /// </summary>
    public static StemmerAnalyser FromStemmer(IStemmer stemmer) => new(stemmer);
}

/// <summary>
/// Adapter that wraps <see cref="HunspellStemFilter"/>'s behaviour behind <see cref="IStemmer"/>.
/// Used to plug Hunspell into the <see cref="StemmerAnalyser"/> pipeline.
/// </summary>
internal sealed class HunspellStemmerAdapter : IStemmer
{
    private readonly HunspellDictionary _dictionary;

    public HunspellStemmerAdapter(HunspellDictionary dictionary)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    }

    public string Stem(string word)
    {
        var stems = _dictionary.Stem(word);
        return stems.Count > 0 ? stems[0] : word;
    }
}

/// <summary>
/// Adapter that exposes <see cref="PorterStemmerFilter.Stem"/> through <see cref="IStemmer"/>.
/// </summary>
public sealed class PorterStemmer : IStemmer
{
    /// <inheritdoc/>
    public string Stem(string word) => PorterStemmerFilter.Stem(word);
}
