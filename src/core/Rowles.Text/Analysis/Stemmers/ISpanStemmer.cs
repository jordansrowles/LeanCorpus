namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Span-based stemming contract. Implementations reduce a word to its root form
/// without allocating, by writing into a caller-provided buffer.
/// </summary>
public interface ISpanStemmer
{
    /// <summary>
    /// Stems <paramref name="word"/> into <paramref name="output"/>.
    /// Returns the length of the stemmed result, or -1 if <paramref name="output"/> is too small,
    /// in which case nothing is written to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// A stem can be longer than its word: German expands <c>ß</c> to <c>ss</c>, so
    /// <c>output.Length >= word.Length</c> does not guarantee success. On -1, call again with a
    /// larger buffer.
    /// </remarks>
    int Stem(ReadOnlySpan<char> word, Span<char> output);
}

/// <summary>Marks a stemmer whose state is immutable and safe to share between analyser instances.</summary>
public interface IShareableSpanStemmer : ISpanStemmer
{
}

/// <summary>Creates independently owned stemmer state for a concurrent analyser instance.</summary>
public interface IThreadLocalSpanStemmer : ISpanStemmer
{
    /// <summary>Creates an independent stemmer instance.</summary>
    ISpanStemmer CreateThreadLocalStemmer();
}
