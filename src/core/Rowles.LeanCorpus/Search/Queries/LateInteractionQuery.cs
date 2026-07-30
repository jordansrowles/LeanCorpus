using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Exact weighted MaxSim query over <see cref="MultiVectorField"/> token vectors.</summary>
public sealed class LateInteractionQuery : Query
{
    private readonly float[][] _queryVectors;
    private readonly float[] _weights;

    /// <summary>Initialises an exact weighted MaxSim query.</summary>
    public LateInteractionQuery(string field, IEnumerable<ReadOnlyMemory<float>> queryVectors, IEnumerable<float>? weights = null)
    {
        Field = FieldNameValidator.Validate(field, nameof(field));
        ArgumentNullException.ThrowIfNull(queryVectors);
        _queryVectors = queryVectors.Select(vector => vector.ToArray()).ToArray();
        if (_queryVectors.Length == 0)
            throw new ArgumentException("Late-interaction queries require at least one token vector.", nameof(queryVectors));
        int dimension = _queryVectors[0].Length;
        if (dimension == 0)
            throw new ArgumentException("Query token vectors must contain at least one dimension.", nameof(queryVectors));
        foreach (float[] vector in _queryVectors)
        {
            if (vector.Length != dimension)
                throw new ArgumentException("Query token vectors must have the same dimension.", nameof(queryVectors));
            if (vector.Any(value => !float.IsFinite(value)))
                throw new ArgumentException("Query token vectors must contain only finite values.", nameof(queryVectors));
        }
        _weights = weights?.ToArray() ?? Enumerable.Repeat(1f, _queryVectors.Length).ToArray();
        if (_weights.Length != _queryVectors.Length || _weights.Any(weight => !float.IsFinite(weight) || weight < 0f))
            throw new ArgumentException("Query token weights must be finite, non-negative, and match the token count.", nameof(weights));
    }

    /// <inheritdoc/>
    public override string Field { get; }
    /// <summary>Immutable query token vectors.</summary>
    public IReadOnlyList<float[]> QueryVectors => _queryVectors;
    /// <summary>Per-token MaxSim weights.</summary>
    public IReadOnlyList<float> Weights => _weights;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not LateInteractionQuery other ||
            Field != other.Field || Boost != other.Boost ||
            !_weights.AsSpan().SequenceEqual(other._weights) ||
            _queryVectors.Length != other._queryVectors.Length)
        {
            return false;
        }
        for (int i = 0; i < _queryVectors.Length; i++)
            if (!_queryVectors[i].AsSpan().SequenceEqual(other._queryVectors[i])) return false;
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(LateInteractionQuery));
        hash.Add(Field);
        foreach (float[] vector in _queryVectors)
            foreach (float value in vector) hash.Add(value);
        foreach (float weight in _weights) hash.Add(weight);
        return CombineBoost(hash.ToHashCode());
    }
}
