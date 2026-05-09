using System.Collections.Frozen;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Removes common English stop words from a token list using a frozen set
/// for fast, allocation-free lookups.
/// </summary>
public sealed class StopWordFilter : ITokenFilter
{
    /// <summary>
    /// The classic 33-word English stop word list used by Lucene's <c>StandardAnalyzer</c>
    /// (equivalent to <c>StopAnalyzer.ENGLISH_STOP_WORDS_SET</c> in Lucene 4.x).
    /// This is the default list used by <c>StandardAnalyser</c> and
    /// aligns with Lucene.NET behaviour for maximum compatibility.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultStopWords =
    [
        "a", "an", "and", "are", "as", "at",
        "be", "but", "by",
        "for",
        "if", "in", "into", "is", "it",
        "no", "not",
        "of", "on", "or",
        "such",
        "that", "the", "their", "then", "there", "these", "they", "this", "to",
        "was", "will", "with"
    ];

    /// <summary>
    /// An extended English stop word list that removes a broader range of function words,
    /// prepositions, pronouns, modal verbs, and adverbs. This list is more aggressive than
    /// <see cref="DefaultStopWords"/> and will suppress indexing of terms such as
    /// <c>after</c>, <c>before</c>, <c>could</c>, <c>how</c>, <c>when</c>, etc.
    /// Use only when reduced index size matters more than full-text compatibility.
    /// Pass this to <c>IndexWriterConfig.StopWords</c> to opt in.
    /// </summary>
    public static readonly IReadOnlyList<string> ExtendedStopWords =
    [
        // Articles & determiners
        "a", "an", "another", "any", "each", "either",
        "every", "many", "more", "most", "much",
        "neither", "no", "other", "some",
        "that", "the", "these", "this", "those",
        "what", "which", "whose",

        // Conjunctions
        "although", "and", "as", "because", "both",
        "but", "if", "nor", "or", "since",
        "so", "than", "though", "unless", "until",
        "whether", "while", "yet",

        // Prepositions
        "about", "above", "across", "after", "against",
        "along", "among", "around", "at", "before",
        "behind", "below", "between", "beyond", "by",
        "down", "during", "for", "from", "in",
        "into", "near", "of", "off", "on",
        "onto", "out", "over", "per", "through",
        "to", "toward", "under", "until", "up",
        "upon", "via", "with", "within", "without",

        // Pronouns
        "he", "her", "hers", "herself", "him",
        "himself", "his", "i", "it", "its",
        "itself", "me", "my", "myself", "our",
        "ours", "ourselves", "she", "them", "their",
        "theirs", "themselves", "they", "us", "we",
        "who", "whom", "you", "your", "yours",
        "yourself", "yourselves",

        // "To be"
        "am", "are", "be", "been", "being", "is", "was", "were",

        // "To have"
        "had", "has", "have", "having",

        // "To do"
        "did", "do", "does", "doing",

        // Modals
        "can", "could", "may", "might", "must",
        "ought", "shall", "should", "will", "would",

        // Adverbs & high-frequency words
        "again", "all", "also", "both", "few",
        "further", "here", "however", "how", "just",
        "not", "now", "once", "only", "own",
        "re", "same", "so", "still", "then",
        "there", "too", "very", "when", "where",
        "why",

        // Negation fragments (post-tokenisation apostrophe stripping)
        "ain", "aren", "couldn", "didn", "doesn",
        "don", "hadn", "hasn", "haven", "isn",
        "ll", "mightn", "mustn", "needn", "shan",
        "shouldn", "t", "ve", "wasn", "weren",
        "won", "wouldn"
    ];

    private readonly FrozenSet<string> _stopWords;

    /// <summary>
    /// Initialises a new <see cref="StopWordFilter"/> using the default English stop word list.
    /// </summary>
    public StopWordFilter() : this(null) { }

    /// <summary>
    /// Initialises a new <see cref="StopWordFilter"/> with a custom stop word list.
    /// </summary>
    /// <param name="customStopWords">A custom set of stop words, or <see langword="null"/> to use <see cref="DefaultStopWords"/>.</param>
    public StopWordFilter(IEnumerable<string>? customStopWords)
    {
        _stopWords = (customStopWords ?? DefaultStopWords).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns true if the given term is a stop word.</summary>
    internal bool IsStopWord(string term) => _stopWords.Contains(term);

    /// <summary>Returns true if the given term span is a stop word (zero-alloc).</summary>
    internal bool IsStopWord(ReadOnlySpan<char> term)
        => _stopWords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(term);

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        tokens.RemoveAll(t => _stopWords.Contains(t.Text));
    }
}
