namespace Rowles.LeanCorpus.Analysis.Tokenisers;

using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Splits text into character substrings of length [<see cref="MinGram"/>, <see cref="MaxGram"/>]
/// anchored at the start of each whitespace-delimited token (edge n-grams).
/// 
/// Thread-safety: This class maintains an instance-level intern cache (_internCache) for performance.
/// Each instance should be used by a single thread, or callers should create separate instances per thread.
/// </summary>
public sealed class EdgeNGramTokeniser : ITokeniser
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
    /// Initialises a new <see cref="EdgeNGramTokeniser"/> with the specified gram size range.
    /// </summary>
    /// <param name="minGram">The minimum gram length (must be ≥ 1).</param>
    /// <param name="maxGram">The maximum gram length (must be ≥ <paramref name="minGram"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minGram"/> is less than 1, or <paramref name="maxGram"/> is less than <paramref name="minGram"/>.
    /// </exception>
    public EdgeNGramTokeniser(int minGram, int maxGram)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minGram, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGram, minGram);
        MinGram = minGram;
        MaxGram = maxGram;
    }

    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>(CountEdgeNGrams(input));
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
        int tokenCount = CountEdgeNGrams(input);
        if (tokens.Capacity < tokenCount)
            tokens.Capacity = tokenCount;

        int len = input.Length;
        int tokenStart = 0;

        for (int i = 0; i <= len; i++)
        {
            bool boundary = i == len || input[i] == ' ' || input[i] == '\t'
                            || input[i] == '\r' || input[i] == '\n';
            if (!boundary) continue;

            int tokenEnd = i;
            int tokenLen = tokenEnd - tokenStart;
            if (tokenLen > 0)
            {
                for (int gramLen = MinGram; gramLen <= MaxGram && gramLen <= tokenLen; gramLen++)
                {
                    var span = input.Slice(tokenStart, gramLen);
                    tokens.Add(new Token(_textCache.GetOrAdd(span), tokenStart, tokenStart + gramLen));
                }
            }
            tokenStart = i + 1;
        }
    }

    private int CountEdgeNGrams(ReadOnlySpan<char> input)
    {
        int count = 0;
        int tokenStart = 0;

        for (int i = 0; i <= input.Length; i++)
        {
            bool boundary = i == input.Length || input[i] == ' ' || input[i] == '\t'
                            || input[i] == '\r' || input[i] == '\n';
            if (!boundary)
                continue;

            int tokenLen = i - tokenStart;
            if (tokenLen >= MinGram)
                count += Math.Min(MaxGram, tokenLen) - MinGram + 1;
            tokenStart = i + 1;
        }

        return count;
    }
}
