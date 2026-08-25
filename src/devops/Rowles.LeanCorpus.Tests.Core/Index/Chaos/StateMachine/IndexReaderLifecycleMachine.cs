using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexReaderLifecycleMachine : Machine<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>
{
    private const int MaxTraceLength = 30;

    public IndexReaderLifecycleMachine()
        : base(MaxTraceLength)
    {
    }

    public override Arbitrary<Setup<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>> Setup =>
        Arb.ToArbitrary(Gen.Fresh(static () =>
            (Setup<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>)new ReaderLifecycleSetup()));

    public override Gen<Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>> Next(
        IndexReaderLifecycleModel model)
    {
        var choices = new List<(int, Gen<Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>>)>
        {
            (18, Constant(new ReaderAddOperation(ModelDocument.Create(model.NextId)))),
            (10, Constant(new ReaderAddBatchOperation(CreateBatch(model.NextId)))),
            (18, Constant(new ReaderCommitOperation())),
            (14, Constant(new ReaderRefreshOperation()))
        };

        if (model.Working.Count > 0)
        {
            var deletes = model.Working.Keys
                .OrderBy(static id => id, StringComparer.Ordinal)
                .Select(static id => (Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>)new ReaderDeleteOperation(id))
                .ToArray();
            choices.Add((8, Gen.Elements(deletes)));
        }

        var updates = model.Working.Values
            .Where(document => model.History.Latest.Contains(document.Id))
            .OrderBy(static document => document.Id, StringComparer.Ordinal)
            .Select(static document => (Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>)new ReaderUpdateOperation(document.Replacement()))
            .ToArray();
        if (updates.Length > 0)
            choices.Add((8, Gen.Elements(updates)));

        choices.Add((12, Constant(new ReaderAcquireOperation(model.NextLeaseId))));

        if (model.Leases.Count > 0)
        {
            var searches = model.Leases.Keys
                .OrderBy(static id => id)
                .SelectMany(leaseId => SearchSpec.Cases.Select(search =>
                    (Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>)new ReaderSearchOperation(leaseId, search)))
                .ToArray();
            var releases = model.Leases.Keys
                .OrderBy(static id => id)
                .Select(static id => (Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>)new ReaderReleaseOperation(id))
                .ToArray();
            choices.Add((10, Gen.Elements(searches)));
            choices.Add((8, Gen.Elements(releases)));
        }

        return Gen.Frequency(choices.ToArray());
    }

    public override TearDown<IndexReaderLifecycleHarness> TearDown => new ReaderLifecycleTearDown();

    private static Gen<Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>> Constant(
        IndexReaderLifecycleOperation operation) =>
        Gen.Constant<Operation<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>>(operation);

    private static ModelDocument[] CreateBatch(int firstNumber)
    {
        int count = 1 + firstNumber % 3;
        return Enumerable.Range(firstNumber, count)
            .Select(ModelDocument.Create)
            .ToArray();
    }

    private sealed class ReaderLifecycleSetup : Setup<IndexReaderLifecycleHarness, IndexReaderLifecycleModel>
    {
        public override IndexReaderLifecycleModel Model() => IndexReaderLifecycleModel.Empty;

        public override IndexReaderLifecycleHarness Actual() => new();
    }

    private sealed class ReaderLifecycleTearDown : TearDown<IndexReaderLifecycleHarness>
    {
        public override void Actual(IndexReaderLifecycleHarness actual) => actual.Dispose();
    }
}
