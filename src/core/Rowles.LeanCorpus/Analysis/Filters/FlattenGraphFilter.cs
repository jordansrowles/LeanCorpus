namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Normalises token position increments so same-position alternates remain explicit and
/// the stream stays consumable by LeanCorpus's linear postings model.
/// </summary>
public sealed class FlattenGraphFilter : ITokenFilter
{
    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        if (tokens.Count == 0)
            return;

        int absolutePosition = -1;
        int previousPosition = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            int increment = tokens[i].PositionIncrement > 0 ? tokens[i].PositionIncrement : 0;
            if (absolutePosition < 0 && increment == 0)
                increment = 1;

            absolutePosition += increment;
            int normalisedIncrement = previousPosition < 0 ? 1 : absolutePosition - previousPosition;
            tokens[i] = tokens[i].WithPositionIncrement(normalisedIncrement);
            previousPosition = absolutePosition;
        }
    }
}
