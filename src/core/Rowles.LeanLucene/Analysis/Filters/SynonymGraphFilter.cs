namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Token filter that supports multi-token synonym expansion using a trie-based
/// <see cref="SynonymMap"/>. Uses longest-match lookahead for multi-word synonyms
/// and inserts replacement tokens at the same position offsets.
/// </summary>
/// <remarks>
/// Replaces the simpler single-token synonym approach with trie-based longest-match.
/// </remarks>
public sealed class SynonymGraphFilter : ITokenFilter
{
    private readonly SynonymMap _map;

    /// <summary>
    /// Initialises a new <see cref="SynonymGraphFilter"/> with the specified synonym map.
    /// </summary>
    /// <param name="map">The synonym map used for multi-token expansion lookups.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="map"/> is <see langword="null"/>.</exception>
    public SynonymGraphFilter(SynonymMap map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        var result = new List<Token>(tokens.Count + 4);
        int i = 0;

        while (i < tokens.Count)
        {
            int matchLen = _map.TryMatch(tokens, i, out var replacements);

            if (matchLen > 0 && replacements is not null)
            {
                // Keep the original tokens at their positions
                for (int j = 0; j < matchLen; j++)
                    result.Add(tokens[i + j]);

                // Insert synonym tokens at the same position as the first source token
                int start = tokens[i].StartOffset;
                int end = tokens[i + matchLen - 1].EndOffset;
                foreach (var syn in replacements)
                    result.Add(new Token(syn, start, end));

                i += matchLen;
            }
            else
            {
                result.Add(tokens[i]);
                i++;
            }
        }

        tokens.Clear();
        tokens.AddRange(result);
    }
}
