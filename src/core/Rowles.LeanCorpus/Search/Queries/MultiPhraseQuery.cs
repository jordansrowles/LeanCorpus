namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches an ordered phrase where each slot can contain one of several alternative terms.</summary>
public sealed class MultiPhraseQuery : Query
{
    private readonly string[][] _termGroups;
    private readonly int[] _positions;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the ordered term alternatives for each phrase slot.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TermGroups => _termGroups;

    /// <summary>Gets the explicit positions for each slot.</summary>
    public IReadOnlyList<int> Positions => _positions;

    /// <summary>Gets the allowed phrase slop.</summary>
    public int Slop { get; }

    /// <summary>Initialises a new <see cref="MultiPhraseQuery"/>.</summary>
    public MultiPhraseQuery(string field, IEnumerable<IEnumerable<string>> termGroups, int[]? positions = null, int slop = 0)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(termGroups);
        ArgumentOutOfRangeException.ThrowIfNegative(slop);

        Field = field;
        var groups = new List<string[]>();
        foreach (var group in termGroups)
        {
            ArgumentNullException.ThrowIfNull(group);
            var terms = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var term in group)
            {
                if (string.IsNullOrWhiteSpace(term))
                    throw new ArgumentException("Term group values must be non-empty.", nameof(termGroups));
                terms.Add(term);
            }

            if (terms.Count == 0)
                throw new ArgumentException("Term groups must contain at least one term.", nameof(termGroups));
            groups.Add(terms.ToArray());
        }

        _termGroups = groups.ToArray();
        if (_termGroups.Length == 0)
            throw new ArgumentException("MultiPhraseQuery requires at least one term group.", nameof(termGroups));

        if (positions is not null && positions.Length != _termGroups.Length)
            throw new ArgumentException("Positions must match the number of term groups.", nameof(positions));

        _positions = positions is not null
            ? positions.ToArray()
            : Enumerable.Range(0, _termGroups.Length).ToArray();

        Slop = slop;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not MultiPhraseQuery other ||
            !string.Equals(Field, other.Field, StringComparison.Ordinal) ||
            Slop != other.Slop ||
            Boost != other.Boost ||
            !_positions.AsSpan().SequenceEqual(other._positions) ||
            _termGroups.Length != other._termGroups.Length)
        {
            return false;
        }

        for (int i = 0; i < _termGroups.Length; i++)
        {
            if (!_termGroups[i].AsSpan().SequenceEqual(other._termGroups[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(MultiPhraseQuery));
        hash.Add(Field);
        hash.Add(Slop);
        foreach (var position in _positions)
            hash.Add(position);
        foreach (var group in _termGroups)
            foreach (var term in group)
                hash.Add(term);
        return CombineBoost(hash.ToHashCode());
    }
}
