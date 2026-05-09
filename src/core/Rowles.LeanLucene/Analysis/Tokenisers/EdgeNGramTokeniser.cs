namespace Rowles.LeanLucene.Analysis.Tokenisers;

using Rowles.LeanLucene.Analysis;

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

    // Intern cache to reduce string allocations for repeated edge n-grams
    private readonly Dictionary<int, string> _internCache = new();
    private const int MaxInternCacheSize = 2048;

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
        var tokens = new List<Token>();
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
                    string text = GetOrCacheString(span);
                    tokens.Add(new Token(text, tokenStart, tokenStart + gramLen));
                }
            }
            tokenStart = i + 1;
        }
        return tokens;
    }

    private string GetOrCacheString(ReadOnlySpan<char> span)
    {
        int hash = string.GetHashCode(span, StringComparison.Ordinal);
        if (_internCache.TryGetValue(hash, out var cached) && cached.AsSpan().SequenceEqual(span))
            return cached;

        var text = span.ToString();
        if (_internCache.Count < MaxInternCacheSize)
            _internCache[hash] = text;
        return text;
    }
}
