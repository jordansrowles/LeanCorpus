using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal abstract class IndexRecoveryOperation : Operation<IndexRecoveryHarness, IndexRecoveryModel>
{
    protected static Property Succeeds() => Prop.ToProperty(true);
}

internal sealed class RecoveryAddOperation(ModelDocument document) : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Add(document);

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.Add(document);
        return Succeeds();
    }

    public override string ToString() => $"Add({document.Id})";
}

internal sealed class RecoveryAddBatchOperation(IReadOnlyList<ModelDocument> documents) : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.AddBatch(documents);

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.AddBatch(documents);
        return Succeeds();
    }

    public override string ToString() => $"AddBatch([{string.Join(",", documents.Select(static document => document.Id))}])";
}

internal sealed class RecoveryDeleteOperation(string id) : IndexRecoveryOperation
{
    public override bool Pre(IndexRecoveryModel model) => model.Working.ContainsKey(id);

    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Delete(id);

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.Delete(id);
        return Succeeds();
    }

    public override string ToString() => $"Delete({id})";
}

internal sealed class RecoveryUpdateOperation(ModelDocument replacement) : IndexRecoveryOperation
{
    public override bool Pre(IndexRecoveryModel model) =>
        model.Working.ContainsKey(replacement.Id) && model.History.Latest.Contains(replacement.Id);

    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Update(replacement);

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.Update(replacement);
        return Succeeds();
    }

    public override string ToString() => $"Update({replacement.Id})";
}

internal sealed class RecoveryCommitOperation : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Commit();

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.Commit();
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "Commit()";
}

internal sealed class RecoveryRestartOperation : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Reopen();

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.ReopenWriter();
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "RestartWriter()";
}

internal sealed class RecoveryInspectOperation : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model;

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        var recovery = actual.InspectRecovery();
        Assert.Equal(model.History.Latest.Generation, recovery.Generation);
        Assert.Equal(model.InvalidLatestCommitPresent, recovery.WasFallback);
        return Succeeds();
    }

    public override string ToString() => "InspectRecovery()";
}

internal sealed class RecoverySearchOperation(SearchSpec search) : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model;

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.AssertSearch(search, model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => $"Search({search})";
}

internal sealed class RecoveryPendingCommitOperation : IndexRecoveryOperation
{
    public override bool Pre(IndexRecoveryModel model) => !model.InvalidLatestCommitPresent;

    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Commit();

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.PrepareCommitAndReopen();
        var recovery = actual.InspectRecovery();
        Assert.Equal(model.History.Latest.Generation, recovery.Generation);
        Assert.False(recovery.WasFallback);
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "PrepareCommitAndRestart()";
}

internal sealed class RecoveryTemporaryFilesOperation : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Reopen();

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.WriteTemporaryFilesAndReopen();
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "RecoverTemporaryFiles()";
}

internal sealed class RecoveryOrphanFilesOperation : IndexRecoveryOperation
{
    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Reopen();

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        actual.WriteOrphanFilesAndReopen();
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "RecoverOrphanFiles()";
}

internal sealed class RecoveryCorruptLatestOperation : IndexRecoveryOperation
{
    public override bool Pre(IndexRecoveryModel model) => model.History.Commits.Count > 1;

    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Fallback(invalidLatestCommitPresent: true);

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        var recovery = actual.CorruptLatestCommitAndReopen();
        Assert.Equal(model.History.Latest.Generation, recovery.Generation);
        Assert.True(recovery.WasFallback);
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "CorruptLatestCommitAndRecover()";
}

internal sealed class RecoveryDeleteLatestOperation : IndexRecoveryOperation
{
    public override bool Pre(IndexRecoveryModel model) => model.History.Commits.Count > 1;

    public override IndexRecoveryModel Run(IndexRecoveryModel model) => model.Fallback(invalidLatestCommitPresent: false);

    public override Property Check(IndexRecoveryHarness actual, IndexRecoveryModel model)
    {
        var recovery = actual.DeleteLatestCommitAndReopen();
        Assert.Equal(model.History.Latest.Generation, recovery.Generation);
        Assert.Equal(model.InvalidLatestCommitPresent, recovery.WasFallback);
        actual.AssertCommitted(model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "DeleteLatestCommitAndRecover()";
}
