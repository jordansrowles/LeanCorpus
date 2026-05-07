namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Maps specific characters or strings to replacements using a lookup table.
/// Useful for normalising special characters (e.g., smart quotes → straight quotes).
/// </summary>
public sealed class MappingCharFilter : ICharFilter
{
    private readonly IReadOnlyDictionary<string, string> _mappings;

    /// <summary>
    /// Initialises a new <see cref="MappingCharFilter"/> with the specified character mappings.
    /// </summary>
    /// <param name="mappings">A dictionary mapping source strings to their replacement strings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mappings"/> is <see langword="null"/>.</exception>
    public MappingCharFilter(IReadOnlyDictionary<string, string> mappings)
    {
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
    }

    /// <inheritdoc/>
    public string Filter(ReadOnlySpan<char> input)
    {
        var text = input.ToString();
        foreach (var (from, to) in _mappings)
            text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }
}
