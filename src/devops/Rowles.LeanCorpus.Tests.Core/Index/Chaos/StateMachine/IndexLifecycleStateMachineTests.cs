using FsCheck;
using FsCheck.Experimental;
using FsCheck.Xunit;
using FsCheckStateMachine = FsCheck.Experimental.StateMachine;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class IndexLifecycleStateMachineTests
{
    [Property(
        DisplayName = "Index lifecycle state machine preserves Working and Committed state",
        MaxTest = 30,
        StartSize = 1,
        EndSize = 30,
        Parallelism = 1)]
    public Property Lifecycle_operations_match_the_model() =>
        FsCheckStateMachine.ToProperty(new IndexLifecycleMachine());

    [Fact(DisplayName = "Index lifecycle operations preserve commit visibility")]
    public void Lifecycle_operations_preserve_commit_visibility()
    {
        using var harness = new IndexHarness();
        var model = IndexModel.Empty;

        var first = ModelDocument.Create(0);
        var batch = new[] { ModelDocument.Create(1), ModelDocument.Create(2) };

        harness.Add(first);
        model = model.Add(first);
        harness.AssertCommitted(model.Committed);

        harness.AddBatch(batch);
        model = model.AddBatch(batch);
        harness.AssertCommitted(model.Committed);

        harness.Commit();
        model = model.Commit();
        harness.AssertCommitted(model.Committed);
        harness.AssertSearch(new SearchSpec(SearchKind.MatchAll), model.Committed);

        var replacement = model.Working[first.Id].Replacement();
        harness.Update(replacement);
        model = model.Update(replacement);
        harness.AssertCommitted(model.Committed);

        harness.Delete(batch[1].Id);
        model = model.Delete(batch[1].Id);
        harness.AssertCommitted(model.Committed);

        var uncommitted = ModelDocument.Create(model.NextId);
        harness.Add(uncommitted);
        model = model.Add(uncommitted);
        harness.AssertSearch(new SearchSpec(SearchKind.MatchAll), model.Committed);

        harness.Reopen();
        model = model.Reopen();
        harness.AssertCommitted(model.Committed);
        harness.AssertSearch(new SearchSpec(SearchKind.BodyTerm, "replacement"), model.Committed);

        harness.Delete(batch[1].Id);
        model = model.Delete(batch[1].Id);
        var secondReplacement = model.Working[batch[0].Id].Replacement();
        harness.Update(secondReplacement);
        model = model.Update(secondReplacement);
        harness.Commit();
        model = model.Commit();

        harness.AssertCommitted(model.Committed);
        harness.AssertSearch(new SearchSpec(SearchKind.Category, "updated"), model.Committed);
        harness.AssertSearch(new SearchSpec(SearchKind.PriceRange, Min: 0, Max: 100), model.Committed);
    }

    [Fact(DisplayName = "Index lifecycle model keeps Working separate from Committed")]
    public void Model_keeps_working_separate_from_committed()
    {
        var document = ModelDocument.Create(0);
        var model = IndexModel.Empty.Add(document);

        Assert.Single(model.Working);
        Assert.Empty(model.Committed);

        model = model.Commit();
        var replacement = document.Replacement();
        model = model.Update(replacement).Delete(document.Id);

        Assert.Empty(model.Working);
        Assert.Empty(model.Committed);

        model = model.Reopen();
        Assert.Equal(model.Committed, model.Working);
    }

    [Fact(DisplayName = "Index lifecycle update preserves pending deletions after flushing")]
    public void Update_preserves_pending_deletions_after_flushing()
    {
        using var harness = new IndexHarness();
        var model = IndexModel.Empty;
        var existing = ModelDocument.Create(0);
        var pending = ModelDocument.Create(1);

        harness.Add(existing);
        model = model.Add(existing);
        harness.Commit();
        model = model.Commit();

        harness.Add(pending);
        model = model.Add(pending);
        harness.Delete(pending.Id);
        model = model.Delete(pending.Id);

        var replacement = model.Working[existing.Id].Replacement();
        harness.Update(replacement);
        model = model.Update(replacement);
        harness.Commit();
        model = model.Commit();

        harness.AssertCommitted(model.Committed);
        harness.AssertSearch(new SearchSpec(SearchKind.BodyTerm, "replacement"), model.Committed);
    }

    [Fact(DisplayName = "Index lifecycle query update preserves pending deletions after flushing")]
    public void Query_update_preserves_pending_deletions_after_flushing()
    {
        using var harness = new IndexHarness();
        var model = IndexModel.Empty;
        var existing = ModelDocument.Create(0);
        var pending = ModelDocument.Create(1);

        harness.Add(existing);
        model = model.Add(existing);
        harness.Commit();
        model = model.Commit();

        harness.Add(pending);
        model = model.Add(pending);
        harness.Delete(pending.Id);
        model = model.Delete(pending.Id);

        var replacement = model.Working[existing.Id].Replacement();
        harness.UpdateByQuery(replacement);
        model = model.Update(replacement);
        harness.Commit();
        model = model.Commit();

        harness.AssertCommitted(model.Committed);
        harness.AssertSearch(new SearchSpec(SearchKind.BodyTerm, "replacement"), model.Committed);
    }
}
