namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Exact ordered phrase match using positional data, with optional slop.
/// </summary>
public sealed class PhraseQuery : Query
{
    /// <summary>Maximum allowed positional deviation for phrase queries.</summary>
    public const int MaximumSlop = 256;

    private readonly int[] _positions;
    private int _slop;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the ordered terms that form the phrase.</summary>
    public string[] Terms { get; }

    /// <summary>Gets the explicit position for each term.</summary>
    public IReadOnlyList<int> Positions => _positions;

    /// <summary>Maximum number of positional gaps allowed between terms, from 0 through <see cref="MaximumSlop"/>. 0 = exact phrase.</summary>
    public int Slop
    {
        get => _slop;
        set
        {
            ValidateSlop(value, nameof(value));
            _slop = value;
        }
    }

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
                    cached[i] = QualifiedTermHelpers.BuildQualifiedTermString(Field, Terms[i]);
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
        _positions = CreateSequentialPositions(terms.Length);
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
        _positions = CreateSequentialPositions(terms.Length);
    }

    /// <summary>Initialises a phrase query with explicit term positions.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="terms">The ordered phrase terms.</param>
    /// <param name="positions">The non-decreasing position for each term.</param>
    /// <param name="slop">Maximum total positional deviation from the supplied positions.</param>
    public PhraseQuery(string field, string[] terms, int[] positions, int slop = 0)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(positions);
        if (terms.Length != positions.Length)
            throw new ArgumentException("Positions must match the number of terms.", nameof(positions));
        ValidateSlop(slop, nameof(slop));
        for (int i = 1; i < positions.Length; i++)
        {
            if (positions[i] < positions[i - 1])
                throw new ArgumentException("Positions must be non-decreasing.", nameof(positions));
        }

        Field = field;
        Terms = terms;
        _positions = positions.ToArray();
        Slop = slop;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PhraseQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Slop == other.Slop &&
        Boost == other.Boost &&
        _positions.AsSpan().SequenceEqual(other._positions) &&
        Terms.AsSpan().SequenceEqual(other.Terms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(PhraseQuery));
        h.Add(Field);
        h.Add(Slop);
        for (int i = 0; i < Terms.Length; i++)
        {
            h.Add(Terms[i]);
            h.Add(_positions[i]);
        }
        return CombineBoost(h.ToHashCode());
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.ConsumeTerms(this, Field, Terms);
    }

    private static int[] CreateSequentialPositions(int count)
    {
        var positions = new int[count];
        for (int i = 0; i < count; i++)
            positions[i] = i;
        return positions;
    }

    internal static void ValidateSlop(int slop, string parameterName)
    {
        if (slop is < 0 or > MaximumSlop)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                slop,
                $"Phrase slop must be between 0 and {MaximumSlop}.");
        }
    }
}
