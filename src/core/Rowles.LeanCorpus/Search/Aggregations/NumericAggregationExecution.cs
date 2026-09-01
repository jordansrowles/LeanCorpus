using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Internal state contract for numeric aggregation execution.</summary>
internal interface INumericAggregationState
{
    void Collect(in NumericDocumentValues values);
    void MergeFrom(INumericAggregationState other);
    AggregationResult Finish(CancellationToken cancellationToken = default);
}

/// <summary>Merge hook for future distributed approximate aggregation states.</summary>
internal interface IMergeableAggregationState<in TState>
    where TState : INumericAggregationState
{
    void MergeFrom(TState other);
}

internal static class NumericAggregationStateFactory
{
    public static INumericAggregationState Create(AggregationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return request.Type switch
        {
            AggregationType.Stats => new StatsAggregationState(request),
            AggregationType.Histogram => new HistogramAggregationState(request),
            AggregationType.Cardinality => new CardinalityAggregationState(request),
            AggregationType.TDigestPercentiles => new TDigestPercentilesAggregationState(request),
            AggregationType.HdrPercentiles => new HdrPercentilesAggregationState(request),
            _ => new EmptyAggregationState(request)
        };
    }
}

internal sealed class StatsAggregationState(AggregationRequest request)
    : INumericAggregationState, IMergeableAggregationState<StatsAggregationState>
{
    private readonly AggregationRequest _request = request;
    private long _count;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;
    private double _sum;

    public void Collect(in NumericDocumentValues values)
    {
        if (values.IsInt64)
        {
            if (values.Int64Values is not null)
            {
                foreach (long value in values.Int64Values)
                    Add(value);
            }
            else
            {
                Add(values.Int64Value);
            }
        }
        else if (values.DoubleValues is not null)
        {
            foreach (double value in values.DoubleValues)
                Add(value);
        }
        else
        {
            Add(values.DoubleValue);
        }
    }

    public AggregationResult Finish(CancellationToken cancellationToken = default)
        => new()
        {
            Name = _request.Name,
            Field = _request.Field,
            Count = _count,
            Min = _count > 0 ? _min : 0,
            Max = _count > 0 ? _max : 0,
            Sum = _sum
        };

    public void MergeFrom(StatsAggregationState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        AggregationMergeCompatibility.EnsureCompatible(_request, other._request);
        if (other._count == 0)
            return;

        _count += other._count;
        _sum += other._sum;
        if (double.IsNaN(_min) || double.IsNaN(other._min))
        {
            _min = double.NaN;
            _max = double.NaN;
            return;
        }
        _min = Math.Min(_min, other._min);
        _max = Math.Max(_max, other._max);
    }

    void INumericAggregationState.MergeFrom(INumericAggregationState other)
        => MergeFrom(other as StatsAggregationState
            ?? throw new ArgumentException("Aggregation states must have the same concrete type.", nameof(other)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Add(double value)
    {
        _count++;
        if (double.IsNaN(value))
        {
            _min = double.NaN;
            _max = double.NaN;
        }
        else if (!double.IsNaN(_min))
        {
            if (value < _min) _min = value;
            if (value > _max) _max = value;
        }
        _sum += value;
    }
}

internal sealed class HistogramAggregationState(AggregationRequest request)
    : INumericAggregationState, IMergeableAggregationState<HistogramAggregationState>
{
    private readonly AggregationRequest _request = request;
    private readonly double _interval = request.HistogramInterval <= 0 ? 10.0 : request.HistogramInterval;
    private readonly SortedDictionary<long, long> _bucketCounts = [];
    private long _count;
    private long _minimumBucketIndex = long.MaxValue;
    private long _maximumBucketIndex = long.MinValue;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;
    private double _sum;

    public void Collect(in NumericDocumentValues values)
    {
        if (values.IsInt64)
        {
            if (values.Int64Values is not null)
            {
                foreach (long value in values.Int64Values)
                    Add(value);
            }
            else
            {
                Add(values.Int64Value);
            }
        }
        else if (values.DoubleValues is not null)
        {
            foreach (double value in values.DoubleValues)
                Add(value);
        }
        else
        {
            Add(values.DoubleValue);
        }
    }

    public AggregationResult Finish(CancellationToken cancellationToken = default)
    {
        if (_count == 0)
            return AggregationResult.Empty(_request.Name, _request.Field);

        int bucketCount = checked((int)GetBucketSpan());
        var buckets = new HistogramBucket[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long index = checked(_minimumBucketIndex + i);
            double lower = index * _interval;
            _bucketCounts.TryGetValue(index, out long count);
            buckets[i] = new HistogramBucket(lower, lower + _interval, count);
        }

        return new AggregationResult
        {
            Name = _request.Name,
            Field = _request.Field,
            Count = _count,
            Min = _min,
            Max = _max,
            Sum = _sum,
            Buckets = buckets
        };
    }

    public void MergeFrom(HistogramAggregationState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        AggregationMergeCompatibility.EnsureCompatible(_request, other._request);
        foreach (var (bucketIndex, count) in other._bucketCounts)
        {
            _bucketCounts.TryGetValue(bucketIndex, out long existing);
            _bucketCounts[bucketIndex] = checked(existing + count);
        }
        _count = checked(_count + other._count);
        if (other._count > 0)
        {
            _minimumBucketIndex = Math.Min(_minimumBucketIndex, other._minimumBucketIndex);
            _maximumBucketIndex = Math.Max(_maximumBucketIndex, other._maximumBucketIndex);
            EnsureBucketSpanWithinLimit();
        }
        _min = Math.Min(_min, other._min);
        _max = Math.Max(_max, other._max);
        _sum += other._sum;
    }

    void INumericAggregationState.MergeFrom(INumericAggregationState other)
        => MergeFrom(other as HistogramAggregationState
            ?? throw new ArgumentException("Aggregation states must have the same concrete type.", nameof(other)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Add(double value)
    {
        if (!double.IsFinite(value))
            throw new InvalidOperationException($"Histogram aggregation for field '{_request.Field}' does not accept non-finite values.");

        double rawIndex = Math.Floor(value / _interval);
        if (rawIndex < long.MinValue || rawIndex > long.MaxValue)
            throw new InvalidOperationException($"Histogram aggregation for field '{_request.Field}' produced an out-of-range bucket index.");

        long bucketIndex = checked((long)rawIndex);
        _bucketCounts.TryGetValue(bucketIndex, out long existing);
        _bucketCounts[bucketIndex] = checked(existing + 1);
        _count = checked(_count + 1);
        _minimumBucketIndex = Math.Min(_minimumBucketIndex, bucketIndex);
        _maximumBucketIndex = Math.Max(_maximumBucketIndex, bucketIndex);
        EnsureBucketSpanWithinLimit();
        if (value < _min) _min = value;
        if (value > _max) _max = value;
        _sum += value;
    }

    private long GetBucketSpan()
        => checked(_maximumBucketIndex - _minimumBucketIndex + 1);

    private void EnsureBucketSpanWithinLimit()
    {
        long span = GetBucketSpan();
        if (span > NumericAggregator.MaxBucketCount)
        {
            throw new InvalidOperationException(
                $"Histogram aggregation for field '{_request.Field}' requires {span} buckets, which exceeds the configured maximum of {NumericAggregator.MaxBucketCount}.");
        }
    }
}

internal sealed class EmptyAggregationState(AggregationRequest request) : INumericAggregationState
{
    public void Collect(in NumericDocumentValues values) { }
    public void MergeFrom(INumericAggregationState other) { }
    public AggregationResult Finish(CancellationToken cancellationToken = default) => AggregationResult.Empty(request.Name, request.Field);
}

internal abstract class NumericAggregationStateBase(AggregationRequest request) : INumericAggregationState
{
    protected AggregationRequest Request { get; } = request;
    public void Collect(in NumericDocumentValues values)
    {
        if (values.IsInt64)
        {
            if (values.Int64Values is not null) foreach (long value in values.Int64Values) Collect(value);
            else Collect(values.Int64Value);
        }
        else if (values.DoubleValues is not null)
        {
            foreach (double value in values.DoubleValues) Collect(value);
        }
        else Collect(values.DoubleValue);
    }
    protected abstract void Collect(long value);
    protected abstract void Collect(double value);
    public abstract AggregationResult Finish(CancellationToken cancellationToken = default);
    public abstract void MergeFrom(INumericAggregationState other);
}

internal sealed class CardinalityAggregationState(AggregationRequest request) : NumericAggregationStateBase(request)
{
    private readonly HyperLogLogPlusPlus _sketch = new(request.CardinalityPrecision);
    protected override void Collect(long value) => _sketch.Add(value);
    protected override void Collect(double value) => _sketch.Add(value);
    public override AggregationResult Finish(CancellationToken cancellationToken = default) => new CardinalityAggregationResult
    {
        Name = Request.Name, Field = Request.Field, Algorithm = "hll++",
        EstimatedCardinality = _sketch.Estimate(), ExpectedRelativeError = _sketch.ExpectedRelativeError
    };
    public override void MergeFrom(INumericAggregationState other)
    {
        var typed = other as CardinalityAggregationState
            ?? throw new ArgumentException("Aggregation states must have the same concrete type.", nameof(other));
        AggregationMergeCompatibility.EnsureCompatible(Request, typed.Request);
        _sketch.MergeFrom(typed._sketch);
    }
}

internal sealed class TDigestPercentilesAggregationState(AggregationRequest request) : NumericAggregationStateBase(request)
{
    private readonly TDigest _digest = new(request.TDigestCompression);
    protected override void Collect(long value) => _digest.Add(value);
    protected override void Collect(double value) => _digest.Add(value);
    public override AggregationResult Finish(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = ValidatePercentiles(Request.Percentiles).Select(percentile => new PercentileValue(percentile, _digest.Quantile(percentile / 100d))).ToArray();
        return new PercentileAggregationResult { Name = Request.Name, Field = Request.Field, Count = (long)_digest.Count, Algorithm = "t-digest", Percentiles = values };
    }
    public override void MergeFrom(INumericAggregationState other)
    {
        var typed = other as TDigestPercentilesAggregationState
            ?? throw new ArgumentException("Aggregation states must have the same concrete type.", nameof(other));
        AggregationMergeCompatibility.EnsureCompatible(Request, typed.Request);
        _digest.MergeFrom(typed._digest);
    }
    internal static IReadOnlyList<double> ValidatePercentiles(IReadOnlyList<double> percentiles)
    {
        ArgumentNullException.ThrowIfNull(percentiles);
        if (percentiles.Count == 0) throw new ArgumentException("At least one percentile is required.", nameof(percentiles));
        foreach (double percentile in percentiles) if (percentile is < 0 or > 100 || double.IsNaN(percentile)) throw new ArgumentOutOfRangeException(nameof(percentiles));
        return percentiles;
    }
}

internal sealed class HdrPercentilesAggregationState(AggregationRequest request) : NumericAggregationStateBase(request)
{
    private readonly HdrHistogram _histogram = new(request.HdrHighestTrackableValue, request.HdrSignificantDigits);
    protected override void Collect(long value) => _histogram.RecordValue(value);
    protected override void Collect(double value) => throw new InvalidOperationException("HDR percentile aggregations require an Int64 field; use t-digest for doubles.");
    public override AggregationResult Finish(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = TDigestPercentilesAggregationState.ValidatePercentiles(Request.Percentiles).Select(percentile => new PercentileValue(percentile, _histogram.ValueAtPercentile(percentile))).ToArray();
        return new PercentileAggregationResult { Name = Request.Name, Field = Request.Field, Count = _histogram.TotalCount, Min = _histogram.Min, Max = _histogram.Max, Algorithm = "hdr-style-logarithmic", Percentiles = values };
    }
    public override void MergeFrom(INumericAggregationState other)
    {
        var typed = other as HdrPercentilesAggregationState
            ?? throw new ArgumentException("Aggregation states must have the same concrete type.", nameof(other));
        AggregationMergeCompatibility.EnsureCompatible(Request, typed.Request);
        _histogram.MergeFrom(typed._histogram);
    }
}

/// <summary>Validates configuration before partial aggregation states are merged.</summary>
internal static class AggregationMergeCompatibility
{
    public static void EnsureCompatible(AggregationRequest left, AggregationRequest right)
    {
        if (left.Type != right.Type
            || !string.Equals(left.Field, right.Field, StringComparison.Ordinal)
            || left.HistogramInterval != right.HistogramInterval
            || left.CardinalityPrecision != right.CardinalityPrecision
            || left.TDigestCompression != right.TDigestCompression
            || left.HdrHighestTrackableValue != right.HdrHighestTrackableValue
            || left.HdrSignificantDigits != right.HdrSignificantDigits
            || !left.Percentiles.SequenceEqual(right.Percentiles))
        {
            throw new ArgumentException("Aggregation states must have the same type, field and configuration to be merged.", nameof(right));
        }
    }
}
