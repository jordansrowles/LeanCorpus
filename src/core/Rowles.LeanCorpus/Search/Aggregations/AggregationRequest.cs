namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// Describes a numeric aggregation to compute alongside a search query.
/// </summary>
public sealed class AggregationRequest
{
    /// <summary>
    /// Initialises a new <see cref="AggregationRequest"/>.
    /// </summary>
    /// <param name="name">Caller-defined label identifying this aggregation in the results.</param>
    /// <param name="field">The numeric doc-values field to aggregate over.</param>
    /// <param name="type">The kind of aggregation to compute. Defaults to <see cref="AggregationType.Stats"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> or <paramref name="field"/> is null.</exception>
    public AggregationRequest(string name, string field, AggregationType type = AggregationType.Stats)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = ValidateName(name);
        Field = Document.Fields.FieldNameValidator.Validate(field, nameof(field));
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        Type = type;
    }

    /// <summary>Caller-defined label for this aggregation.</summary>
    public string Name { get; }

    /// <summary>Numeric doc-values field to aggregate over.</summary>
    public string Field { get; }

    /// <summary>The kind of aggregation to compute.</summary>
    public AggregationType Type { get; }

    /// <summary>Histogram bucket width (only used when <see cref="Type"/> is <see cref="AggregationType.Histogram"/>).</summary>
    public double HistogramInterval { get; init; } = 10.0;

    /// <summary>HyperLogLog++ precision from 4 to 18. Higher precision uses more memory.</summary>
    public int CardinalityPrecision { get; init; } = HyperLogLogPlusPlus.DefaultPrecision;

    /// <summary>Requested percentiles in the inclusive range 0 to 100.</summary>
    public IReadOnlyList<double> Percentiles { get; init; } = [50, 90, 95, 99];

    /// <summary>t-digest compression from 20 to 1,000. Higher values retain more centroids.</summary>
    public int TDigestCompression { get; init; } = TDigest.DefaultCompression;

    /// <summary>HDR highest trackable Int64 value.</summary>
    public long HdrHighestTrackableValue { get; init; } = 1_000_000;

    /// <summary>HDR significant decimal digits from 1 to 5.</summary>
    public int HdrSignificantDigits { get; init; } = 3;

    internal void Validate()
    {
        Document.Fields.FieldNameValidator.Validate(Field, nameof(Field));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Aggregation names must not be empty or whitespace.", nameof(Name));
        if (!Enum.IsDefined(Type))
            throw new ArgumentOutOfRangeException(nameof(Type));
        if (Type == AggregationType.Histogram
            && (!double.IsFinite(HistogramInterval) || HistogramInterval <= 0))
            throw new ArgumentOutOfRangeException(nameof(HistogramInterval), "Histogram interval must be finite and positive.");
        if (CardinalityPrecision is < 4 or > 18)
            throw new ArgumentOutOfRangeException(nameof(CardinalityPrecision));
        if (TDigestCompression is < 20 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(TDigestCompression));
        if (HdrHighestTrackableValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(HdrHighestTrackableValue));
        if (HdrSignificantDigits is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(HdrSignificantDigits));
        TDigestPercentilesAggregationState.ValidatePercentiles(Percentiles);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Aggregation names must not be empty or whitespace.", nameof(name));
        return name;
    }
}
