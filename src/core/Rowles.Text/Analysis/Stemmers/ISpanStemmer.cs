namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Span-based stemming contract. Implementations reduce a word to its root form
/// without allocating, by writing into a caller-provided buffer.
/// </summary>
public interface ISpanStemmer
{
    /// <summary>
    /// Stems <paramref name="word"/> into <paramref name="output"/>.
    /// Returns the length of the stemmed result, or -1 if <paramref name="output"/> is too small.
    /// The caller must ensure <c>output.Length >= word.Length</c> for guaranteed success.
    /// </summary>
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
