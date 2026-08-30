using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Internal state contract for numeric aggregation execution.</summary>
internal interface INumericAggregationState
{
    void Collect(in NumericDocumentValues values);
    AggregationResult Finish();
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
        => request.Type switch
        {
            AggregationType.Stats => new StatsAggregationState(request),
            AggregationType.Histogram => new HistogramAggregationState(request),
            _ => new EmptyAggregationState(request)
        };
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

    public AggregationResult Finish()
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
    private readonly List<double> _values = [];
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

    public AggregationResult Finish()
    {
        if (_values.Count == 0)
            return AggregationResult.Empty(_request.Name, _request.Field);

        double interval = _request.HistogramInterval <= 0 ? 10.0 : _request.HistogramInterval;
        double bucketStart = Math.Floor(_min / interval) * interval;
        double rawBuckets = (_max - bucketStart) / interval;
        int bucketCount = double.IsNaN(rawBuckets) || double.IsInfinity(rawBuckets) || rawBuckets > NumericAggregator.MaxBucketCount
            ? NumericAggregator.MaxBucketCount
            : Math.Max(1, (int)Math.Ceiling(rawBuckets) + 1);
        var bucketCounts = new long[bucketCount];
        foreach (double value in _values)
        {
            int index = (int)((value - bucketStart) / interval);
            bucketCounts[Math.Clamp(index, 0, bucketCount - 1)]++;
        }

        var buckets = new HistogramBucket[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            double lower = bucketStart + i * interval;
            buckets[i] = new HistogramBucket(lower, lower + interval, bucketCounts[i]);
        }

        return new AggregationResult
        {
            Name = _request.Name,
            Field = _request.Field,
            Count = _values.Count,
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
        _values.AddRange(other._values);
        _min = Math.Min(_min, other._min);
        _max = Math.Max(_max, other._max);
        _sum += other._sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Add(double value)
    {
        _values.Add(value);
        if (value < _min) _min = value;
        if (value > _max) _max = value;
        _sum += value;
    }
}

internal sealed class EmptyAggregationState(AggregationRequest request) : INumericAggregationState
{
    public void Collect(in NumericDocumentValues values) { }
    public AggregationResult Finish() => AggregationResult.Empty(request.Name, request.Field);
}

/// <summary>Validates configuration before partial aggregation states are merged.</summary>
internal static class AggregationMergeCompatibility
{
    public static void EnsureCompatible(AggregationRequest left, AggregationRequest right)
    {
        if (left.Type != right.Type
            || !string.Equals(left.Field, right.Field, StringComparison.Ordinal)
            || left.HistogramInterval != right.HistogramInterval)
        {
            throw new ArgumentException("Aggregation states must have the same type, field and configuration to be merged.", nameof(right));
        }
    }
}
