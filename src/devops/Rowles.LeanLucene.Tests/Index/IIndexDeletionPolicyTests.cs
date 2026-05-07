using Rowles.LeanLucene.Index.Indexer;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Unit tests for <see cref="IIndexDeletionPolicy"/> verifying that the default
/// three-parameter overload delegates correctly to the two-parameter overload.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class IIndexDeletionPolicyTests
{
    private sealed class TrackingPolicy : IIndexDeletionPolicy
    {
        public string? LastDir;
        public int LastGen;

        public void OnCommit(string directoryPath, int currentGeneration)
        {
            LastDir = directoryPath;
            LastGen = currentGeneration;
        }
    }

    [Fact(DisplayName = "IIndexDeletionPolicy: Default Three-Arg Overload Delegates To Two-Arg")]
    public void DefaultThreeArgOverload_DelegatesToTwoArg()
    {
        var policy = new TrackingPolicy();
        IIndexDeletionPolicy iface = policy;

        iface.OnCommit("/some/dir", 3, new HashSet<string> { "seg_001" });

        Assert.Equal("/some/dir", policy.LastDir);
        Assert.Equal(3, policy.LastGen);
    }

    [Fact(DisplayName = "IIndexDeletionPolicy: Two-Arg Overload Called Directly")]
    public void TwoArgOverload_CalledDirectly()
    {
        var policy = new TrackingPolicy();
        policy.OnCommit("/idx", 7);
        Assert.Equal("/idx", policy.LastDir);
        Assert.Equal(7, policy.LastGen);
    }
}
