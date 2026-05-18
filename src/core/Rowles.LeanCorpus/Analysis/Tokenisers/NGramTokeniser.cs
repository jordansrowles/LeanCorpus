namespace Rowles.LeanCorpus.Analysis.Tokenisers;

using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Splits text into all contiguous character substrings of length in [<see cref="MinGram"/>, <see cref="MaxGram"/>].
/// Useful for partial-word matching and CJK text.
/// 
/// Thread-safety: This class maintains an instance-level intern cache (_internCache) for performance.
/// Each instance should be used by a single thread, or callers should create separate instances per thread.
/// </summary>
public sealed class NGramTokeniser : ITokeniser
{
    /// <summary>
    /// Gets the minimum n-gram length (inclusive).
    /// </summary>
    public int MinGram { get; }

    /// <summary>
    /// Gets the maximum n-gram length (inclusive).
    /// </summary>
    public int MaxGram { get; }

    private const int MaxTextCacheSize = 65_536;
    private readonly TokenTextCache _textCache = new(MaxTextCacheSize);

    /// <summary>
    /// Initialises a new <see cref="NGramTokeniser"/> with the specified gram size range.
    /// </summary>
    /// <param name="minGram">The minimum gram length (must be ≥ 1).</param>
    /// <param name="maxGram">The maximum gram length (must be ≥ <paramref name="minGram"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minGram"/> is less than 1, or <paramref name="maxGram"/> is less than <paramref name="minGram"/>.
    /// </exception>
    public NGramTokeniser(int minGram, int maxGram)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minGram, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGram, minGram);
        MinGram = minGram;
        MaxGram = maxGram;
    }

    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>(CountNGrams(input.Length));
        Tokenise(input, tokens);
        return tokens;
    }

    /// <summary>
    /// Tokenises the input into the supplied destination list, clearing it before use.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="tokens">The destination token buffer to populate.</param>
    public void Tokenise(ReadOnlySpan<char> input, List<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        tokens.Clear();
        int tokenCount = CountNGrams(input.Length);
        if (tokens.Capacity < tokenCount)
            tokens.Capacity = tokenCount;

        int len = input.Length;
        for (int start = 0; start < len; start++)
        {
            for (int gramLen = MinGram; gramLen <= MaxGram && start + gramLen <= len; gramLen++)
            {
                var span = input.Slice(start, gramLen);
                tokens.Add(new Token(_textCache.GetOrAdd(span), start, start + gramLen));
            }
        }
    }

    private int CountNGrams(int length)
    {
        int count = 0;
        for (int gramLen = MinGram; gramLen <= MaxGram && gramLen <= length; gramLen++)
            count += length - gramLen + 1;
        return count;
    }
}
