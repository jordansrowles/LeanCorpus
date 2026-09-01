namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Compiled lookup plan for numeric range facets.</summary>
internal sealed class NumericRangeExecutionPlan
{
    private readonly NumericRange[] _ranges;
    private readonly Entry[] _byLowerBound;

    public NumericRangeExecutionPlan(IReadOnlyList<NumericRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        _ranges = ranges.ToArray();
        _byLowerBound = _ranges
            .Select((range, index) => new Entry(index, range))
            .OrderBy(static entry => entry.Range.LowerBound ?? double.NegativeInfinity)
            .ThenBy(static entry => entry.Range.IncludeLower ? 0 : 1)
            .ToArray();
        IsNonOverlapping = IsDisjoint(_byLowerBound);
    }

    public bool IsNonOverlapping { get; }

    public int Find(double value)
    {
        if (!IsNonOverlapping || double.IsNaN(value))
            return -1;

        int low = 0;
        int high = _byLowerBound.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            var range = _byLowerBound[middle].Range;
            if (range.LowerBound is null
                || value > range.LowerBound.Value
                || (value.Equals(range.LowerBound.Value) && range.IncludeLower))
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (candidate < 0)
            return -1;
        var selected = _byLowerBound[candidate];
        return selected.Range.Contains(value) ? selected.OriginalIndex : -1;
    }

    public int Find(long value)
    {
        if (!IsNonOverlapping)
            return -1;

        int low = 0;
        int high = _byLowerBound.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            var range = _byLowerBound[middle].Range;
            if (range.LowerBound is null
                || Compare(value, range.LowerBound.Value) > 0
                || (Compare(value, range.LowerBound.Value) == 0 && range.IncludeLower))
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (candidate < 0)
            return -1;
        var selected = _byLowerBound[candidate];
        return selected.Range.Contains(value) ? selected.OriginalIndex : -1;
    }

    public IEnumerable<int> FindOverlapping(double value)
    {
        for (int i = 0; i < _ranges.Length; i++)
            if (_ranges[i].Contains(value))
                yield return i;
    }

    public IEnumerable<int> FindOverlapping(long value)
    {
        for (int i = 0; i < _ranges.Length; i++)
            if (_ranges[i].Contains(value))
                yield return i;
    }

    private static bool IsDisjoint(IReadOnlyList<Entry> ranges)
    {
        for (int i = 1; i < ranges.Count; i++)
        {
            var previous = ranges[i - 1].Range;
            var current = ranges[i].Range;
            if (previous.UpperBound is null || current.LowerBound is null)
                return false;

            int comparison = previous.UpperBound.Value.CompareTo(current.LowerBound.Value);
            if (comparison > 0 || (comparison == 0 && previous.IncludeUpper && current.IncludeLower))
                return false;
        }
        return true;
    }

    private static int Compare(long value, double boundary)
    {
        if (boundary > 9_223_372_036_854_775_808d)
            return -1;
        if (boundary < -9_223_372_036_854_775_808d)
            return 1;
        return ((decimal)value).CompareTo((decimal)boundary);
    }

    private readonly record struct Entry(int OriginalIndex, NumericRange Range);
}

/// <summary>Compiled lookup plan for Int64 and encoded date ranges.</summary>
internal sealed class Int64RangeExecutionPlan
{
    private readonly Int64Range[] _ranges;
    private readonly Entry[] _byLowerBound;

    public Int64RangeExecutionPlan(IReadOnlyList<Int64Range> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        _ranges = ranges.ToArray();
        _byLowerBound = _ranges
            .Select((range, index) => new Entry(index, range))
            .OrderBy(static entry => entry.Range.LowerBound ?? long.MinValue)
            .ThenBy(static entry => entry.Range.IncludeLower ? 0 : 1)
            .ToArray();
        IsNonOverlapping = IsDisjoint(_byLowerBound);
    }

    public bool IsNonOverlapping { get; }

    public int Find(long value)
    {
        if (!IsNonOverlapping)
            return -1;

        int low = 0;
        int high = _byLowerBound.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            var range = _byLowerBound[middle].Range;
            if (range.LowerBound is null
                || value > range.LowerBound.Value
                || (value == range.LowerBound.Value && range.IncludeLower))
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (candidate < 0)
            return -1;
        var selected = _byLowerBound[candidate];
        return selected.Range.Contains(value) ? selected.OriginalIndex : -1;
    }

    public IEnumerable<int> FindOverlapping(long value)
    {
        for (int i = 0; i < _ranges.Length; i++)
            if (_ranges[i].Contains(value))
                yield return i;
    }

    private static bool IsDisjoint(IReadOnlyList<Entry> ranges)
    {
        for (int i = 1; i < ranges.Count; i++)
        {
            var previous = ranges[i - 1].Range;
            var current = ranges[i].Range;
            if (previous.UpperBound is null || current.LowerBound is null)
                return false;

            int comparison = previous.UpperBound.Value.CompareTo(current.LowerBound.Value);
            if (comparison > 0 || (comparison == 0 && previous.IncludeUpper && current.IncludeLower))
                return false;
        }
        return true;
    }

    private readonly record struct Entry(int OriginalIndex, Int64Range Range);
}
