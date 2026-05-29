namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches terms across several text fields using combined-field scoring semantics.</summary>
public sealed class CombinedFieldsQuery : Query
{
    private readonly string[] _fields;
    private readonly string[] _terms;
    private readonly KeyValuePair<string, float>[] _fieldWeights;

    /// <inheritdoc/>
    public override string Field => string.Empty;

    /// <summary>Gets the participating fields.</summary>
    public IReadOnlyList<string> Fields => _fields;

    /// <summary>Gets the distinct, sorted query terms.</summary>
    public IReadOnlyList<string> Terms => _terms;

    /// <summary>Gets the minimum number of terms that must match across the field set.</summary>
    public int MinimumShouldMatch { get; }

    /// <summary>Gets the optional per-field query weights.</summary>
    public IReadOnlyDictionary<string, float> FieldWeights => _fieldWeights.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Initialises a new <see cref="CombinedFieldsQuery"/>.</summary>
    public CombinedFieldsQuery(
        IEnumerable<string> fields,
        IEnumerable<string> terms,
        int minimumShouldMatch = 1,
        IReadOnlyDictionary<string, float>? fieldWeights = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumShouldMatch);

        _fields = NormaliseNonEmptyValues(fields, nameof(fields), "Field");
        _terms = NormaliseNonEmptyValues(terms, nameof(terms), "Term");

        MinimumShouldMatch = minimumShouldMatch == 0 && _terms.Length > 0 ? 1 : minimumShouldMatch;

        if (fieldWeights is null || _fields.Length == 0)
        {
            _fieldWeights = [];
            return;
        }

        var allowed = new HashSet<string>(_fields, StringComparer.Ordinal);
        var weights = new List<KeyValuePair<string, float>>();
        foreach (var pair in fieldWeights)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("Field weight keys must be non-empty field names.", nameof(fieldWeights));
            if (!allowed.Contains(pair.Key))
                throw new ArgumentException($"Field weight '{pair.Key}' does not match a participating field.", nameof(fieldWeights));
            if (!float.IsFinite(pair.Value) || pair.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(fieldWeights), "Field weights must be finite positive values.");

            weights.Add(pair);
        }

        _fieldWeights = weights
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] NormaliseNonEmptyValues(IEnumerable<string> values, string parameterName, string label)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{label} values must be non-empty.", parameterName);
            result.Add(value);
        }

        if (result.Count == 0)
            throw new ArgumentException($"{label} values must contain at least one value.", parameterName);

        return result.ToArray();
    }

    internal float GetFieldWeight(string field)
    {
        for (int i = 0; i < _fieldWeights.Length; i++)
        {
            if (string.Equals(_fieldWeights[i].Key, field, StringComparison.Ordinal))
                return _fieldWeights[i].Value;
        }

        return 1.0f;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not CombinedFieldsQuery other ||
            MinimumShouldMatch != other.MinimumShouldMatch ||
            Boost != other.Boost ||
            !_fields.AsSpan().SequenceEqual(other._fields) ||
            !_terms.AsSpan().SequenceEqual(other._terms) ||
            _fieldWeights.Length != other._fieldWeights.Length)
        {
            return false;
        }

        for (int i = 0; i < _fieldWeights.Length; i++)
        {
            if (!string.Equals(_fieldWeights[i].Key, other._fieldWeights[i].Key, StringComparison.Ordinal) ||
                _fieldWeights[i].Value != other._fieldWeights[i].Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(CombinedFieldsQuery));
        hash.Add(MinimumShouldMatch);
        foreach (var field in _fields)
            hash.Add(field);
        foreach (var term in _terms)
            hash.Add(term);
        foreach (var (field, weight) in _fieldWeights)
        {
            hash.Add(field);
            hash.Add(weight);
        }
        return CombineBoost(hash.ToHashCode());
    }
}
