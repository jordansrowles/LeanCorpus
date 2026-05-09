namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Exact ordered phrase match using positional data, with optional slop.
/// </summary>
public sealed class PhraseQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the ordered terms that form the phrase.</summary>
    public string[] Terms { get; }

    /// <summary>Maximum number of positional gaps allowed between terms. 0 = exact phrase.</summary>
    public int Slop { get; set; }

    /// <summary>Cached qualified term strings ("field\0term") to avoid per-search allocation.</summary>
    private volatile string[]? _cachedQualifiedTerms;

    /// <summary>Gets the qualified term strings (<c>"field\0term"</c>) for each phrase term, lazily computed.</summary>
    public string[] QualifiedTerms
    {
        get
        {
            var cached = _cachedQualifiedTerms;
            if (cached is null)
            {
                cached = new string[Terms.Length];
                for (int i = 0; i < Terms.Length; i++)
                    cached[i] = string.Concat(Field, "\x00", Terms[i]);
                _cachedQualifiedTerms = cached;
            }
            return cached;
        }
    }

    /// <summary>Initialises a new <see cref="PhraseQuery"/> with the specified field and terms.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="terms">The ordered terms that form the phrase.</param>
    public PhraseQuery(string field, params string[] terms)
    {
        Field = field;
        Terms = terms;
    }

    /// <summary>Initialises a new <see cref="PhraseQuery"/> with the specified field, slop, and terms.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="slop">Maximum allowed positional gaps between terms.</param>
    /// <param name="terms">The ordered terms that form the phrase.</param>
    public PhraseQuery(string field, int slop, params string[] terms)
    {
        Field = field;
        Slop = slop;
        Terms = terms;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PhraseQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Slop == other.Slop &&
        Boost == other.Boost &&
        Terms.AsSpan().SequenceEqual(other.Terms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(PhraseQuery));
        h.Add(Field);
        h.Add(Slop);
        foreach (var t in Terms) h.Add(t);
        return CombineBoost(h.ToHashCode());
    }
}
