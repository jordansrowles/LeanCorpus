using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Exact weighted dot-product query over <see cref="SparseImpactField"/> values.</summary>
public sealed class SparseImpactQuery : Query
{
    private readonly SparseImpact[] _impacts;
    private readonly float _maximumImpact;

    /// <summary>Initialises a learned-sparse impact query.</summary>
    public SparseImpactQuery(string field, IEnumerable<SparseImpact> impacts)
    {
        Field = FieldNameValidator.Validate(field, nameof(field));
        ArgumentNullException.ThrowIfNull(impacts);
        _impacts = impacts.ToArray();
        if (_impacts.Length == 0)
            throw new ArgumentException("Sparse impact queries must contain at least one impact.", nameof(impacts));
        Array.Sort(_impacts, static (left, right) => StringComparer.Ordinal.Compare(left.Term, right.Term));
        for (int i = 0; i < _impacts.Length; i++)
        {
            if (string.IsNullOrEmpty(_impacts[i].Term) || !float.IsFinite(_impacts[i].Weight) || _impacts[i].Weight <= 0f)
                throw new ArgumentException("Sparse impacts require non-empty terms and positive finite weights.", nameof(impacts));
            if (i > 0 && _impacts[i - 1].Term == _impacts[i].Term)
                throw new ArgumentException("Sparse impact terms must be unique.", nameof(impacts));
        }
        for (int i = 0; i < _impacts.Length; i++)
            _maximumImpact = MathF.Max(_maximumImpact, _impacts[i].Weight);
    }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Sorted query impacts.</summary>
    public IReadOnlyList<SparseImpact> Impacts => _impacts;

    /// <summary>Largest positive query impact, used for safe impact upper bounds.</summary>
    internal float MaximumImpact => _maximumImpact;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SparseImpactQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Boost == other.Boost &&
        _impacts.AsSpan().SequenceEqual(other._impacts);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(SparseImpactQuery));
        hash.Add(Field);
        foreach (var impact in _impacts)
        {
            hash.Add(impact.Term, StringComparer.Ordinal);
            hash.Add(impact.Weight);
        }
        return CombineBoost(hash.ToHashCode());
    }
}
