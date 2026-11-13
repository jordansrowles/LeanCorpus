namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Trie-based synonym map supporting multi-token source phrases.
/// Used by <see cref="SynonymGraphFilter"/> for longest-match multi-token synonym expansion.
/// </summary>
public sealed class SynonymMap
{
    private readonly TrieNode _root = new();

    /// <summary>
    /// Adds a synonym mapping. Source may be a multi-word phrase (space-separated).
    /// Replacements are the synonym tokens to inject.
    /// </summary>
    public void Add(string source, string[] replacements)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacements);

        var words = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var node = _root;
        foreach (var w in words)
        {
            var key = w.ToLowerInvariant();
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new TrieNode();
                node.Children[key] = child;
            }
            node = child;
        }
        node.Replacements = replacements;
        if (words.Length > MaxSourceLength)
            MaxSourceLength = words.Length;
    }

    /// <summary>Gets the number of words in the longest source phrase added so far.</summary>
    internal int MaxSourceLength { get; private set; }

    /// <summary>Returns whether a token can begin a source phrase, without allocating.</summary>
    internal bool IsSourceStart(ReadOnlySpan<char> text) => TryGetChild(_root, text, out _);

    /// <summary>
    /// Attempts longest-match lookup from the first held token, comparing lowercased text
    /// without allocating.
    /// </summary>
    /// <returns>
    /// The number of source tokens consumed (0 if no match), and the replacement tokens via
    /// <paramref name="replacements"/>. <paramref name="canExtend"/> is true when every held
    /// token matched and a longer source could still match once more tokens arrive.
    /// </returns>
    internal int TryMatch(List<SynonymGraphFilter.PendingEdge> tokens, out string[]? replacements, out bool canExtend)
    {
        var node = _root;
        int bestLength = 0;
        string[]? bestReplacements = null;
        canExtend = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            if (!TryGetChild(node, tokens[i].Token.Text, out var child))
            {
                replacements = bestReplacements;
                return bestLength;
            }

            node = child!;
            if (node.Replacements is not null)
            {
                bestLength = i + 1;
                bestReplacements = node.Replacements;
            }
        }

        // Look-ahead never exceeds the longest source: past it no node has children.
        canExtend = node.Children.Count > 0 && tokens.Count < MaxSourceLength;
        replacements = bestReplacements;
        return bestLength;
    }

    private static bool TryGetChild(TrieNode node, ReadOnlySpan<char> text, out TrieNode? child)
    {
        if (node.Children.Count == 0)
        {
            child = null;
            return false;
        }

        // Same invariant lowercasing as string.ToLowerInvariant, into a stack buffer.
        Span<char> lowered = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        int length = text.ToLowerInvariant(lowered);
        return node.Children.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(lowered[..length], out child);
    }

    private sealed class TrieNode
    {
        public Dictionary<string, TrieNode> Children { get; } = new(StringComparer.Ordinal);
        public string[]? Replacements { get; set; }
    }
}
