namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches documents containing any exact UTF-8 term in the supplied set.</summary>
public sealed class TermsQuery : Query
{
    private readonly byte[][] _terms;
    private readonly byte[][] _qualifiedTerms;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the distinct, byte-sorted UTF-8 terms.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Terms { get; }

    internal IReadOnlyList<byte[]> QualifiedTerms => _qualifiedTerms;

    /// <summary>Initialises a new <see cref="TermsQuery"/>.</summary>
    public TermsQuery(string field, params ReadOnlyMemory<byte>[] terms)
        : this(field, (IEnumerable<ReadOnlyMemory<byte>>)terms)
    {
    }

    /// <summary>Initialises a new <see cref="TermsQuery"/>.</summary>
    public TermsQuery(string field, IEnumerable<ReadOnlyMemory<byte>> terms)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(terms);

        Field = field;
        var values = new List<byte[]>();
        foreach (var term in terms)
        {
            if (term.IsEmpty)
                throw new ArgumentException("Term values must be non-empty.", nameof(terms));
            values.Add(term.ToArray());
        }

        if (values.Count == 0)
            throw new ArgumentException("TermsQuery requires at least one term.", nameof(terms));

        values.Sort(ByteArrayComparer.Instance);
        int distinctCount = 1;
        for (int i = 1; i < values.Count; i++)
        {
            if (!values[i].AsSpan().SequenceEqual(values[distinctCount - 1]))
                values[distinctCount++] = values[i];
        }
        if (distinctCount > TermInSetQuery.MaxTermCount)
            throw new ArgumentException(
                $"TermsQuery supports at most {TermInSetQuery.MaxTermCount} distinct terms.",
                nameof(terms));

        _terms = new byte[distinctCount][];
        values.CopyTo(0, _terms, 0, distinctCount);
        Terms = Array.ConvertAll(_terms, static term => (ReadOnlyMemory<byte>)term);

        byte[] fieldBytes = System.Text.Encoding.UTF8.GetBytes(field);
        _qualifiedTerms = new byte[_terms.Length][];
        for (int i = 0; i < _terms.Length; i++)
        {
            var qualified = new byte[fieldBytes.Length + 1 + _terms[i].Length];
            fieldBytes.CopyTo(qualified, 0);
            _terms[i].CopyTo(qualified, fieldBytes.Length + 1);
            _qualifiedTerms[i] = qualified;
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not TermsQuery other ||
            !string.Equals(Field, other.Field, StringComparison.Ordinal) ||
            Boost != other.Boost ||
            _terms.Length != other._terms.Length)
            return false;

        for (int i = 0; i < _terms.Length; i++)
        {
            if (!_terms[i].AsSpan().SequenceEqual(other._terms[i]))
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(TermsQuery));
        hash.Add(Field);
        foreach (var term in _terms)
            foreach (byte value in term)
                hash.Add(value);
        return CombineBoost(hash.ToHashCode());
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.VisitLeaf(this);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            return x.AsSpan().SequenceCompareTo(y);
        }
    }
}
