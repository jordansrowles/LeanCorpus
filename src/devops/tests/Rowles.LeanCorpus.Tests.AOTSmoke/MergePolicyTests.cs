using Xunit;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public class MergePolicyTests
{
    [Fact]
    public void TieredMergePolicy_DefaultConstructor()
    {
        var policy = new TieredMergePolicy();
        Assert.NotNull(policy);
        Assert.True(policy is IMergePolicy);
    }

    [Fact]
    public void TieredMergePolicy_CustomThreshold()
    {
        var policy = new TieredMergePolicy(mergeThreshold: 5);
        Assert.NotNull(policy);
        Assert.True(policy is IMergePolicy);
    }

    [Fact]
    public void LogByteSizeMergePolicy_DefaultConstructor()
    {
        var policy = new LogByteSizeMergePolicy();
        Assert.NotNull(policy);
        Assert.True(policy is IMergePolicy);
    }

    [Fact]
    public void LogByteSizeMergePolicy_CustomParameters()
    {
        var policy = new LogByteSizeMergePolicy(mergeThreshold: 5, minMergeMB: 2.0);
        Assert.NotNull(policy);
        Assert.True(policy is IMergePolicy);
    }

    [Fact]
    public void NoMergePolicy_InstanceIsNonNull()
    {
        Assert.NotNull(NoMergePolicy.Instance);
        Assert.Same(NoMergePolicy.Instance, NoMergePolicy.Instance);
        Assert.True(NoMergePolicy.Instance is IMergePolicy);
    }

    [Fact]
    public void NoMergePolicy_FindMergesReturnsEmpty()
    {
        var result = NoMergePolicy.Instance.FindMerges([], new HashSet<string>());
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
