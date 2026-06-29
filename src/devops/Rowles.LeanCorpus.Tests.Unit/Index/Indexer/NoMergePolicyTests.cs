namespace Rowles.LeanCorpus.Tests.Unit.Index.Indexer;

/// <summary>
/// Unit tests for <see cref="NoMergePolicy"/>.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class NoMergePolicyTests
{
    [Fact(DisplayName = "NoMergePolicy: Instance is non-null singleton")]
    public void Instance_IsNonNullSingleton()
    {
        Assert.NotNull(NoMergePolicy.Instance);
        Assert.Same(NoMergePolicy.Instance, NoMergePolicy.Instance);
    }

    [Fact(DisplayName = "NoMergePolicy: FindMerges always returns empty")]
    public void FindMerges_AlwaysReturnsEmpty()
    {
        var segments = new List<SegmentInfo>
        {
            new() { SegmentId = "s1", DocCount = 100 },
            new() { SegmentId = "s2", DocCount = 200 },
            new() { SegmentId = "s3", DocCount = 300 },
        };
        var protectedIds = new HashSet<string> { "s1" };

        var result = NoMergePolicy.Instance.FindMerges(segments, protectedIds);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "NoMergePolicy: FindMerges with empty segments returns empty")]
    public void FindMerges_EmptySegments_ReturnsEmpty()
    {
        var result = NoMergePolicy.Instance.FindMerges([], new HashSet<string>());
        Assert.Empty(result);
    }

    [Fact(DisplayName = "NoMergePolicy: FindMerges with many segments returns empty")]
    public void FindMerges_ManySegments_ReturnsEmpty()
    {
        var segments = new List<SegmentInfo>();
        for (int i = 0; i < 100; i++)
            segments.Add(new SegmentInfo { SegmentId = $"s{i}", DocCount = i * 100 });

        var result = NoMergePolicy.Instance.FindMerges(segments, new HashSet<string>());
        Assert.Empty(result);
    }

    [Fact(DisplayName = "NoMergePolicy: Implements IMergePolicy")]
    public void ImplementsIMergePolicy()
    {
        Assert.True(typeof(NoMergePolicy).IsAssignableTo(typeof(IMergePolicy)));
    }
}
