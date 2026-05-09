namespace Rowles.LeanLucene.Analysis.Filters;

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
    }

    /// <summary>Attempts longest-match lookup starting at the given token position.</summary>
    /// <returns>
    /// The number of source tokens consumed (0 if no match),
    /// and the replacement tokens via <paramref name="replacements"/>.
    /// </returns>
    internal int TryMatch(List<Token> tokens, int startIndex, out string[]? replacements)
    {
        replacements = null;
        var node = _root;
        int bestLength = 0;
        string[]? bestReplacements = null;

        for (int i = startIndex; i < tokens.Count; i++)
        {
            var key = tokens[i].Text.ToLowerInvariant();
            if (!node.Children.TryGetValue(key, out var child))
                break;

            node = child;
            if (node.Replacements is not null)
            {
                bestLength = i - startIndex + 1;
                bestReplacements = node.Replacements;
            }
        }

        replacements = bestReplacements;
        return bestLength;
    }

    private sealed class TrieNode
    {
        public Dictionary<string, TrieNode> Children { get; } = new(StringComparer.Ordinal);
        public string[]? Replacements { get; set; }
    }
}
