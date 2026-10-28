using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexLifecycleMachine : Machine<IndexHarness, IndexModel>
{
    private const int MaxTraceLength = 30;

    private static readonly IReadOnlyList<Operation<IndexHarness, IndexModel>> SearchOperations =
        SearchSpec.Cases
            .Select(static search => (Operation<IndexHarness, IndexModel>)new SearchOperation(search))
            .ToArray();

    public IndexLifecycleMachine()
        : base(MaxTraceLength)
    {
    }

    public override Arbitrary<Setup<IndexHarness, IndexModel>> Setup =>
        Arb.ToArbitrary(Gen.Fresh(static () => (Setup<IndexHarness, IndexModel>)new IndexLifecycleSetup()));

    public override Gen<Operation<IndexHarness, IndexModel>> Next(IndexModel model)
    {
        var choices = new List<(int, Gen<Operation<IndexHarness, IndexModel>>)>
        {
            (25, Constant(new AddOperation(ModelDocument.Create(model.NextId)))),
            (15, Constant(new AddBatchOperation(CreateBatch(model.NextId)))),
            (15, Constant(new AddAsyncOperation(ModelDocument.Create(model.NextId)))),
            (10, Constant(new AddBatchAsyncOperation(CreateBatch(model.NextId)))),
            (15, Constant(new CommitOperation())),
            (20, Gen.Elements(SearchOperations.ToArray())),
            (5, Constant(new ReopenOperation()))
        };

        if (model.Working.Count > 0)
        {
            var deletes = model.Working.Keys
                .OrderBy(static id => id, StringComparer.Ordinal)
                .Select(static id => (Operation<IndexHarness, IndexModel>)new DeleteOperation(id))
                .ToArray();
            choices.Add((10, Gen.Elements(deletes)));
        }

        var updates = model.Working.Values
            .Where(document => model.Committed.ContainsKey(document.Id))
            .OrderBy(static document => document.Id, StringComparer.Ordinal)
            .Select(static document => (Operation<IndexHarness, IndexModel>)new UpdateOperation(document.Replacement()))
            .ToArray();
        if (updates.Length > 0)
            choices.Add((10, Gen.Elements(updates)));

        return Gen.Frequency(choices.ToArray());
    }

    public override TearDown<IndexHarness> TearDown => new IndexLifecycleTearDown();

    private static Gen<Operation<IndexHarness, IndexModel>> Constant(IndexLifecycleOperation operation) =>
        Gen.Constant<Operation<IndexHarness, IndexModel>>(operation);

    private static ModelDocument[] CreateBatch(int firstNumber)
    {
        int count = 1 + firstNumber % 3;
        return Enumerable.Range(firstNumber, count)
            .Select(ModelDocument.Create)
            .ToArray();
    }

    private sealed class IndexLifecycleSetup : Setup<IndexHarness, IndexModel>
    {
        public override IndexModel Model() => IndexModel.Empty;

        public override IndexHarness Actual() => new();
    }

    private sealed class IndexLifecycleTearDown : TearDown<IndexHarness>
    {
        public override void Actual(IndexHarness actual) => actual.Dispose();
    }
}
