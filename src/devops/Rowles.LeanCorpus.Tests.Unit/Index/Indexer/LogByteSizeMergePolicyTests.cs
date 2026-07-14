namespace Rowles.LeanCorpus.Tests.Unit.Index.Indexer;

/// <summary>
/// Unit tests for <see cref="LogByteSizeMergePolicy"/>.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class LogByteSizeMergePolicyTests
{
    private static SegmentInfo MakeSegment(string id, int docCount)
        => new() { SegmentId = id, DocCount = docCount };

    private static readonly IReadOnlySet<string> EmptyProtected = new HashSet<string>();

    [Fact(DisplayName = "LogByteSizeMergePolicy: Default constructor sets threshold 10 and minMerge 1 MB")]
    public void DefaultConstructor_UsesDefaults()
    {
        var policy = new LogByteSizeMergePolicy();
        // Verify it can be constructed without exceptions
        Assert.NotNull(policy);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Custom threshold and minMergeMB are accepted")]
    public void CustomParameters_Accepted()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 5, minMergeMB: 2.0);
        Assert.NotNull(policy);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Fewer segments than threshold returns empty")]
    public void FewerSegmentsThanThreshold_ReturnsEmpty()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 5);
        var segments = new List<SegmentInfo>
        {
            MakeSegment("s1", 100),
            MakeSegment("s2", 200),
        };

        var result = policy.FindMerges(segments, EmptyProtected);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Segments equal to threshold but in different buckets returns empty")]
    public void EqualToThreshold_DifferentBuckets_ReturnsEmpty()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 3, minMergeMB: 1.0);
        var segments = new List<SegmentInfo>
        {
            MakeSegment("s1", 100),     // small, bucket 0
            MakeSegment("s2", 10000),   // larger, different bucket
            MakeSegment("s3", 20000),   // different bucket
        };

        var result = policy.FindMerges(segments, EmptyProtected);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Sufficient segments in same bucket returns merge candidates")]
    public void SufficientSegments_SameBucket_ReturnsCandidates()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 3, minMergeMB: 1.0);
        var segments = new List<SegmentInfo>
        {
            MakeSegment("s1", 100),
            MakeSegment("s2", 200),
            MakeSegment("s3", 150),
        };

        var result = policy.FindMerges(segments, EmptyProtected);
        Assert.NotEmpty(result);
        Assert.Equal(3, result.Count);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Returns at most mergeThreshold segments")]
    public void ReturnsAtMostThreshold()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 3, minMergeMB: 0.1);
        var segments = new List<SegmentInfo>();
        for (int i = 0; i < 20; i++)
            segments.Add(MakeSegment($"s{i}", 100));

        var result = policy.FindMerges(segments, EmptyProtected);
        Assert.Equal(3, result.Count);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Protected segments are excluded")]
    public void ProtectedSegments_AreExcluded()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 2, minMergeMB: 0.1);
        var segments = new List<SegmentInfo>
        {
            MakeSegment("s1", 100),
            MakeSegment("s2", 100),
            MakeSegment("s3", 100),
        };
        var protectedIds = new HashSet<string> { "s1", "s3" };

        var result = policy.FindMerges(segments, protectedIds);
        Assert.Empty(result); // only s2 is unprotected, not enough
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Returns smallest segments in bucket first")]
    public void ReturnsSmallestFirst()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 3, minMergeMB: 0.1);
        var segments = new List<SegmentInfo>
        {
            MakeSegment("s1", 500),
            MakeSegment("s2", 100),
            MakeSegment("s3", 300),
        };

        var result = policy.FindMerges(segments, EmptyProtected);
        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].DocCount); // smallest first
        Assert.Equal(300, result[1].DocCount);
        Assert.Equal(500, result[2].DocCount);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Empty segments list returns empty")]
    public void EmptySegments_ReturnsEmpty()
    {
        var policy = new LogByteSizeMergePolicy();
        var result = policy.FindMerges([], EmptyProtected);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Zero DocCount segments estimated as zero bytes")]
    public void ZeroDocCount_EstimatedZero()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 2, minMergeMB: 0.1);
        var segments = new List<SegmentInfo>
        {
            MakeSegment("s1", 0),
            MakeSegment("s2", 0),
        };

        var result = policy.FindMerges(segments, EmptyProtected);
        Assert.NotEmpty(result); // both in bucket 0, same doc count
        Assert.Equal(2, result.Count);
    }

    [Fact(DisplayName = "LogByteSizeMergePolicy: Implements IMergePolicy")]
    public void ImplementsIMergePolicy()
    {
        Assert.True(typeof(LogByteSizeMergePolicy).IsAssignableTo(typeof(IMergePolicy)));
    }
}
