using Rowles.LeanCorpus;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

/// <summary>Serialises tests that temporarily change process-wide defaults.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LeanCorpusDefaultsCollection
{
    public const string Name = "LeanCorpus defaults";
}

/// <summary>Verifies snapshot semantics for process-wide LeanCorpus defaults.</summary>
[Collection(LeanCorpusDefaultsCollection.Name)]
[Category(TestCategory.Unit)]
[Area(TestArea.Foundation)]
public sealed class LeanCorpusDefaultsTests
{
    [Fact(DisplayName = "LeanCorpus Defaults: Built-In Durable Commits Default Is True")]
    public void Reset_UsesBuiltInDurableCommitsDefault()
    {
        RunWithRestoredTestPolicy(() =>
        {
            LeanCorpusDefaults.Reset();
            Assert.True(new IndexWriterConfig().DurableCommits);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Global Override Applies To New Configurations")]
    public void Configure_AppliesOverrideToNewConfigurations()
    {
        RunWithRestoredTestPolicy(() =>
        {
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.DurableCommits = false);
            Assert.False(new IndexWriterConfig().DurableCommits);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Explicit Configuration Wins")]
    public void ExplicitConfiguration_OverridesGlobalDefault()
    {
        RunWithRestoredTestPolicy(() =>
        {
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.DurableCommits = false);
            Assert.True(new IndexWriterConfig { DurableCommits = true }.DurableCommits);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Existing Configuration Is Unchanged")]
    public void Configure_DoesNotMutateExistingConfiguration()
    {
        RunWithRestoredTestPolicy(() =>
        {
            LeanCorpusDefaults.Reset();
            var before = new IndexWriterConfig();
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.DurableCommits = false);
            var after = new IndexWriterConfig();

            Assert.True(before.DurableCommits);
            Assert.False(after.DurableCommits);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Reset Restores Built-In Default")]
    public void Reset_RestoresBuiltInDefault()
    {
        RunWithRestoredTestPolicy(() =>
        {
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.DurableCommits = false);
            LeanCorpusDefaults.Reset();
            Assert.True(new IndexWriterConfig().DurableCommits);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Concurrent Publication Produces Complete Snapshots")]
    public void Configure_ConcurrentPublicationProducesUsableSnapshots()
    {
        RunWithRestoredTestPolicy(() =>
        {
            Parallel.For(0, 256, i =>
            {
                LeanCorpusDefaults.Configure(options => options.IndexWriter.DurableCommits = i % 2 == 0);
                _ = new IndexWriterConfig().DurableCommits;
            });
        });
    }

    private static void RunWithRestoredTestPolicy(Action action)
    {
        var snapshot = LeanCorpusDefaults.CaptureSnapshotForTests();
        try
        {
            action();
        }
        finally
        {
            LeanCorpusDefaults.RestoreSnapshotForTests(snapshot);
        }
    }
}
