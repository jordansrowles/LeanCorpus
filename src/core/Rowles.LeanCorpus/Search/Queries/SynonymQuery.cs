namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches any of several terms as one scoring unit.</summary>
public sealed class SynonymQuery : Query
{
    private readonly string[] _terms;
    private volatile string[]? _cachedQualifiedTerms;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the distinct, sorted synonym terms.</summary>
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
                    cached[i] = QualifiedTermHelpers.BuildQualifiedTermString(Field, _terms[i]);
                _cachedQualifiedTerms = cached;
            }
            return cached;
        }
    }

    /// <summary>Initialises a synonym query for one field.</summary>
    public SynonymQuery(string field, params string[] terms)
        : this(field, (IEnumerable<string>)terms)
    {
    }

    /// <summary>Initialises a synonym query for one field.</summary>
    public SynonymQuery(string field, IEnumerable<string> terms)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(terms);

        Field = field;
        var normalised = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
                throw new ArgumentException("Synonym terms must be non-empty.", nameof(terms));
            normalised.Add(term);
        }

        _terms = normalised.ToArray();
        if (_terms.Length == 0)
            throw new ArgumentException("SynonymQuery requires at least one term.", nameof(terms));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SynonymQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Boost == other.Boost &&
        _terms.AsSpan().SequenceEqual(other._terms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(SynonymQuery));
        hash.Add(Field);
        foreach (var term in _terms)
            hash.Add(term);
        return CombineBoost(hash.ToHashCode());
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.ConsumeTerms(this, Field, _terms);
    }
}
