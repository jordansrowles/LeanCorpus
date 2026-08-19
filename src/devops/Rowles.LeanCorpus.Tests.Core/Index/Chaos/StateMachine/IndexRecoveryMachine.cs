using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexRecoveryMachine : Machine<IndexRecoveryHarness, IndexRecoveryModel>
{
    private const int MaxTraceLength = 30;

    private static readonly IReadOnlyList<Operation<IndexRecoveryHarness, IndexRecoveryModel>> SearchOperations =
        SearchSpec.Cases
            .Select(static search => (Operation<IndexRecoveryHarness, IndexRecoveryModel>)new RecoverySearchOperation(search))
            .ToArray();

    public IndexRecoveryMachine()
        : base(MaxTraceLength)
    {
    }

    public override Arbitrary<Setup<IndexRecoveryHarness, IndexRecoveryModel>> Setup =>
        Arb.ToArbitrary(Gen.Fresh(static () =>
            (Setup<IndexRecoveryHarness, IndexRecoveryModel>)new RecoverySetup()));

    public override Gen<Operation<IndexRecoveryHarness, IndexRecoveryModel>> Next(IndexRecoveryModel model)
    {
        var choices = new List<(int, Gen<Operation<IndexRecoveryHarness, IndexRecoveryModel>>)>
        {
            (16, Constant(new RecoveryAddOperation(ModelDocument.Create(model.NextId)))),
            (10, Constant(new RecoveryAddBatchOperation(CreateBatch(model.NextId)))),
            (16, Constant(new RecoveryCommitOperation())),
            (10, Constant(new RecoveryRestartOperation())),
            (6, Constant(new RecoveryInspectOperation())),
            (10, Gen.Elements(SearchOperations.ToArray())),
            (5, Constant(new RecoveryPendingCommitOperation())),
            (5, Constant(new RecoveryTemporaryFilesOperation())),
            (5, Constant(new RecoveryOrphanFilesOperation()))
        };

        if (model.Working.Count > 0)
        {
            var deletes = model.Working.Keys
                .OrderBy(static id => id, StringComparer.Ordinal)
                .Select(static id => (Operation<IndexRecoveryHarness, IndexRecoveryModel>)new RecoveryDeleteOperation(id))
                .ToArray();
            choices.Add((8, Gen.Elements(deletes)));
        }

        var updates = model.Working.Values
            .Where(document => model.History.Latest.Contains(document.Id))
            .OrderBy(static document => document.Id, StringComparer.Ordinal)
            .Select(static document => (Operation<IndexRecoveryHarness, IndexRecoveryModel>)new RecoveryUpdateOperation(document.Replacement()))
            .ToArray();
        if (updates.Length > 0)
            choices.Add((8, Gen.Elements(updates)));

        if (model.History.Commits.Count > 1)
        {
            choices.Add((5, Constant(new RecoveryCorruptLatestOperation())));
            choices.Add((5, Constant(new RecoveryDeleteLatestOperation())));
        }

        return Gen.Frequency(choices.ToArray());
    }

    public override TearDown<IndexRecoveryHarness> TearDown => new RecoveryTearDown();

    private static Gen<Operation<IndexRecoveryHarness, IndexRecoveryModel>> Constant(
        IndexRecoveryOperation operation) =>
        Gen.Constant<Operation<IndexRecoveryHarness, IndexRecoveryModel>>(operation);

    private static ModelDocument[] CreateBatch(int firstNumber)
    {
        int count = 1 + firstNumber % 3;
        return Enumerable.Range(firstNumber, count)
            .Select(ModelDocument.Create)
            .ToArray();
    }

    private sealed class RecoverySetup : Setup<IndexRecoveryHarness, IndexRecoveryModel>
    {
        public override IndexRecoveryModel Model() => IndexRecoveryModel.Empty;

        public override IndexRecoveryHarness Actual() => new();
    }

    private sealed class RecoveryTearDown : TearDown<IndexRecoveryHarness>
    {
        public override void Actual(IndexRecoveryHarness actual) => actual.Dispose();
    }
}
