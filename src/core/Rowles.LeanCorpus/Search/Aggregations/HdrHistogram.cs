using System.Numerics;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// Mergeable HDR-style logarithmic histogram for non-negative Int64 values.
/// Values are mapped to power-of-two buckets and fixed-size sub-buckets, giving
/// bounded relative precision across orders of magnitude without fixed-width bins.
/// </summary>
public sealed class HdrHistogram
{
    private readonly SortedDictionary<long, long> _counts = [];
    private readonly long _subBucketMask;

    /// <summary>Initialises an HDR histogram with explicit range and significant digits.</summary>
    public HdrHistogram(long highestTrackableValue, int significantDigits = 3, long lowestDiscernibleValue = 1)
    {
        if (lowestDiscernibleValue <= 0) throw new ArgumentOutOfRangeException(nameof(lowestDiscernibleValue));
        if (highestTrackableValue < lowestDiscernibleValue) throw new ArgumentOutOfRangeException(nameof(highestTrackableValue));
        if (significantDigits is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(significantDigits), "HDR significant digits must be from 1 to 5.");
        LowestDiscernibleValue = lowestDiscernibleValue;
        HighestTrackableValue = highestTrackableValue;
        SignificantDigits = significantDigits;
        int subBucketCount = 1;
        while (subBucketCount < 2 * Math.Pow(10, significantDigits)) subBucketCount <<= 1;
        _subBucketMask = subBucketCount - 1;
    }

    /// <summary>Gets the lowest discernible value.</summary>
    public long LowestDiscernibleValue { get; }
    /// <summary>Gets the inclusive highest permitted value.</summary>
    public long HighestTrackableValue { get; }
    /// <summary>Gets configured significant decimal digits.</summary>
    public int SignificantDigits { get; }
    /// <summary>Gets total observations.</summary>
    public long TotalCount { get; private set; }
    /// <summary>Gets the observed minimum, or zero when empty.</summary>
    public long Min { get; private set; }
    /// <summary>Gets the observed maximum, or zero when empty.</summary>
    public long Max { get; private set; }

    /// <summary>Records a value, rejecting values outside the configured range.</summary>
    public void RecordValue(long value, long count = 1)
    {
        if (value < 0 || value > HighestTrackableValue) throw new ArgumentOutOfRangeException(nameof(value), "HDR values must be within the configured non-negative range.");
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        long equivalent = EquivalentValue(value);
        _counts[equivalent] = _counts.GetValueOrDefault(equivalent) + count;
        TotalCount += count;
        if (TotalCount == count || value < Min) Min = value;
        if (value > Max) Max = value;
    }

    /// <summary>Returns the value at a percentile in the inclusive range 0 to 100.</summary>
    public long ValueAtPercentile(double percentile)
    {
        if (percentile is < 0 or > 100 || double.IsNaN(percentile)) throw new ArgumentOutOfRangeException(nameof(percentile));
        if (TotalCount == 0) return 0;
        long target = Math.Max(1, (long)Math.Ceiling(percentile / 100d * TotalCount));
        long cumulative = 0;
        foreach (var bucket in _counts)
        {
            cumulative += bucket.Value;
            if (cumulative >= target) return bucket.Key;
        }
        return Max;
    }

    /// <summary>Enumerates logarithmic sub-bucket representative values and counts.</summary>
    public IEnumerable<(long Value, long Count)> EnumerateDistribution()
        => _counts.Select(static pair => (pair.Key, pair.Value));

    /// <summary>Merges a compatible histogram.</summary>
    public void MergeFrom(HdrHistogram other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (LowestDiscernibleValue != other.LowestDiscernibleValue || HighestTrackableValue != other.HighestTrackableValue || SignificantDigits != other.SignificantDigits)
            throw new ArgumentException("HDR histogram range and significant digits must match before merging.", nameof(other));
        foreach (var bucket in other._counts) _counts[bucket.Key] = _counts.GetValueOrDefault(bucket.Key) + bucket.Value;
        if (other.TotalCount == 0) return;
        if (TotalCount == 0 || other.Min < Min) Min = other.Min;
        if (other.Max > Max) Max = other.Max;
        TotalCount += other.TotalCount;
    }

    private long EquivalentValue(long value)
    {
        if (value < LowestDiscernibleValue) return value;
        int magnitude = BitOperations.Log2((ulong)value);
        int shift = Math.Max(0, magnitude - BitOperations.Log2((ulong)_subBucketMask));
        return (value >> shift) << shift;
    }
}
