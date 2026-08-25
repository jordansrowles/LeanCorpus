using FsCheck;
using FsCheck.Experimental;
using FsCheck.Xunit;
using FsCheckStateMachine = FsCheck.Experimental.StateMachine;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class IndexReaderLifecycleStateMachineTests
{
    [Property(
        DisplayName = "Index reader lifecycle preserves leased snapshots across refresh",
        MaxTest = 30,
        StartSize = 1,
        EndSize = 30,
        Parallelism = 1)]
    public Property Reader_lifecycle_operations_match_the_model() =>
        FsCheckStateMachine.ToProperty(new IndexReaderLifecycleMachine());

    [Fact(DisplayName = "Index reader lifecycle keeps old leases stable across refresh")]
    public void Old_leases_remain_stable_across_refresh()
    {
        using var harness = new IndexReaderLifecycleHarness();
        var model = IndexReaderLifecycleModel.Empty;
        var document = ModelDocument.Create(0);

        harness.Acquire(0);
        model = model.Acquire(0);
        harness.AssertSearch(0, new SearchSpec(SearchKind.MatchAll), model.Leases[0].Documents);

        harness.Add(document);
        model = model.Add(document);
        harness.Commit();
        model = model.Commit();

        harness.Acquire(1);
        model = model.Acquire(1);
        harness.AssertSearch(1, new SearchSpec(SearchKind.MatchAll), model.Leases[1].Documents);
        harness.AssertSearch(0, new SearchSpec(SearchKind.MatchAll), model.Leases[0].Documents);

        harness.Refresh();
        model = model.Refresh();
        harness.Acquire(2);
        model = model.Acquire(2);

        harness.AssertSearch(0, new SearchSpec(SearchKind.MatchAll), model.Leases[0].Documents);
        harness.AssertSearch(1, new SearchSpec(SearchKind.MatchAll), model.Leases[1].Documents);
        harness.AssertSearch(2, new SearchSpec(SearchKind.MatchAll), model.Leases[2].Documents);
        harness.AssertDiagnostics(model);

        harness.Release(0);
        model = model.Release(0);
        harness.Release(1);
        model = model.Release(1);
        harness.Release(2);
        model = model.Release(2);
        harness.AssertDiagnostics(model);
    }

    [Fact(DisplayName = "Index reader lifecycle refreshes generation without changing content")]
    public void Noop_commit_updates_new_lease_generation_without_changing_content()
    {
        using var harness = new IndexReaderLifecycleHarness();
        var model = IndexReaderLifecycleModel.Empty;

        harness.Acquire(0);
        model = model.Acquire(0);
        harness.Commit();
        model = model.Commit();
        harness.Refresh();
        model = model.Refresh();

        harness.Acquire(1);
        model = model.Acquire(1);

        Assert.Equal(2, model.Leases[1].Generation);
        harness.AssertLease(1, model.Leases[1]);
        harness.AssertLease(0, model.Leases[0]);
    }
}
