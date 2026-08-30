namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>A UTC date histogram bucket with lower-inclusive and upper-exclusive boundaries.</summary>
public sealed record DateHistogramBucket(DateTimeOffset Start, DateTimeOffset End, int Count)
{
    /// <summary>Gets the lower-inclusive start as UTC Unix milliseconds.</summary>
    public long StartUnixMilliseconds => Start.ToUnixTimeMilliseconds();

    /// <summary>Gets the upper-exclusive end as UTC Unix milliseconds.</summary>
    public long EndUnixMilliseconds => End.ToUnixTimeMilliseconds();
}
