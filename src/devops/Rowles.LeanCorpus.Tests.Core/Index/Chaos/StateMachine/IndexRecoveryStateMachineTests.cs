using FsCheck;
using FsCheck.Experimental;
using FsCheck.Xunit;
using FsCheckStateMachine = FsCheck.Experimental.StateMachine;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class IndexRecoveryStateMachineTests
{
    [Property(
        DisplayName = "Index recovery preserves the latest valid committed state",
        MaxTest = 30,
        StartSize = 1,
        EndSize = 30,
        Parallelism = 1)]
    public Property Recovery_operations_match_the_model() =>
        FsCheckStateMachine.ToProperty(new IndexRecoveryMachine());

    [Fact(DisplayName = "Index recovery falls back after the newest commit is corrupted")]
    public void Recovery_falls_back_to_previous_valid_commit()
    {
        using var harness = new IndexRecoveryHarness();
        var model = IndexRecoveryModel.Empty;
        var first = ModelDocument.Create(0);
        var second = ModelDocument.Create(1);

        harness.Add(first);
        model = model.Add(first);
        harness.Commit();
        model = model.Commit();

        harness.Add(second);
        model = model.Add(second);
        harness.Commit();
        model = model.Commit();

        var fallback = model.Fallback(invalidLatestCommitPresent: true);
        var recovery = harness.CorruptLatestCommitAndReopen();

        Assert.Equal(fallback.History.Latest.Generation, recovery.Generation);
        Assert.True(recovery.WasFallback);
        harness.AssertCommitted(fallback.History.Latest.Documents);
    }

    [Fact(DisplayName = "Index recovery promotes a prepared pending commit")]
    public void Recovery_promotes_prepared_commit_after_writer_restart()
    {
        using var harness = new IndexRecoveryHarness();
        var model = IndexRecoveryModel.Empty;
        var document = ModelDocument.Create(0);

        harness.Add(document);
        model = model.Add(document);
        harness.PrepareCommitAndReopen();
        model = model.Commit();

        var recovery = harness.InspectRecovery();
        Assert.Equal(model.History.Latest.Generation, recovery.Generation);
        Assert.False(recovery.WasFallback);
        harness.AssertCommitted(model.History.Latest.Documents);
    }

    [Fact(DisplayName = "Index recovery refuses an index when every commit is corrupt")]
    public void Recovery_refuses_index_with_no_valid_commit()
    {
        using var harness = new IndexRecoveryHarness();
        harness.AssertAllCommitsInvalid();
    }
}
