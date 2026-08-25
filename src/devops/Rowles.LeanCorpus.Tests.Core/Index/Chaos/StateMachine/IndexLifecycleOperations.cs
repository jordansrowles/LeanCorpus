using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal abstract class IndexLifecycleOperation : Operation<IndexHarness, IndexModel>
{
    protected static Property Succeeds() => Prop.ToProperty(true);
}

internal sealed class AddOperation(ModelDocument document) : IndexLifecycleOperation
{
    public override IndexModel Run(IndexModel model) => model.Add(document);

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.Add(document);
        actual.AssertCommitted(model.Committed);
        return Succeeds();
    }

    public override string ToString() => $"Add({document.Id})";
}

internal sealed class AddBatchOperation(IReadOnlyList<ModelDocument> documents) : IndexLifecycleOperation
{
    public override IndexModel Run(IndexModel model) => model.AddBatch(documents);

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.AddBatch(documents);
        actual.AssertCommitted(model.Committed);
        return Succeeds();
    }

    public override string ToString() => $"AddBatch([{string.Join(",", documents.Select(static document => document.Id))}])";
}

internal sealed class DeleteOperation(string id) : IndexLifecycleOperation
{
    public override bool Pre(IndexModel model) => model.Working.ContainsKey(id);

    public override IndexModel Run(IndexModel model) => model.Delete(id);

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.Delete(id);
        actual.AssertCommitted(model.Committed);
        return Succeeds();
    }

    public override string ToString() => $"Delete({id})";
}

internal sealed class UpdateOperation(ModelDocument replacement) : IndexLifecycleOperation
{
    public override bool Pre(IndexModel model) =>
        model.Committed.ContainsKey(replacement.Id) && model.Working.ContainsKey(replacement.Id);

    public override IndexModel Run(IndexModel model) => model.Update(replacement);

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.Update(replacement);
        actual.AssertCommitted(model.Committed);
        return Succeeds();
    }

    public override string ToString() => $"Update({replacement.Id})";
}

internal sealed class CommitOperation : IndexLifecycleOperation
{
    public override IndexModel Run(IndexModel model) => model.Commit();

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.Commit();
        actual.AssertCommitted(model.Committed);
        return Succeeds();
    }

    public override string ToString() => "Commit()";
}

internal sealed class SearchOperation(SearchSpec search) : IndexLifecycleOperation
{
    public override IndexModel Run(IndexModel model) => model;

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.AssertSearch(search, model.Committed);
        return Succeeds();
    }

    public override string ToString() => $"Search({search})";
}

internal sealed class ReopenOperation : IndexLifecycleOperation
{
    public override IndexModel Run(IndexModel model) => model.Reopen();

    public override Property Check(IndexHarness actual, IndexModel model)
    {
        actual.Reopen();
        actual.AssertCommitted(model.Committed);
        return Succeeds();
    }

    public override string ToString() => "Reopen()";
}
