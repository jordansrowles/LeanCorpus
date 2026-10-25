using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal abstract class IndexReaderLifecycleOperation : Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>
{
    protected static Property Succeeds() => Prop.ToProperty(true);
}

internal sealed class ReaderAddOperation(ModelDocument document) : IndexReaderLifecycleOperation
{
    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Add(document);

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Add(document);
        return Succeeds();
    }

    public override string ToString() => $"Add({document.Id})";
}

internal sealed class ReaderAddBatchOperation(IReadOnlyList<ModelDocument> documents) : IndexReaderLifecycleOperation
{
    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.AddBatch(documents);

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.AddBatch(documents);
        return Succeeds();
    }

    public override string ToString() => $"AddBatch([{string.Join(",", documents.Select(static document => document.Id))}])";
}

internal sealed class ReaderDeleteOperation(string id) : IndexReaderLifecycleOperation
{
    public override bool Pre(IndexReaderLifecycleModel model) => model.Working.ContainsKey(id);

    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Delete(id);

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Delete(id);
        return Succeeds();
    }

    public override string ToString() => $"Delete({id})";
}

internal sealed class ReaderUpdateOperation(ModelDocument replacement) : IndexReaderLifecycleOperation
{
    public override bool Pre(IndexReaderLifecycleModel model) =>
        model.Working.ContainsKey(replacement.Id) && model.History.Latest.Contains(replacement.Id);

    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Update(replacement);

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Update(replacement);
        return Succeeds();
    }

    public override string ToString() => $"Update({replacement.Id})";
}

internal sealed class ReaderCommitOperation : IndexReaderLifecycleOperation
{
    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Commit();

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Commit();
        actual.AssertCurrentGeneration(model.ManagerGeneration);
        return Succeeds();
    }

    public override string ToString() => "Commit()";
}

internal sealed class ReaderRefreshOperation : IndexReaderLifecycleOperation
{
    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Refresh();

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Refresh();
        actual.AssertCurrentGeneration(model.ManagerGeneration);
        return Succeeds();
    }

    public override string ToString() => "Refresh()";
}

internal sealed class ReaderAcquireOperation(int leaseId) : IndexReaderLifecycleOperation
{
    public override bool Pre(IndexReaderLifecycleModel model) => !model.Leases.ContainsKey(leaseId);

    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Acquire(leaseId);

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Acquire(leaseId);
        actual.AssertLease(leaseId, model.Leases[leaseId]);
        actual.AssertDiagnostics(model);
        return Succeeds();
    }

    public override string ToString() => $"Acquire({leaseId})";
}

internal sealed class ReaderSearchOperation(int leaseId, SearchSpec search) : IndexReaderLifecycleOperation
{
    public override bool Pre(IndexReaderLifecycleModel model) => model.Leases.ContainsKey(leaseId);

    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model;

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.AssertSearch(leaseId, search, model.Leases[leaseId].Documents);
        return Succeeds();
    }

    public override string ToString() => $"Search(lease={leaseId},{search})";
}

internal sealed class ReaderReleaseOperation(int leaseId) : IndexReaderLifecycleOperation
{
    public override bool Pre(IndexReaderLifecycleModel model) => model.Leases.ContainsKey(leaseId);

    public override IndexReaderLifecycleModel Run(IndexReaderLifecycleModel model) => model.Release(leaseId);

    public override Property Check(IndexReaderLifecycleHarness actual, IndexReaderLifecycleModel model)
    {
        actual.Release(leaseId);
        actual.AssertDiagnostics(model);
        return Succeeds();
    }

    public override string ToString() => $"Release({leaseId})";
}
