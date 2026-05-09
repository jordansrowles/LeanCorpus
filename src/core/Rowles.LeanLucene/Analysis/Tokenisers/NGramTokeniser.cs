namespace Rowles.LeanLucene.Analysis.Tokenisers;

using Rowles.LeanLucene.Analysis;

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

    // Intern cache to reduce string allocations for repeated n-grams
    private readonly Dictionary<int, string> _internCache = new();
    private const int MaxInternCacheSize = 2048;

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
        var tokens = new List<Token>();
        int len = input.Length;
        for (int start = 0; start < len; start++)
        {
            for (int gramLen = MinGram; gramLen <= MaxGram && start + gramLen <= len; gramLen++)
            {
                var span = input.Slice(start, gramLen);
                string text = GetOrCacheString(span);
                tokens.Add(new Token(text, start, start + gramLen));
            }
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
