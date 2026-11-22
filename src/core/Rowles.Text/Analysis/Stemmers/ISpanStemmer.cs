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
    /// <para>A stem must be at most <c>8 * word.Length + 64</c> characters. <c>StemTokenFilter</c>
    /// retries with larger buffers up to that length and throws <see cref="InvalidOperationException"/>
    /// if the stemmer still returns -1, rather than emitting the word unstemmed. Every built-in stemmer
    /// stays well within the bound.</para>
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
