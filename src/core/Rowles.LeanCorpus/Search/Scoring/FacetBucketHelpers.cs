namespace Rowles.LeanCorpus.Search.Scoring;

internal static class FacetBucketHelpers
{
    public static IComparer<FacetBucket> GetComparer(FacetBucketOrder order)
    {
        if (!Enum.IsDefined(order))
            throw new ArgumentOutOfRangeException(nameof(order));

        return order switch
        {
            FacetBucketOrder.CountDescending => CountDescendingComparer.Instance,
            FacetBucketOrder.CountAscending => CountAscendingComparer.Instance,
            FacetBucketOrder.ValueAscending => ValueAscendingComparer.Instance,
            FacetBucketOrder.ValueDescending => ValueDescendingComparer.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
    }

    public static IReadOnlyList<FacetBucket> Page(IReadOnlyList<FacetBucket> buckets, int offset, int limit)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (limit == 0 || offset >= buckets.Count)
            return [];

        int count = Math.Min(limit, buckets.Count - offset);
        var page = new FacetBucket[count];
        for (int i = 0; i < count; i++)
            page[i] = buckets[offset + i];
        return page;
    }

    private static int CompareMissing(FacetBucket x, FacetBucket y)
        => x.IsMissing == y.IsMissing ? 0 : x.IsMissing ? 1 : -1;

    private sealed class CountDescendingComparer : IComparer<FacetBucket>
    {
        public static readonly CountDescendingComparer Instance = new();
        public int Compare(FacetBucket x, FacetBucket y)
            => x.Count != y.Count ? y.Count.CompareTo(x.Count) : CompareValueAscending(x, y);
    }

    private sealed class CountAscendingComparer : IComparer<FacetBucket>
    {
        public static readonly CountAscendingComparer Instance = new();
        public int Compare(FacetBucket x, FacetBucket y)
            => x.Count != y.Count ? x.Count.CompareTo(y.Count) : CompareValueAscending(x, y);
    }

    private sealed class ValueAscendingComparer : IComparer<FacetBucket>
    {
        public static readonly ValueAscendingComparer Instance = new();
        public int Compare(FacetBucket x, FacetBucket y)
            => CompareValueAscending(x, y) is var valueComparison && valueComparison != 0
                ? valueComparison
                : y.Count.CompareTo(x.Count);
    }

    private sealed class ValueDescendingComparer : IComparer<FacetBucket>
    {
        public static readonly ValueDescendingComparer Instance = new();
        public int Compare(FacetBucket x, FacetBucket y)
        {
            int missingComparison = CompareMissing(x, y);
            if (missingComparison != 0)
                return missingComparison;

            int valueComparison = StringComparer.Ordinal.Compare(y.Value, x.Value);
            return valueComparison != 0 ? valueComparison : y.Count.CompareTo(x.Count);
        }
    }

    private static int CompareValueAscending(FacetBucket x, FacetBucket y)
    {
        int missingComparison = CompareMissing(x, y);
        return missingComparison != 0 ? missingComparison : StringComparer.Ordinal.Compare(x.Value, y.Value);
    }
}
