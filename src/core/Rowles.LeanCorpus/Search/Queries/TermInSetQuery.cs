namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches documents containing any of the exact terms in the supplied set.</summary>
public sealed class TermInSetQuery : Query
{
    /// <summary>Maximum distinct terms accepted by a single query instance.</summary>
    public const int MaxTermCount = 65536;
    private readonly string[] _terms;
    private volatile string[]? _cachedQualifiedTerms;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the distinct, sorted term set.</summary>
    public IReadOnlyList<string> Terms => _terms;

    internal IReadOnlyList<string> QualifiedTerms
    {
        get
        {
            var cached = _cachedQualifiedTerms;
            if (cached is null)
            {
                cached = new string[_terms.Length];
                for (int i = 0; i < _terms.Length; i++)
                    cached[i] = string.Concat(Field, "\x00", _terms[i]);
                _cachedQualifiedTerms = cached;
            }

            return cached;
        }
    }

    /// <summary>Initialises a new <see cref="TermInSetQuery"/>.</summary>
    public TermInSetQuery(string field, params string[] terms)
        : this(field, (IEnumerable<string>)terms)
    {
    }

    /// <summary>Initialises a new <see cref="TermInSetQuery"/>.</summary>
    public TermInSetQuery(string field, IEnumerable<string> terms)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(terms);
        Field = field;

        var normalisedTerms = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
                throw new ArgumentException("Term values must be non-empty.", nameof(terms));
            normalisedTerms.Add(term);
        }

        _terms = normalisedTerms.ToArray();
        if (_terms.Length == 0)
            throw new ArgumentException("TermInSetQuery requires at least one term.", nameof(terms));
        if (_terms.Length > MaxTermCount)
            throw new ArgumentException($"TermInSetQuery supports at most {MaxTermCount} distinct terms.", nameof(terms));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TermInSetQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Boost == other.Boost &&
        _terms.AsSpan().SequenceEqual(other._terms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(TermInSetQuery));
        hash.Add(Field);
        foreach (var term in _terms)
            hash.Add(term);
        return CombineBoost(hash.ToHashCode());
    }
}
