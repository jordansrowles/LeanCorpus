using System.Collections.Immutable;
using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed record TwoPhaseCommitModel(
    ImmutableDictionary<string, ModelDocument> Working,
    ImmutableDictionary<string, ModelDocument> Committed,
    ImmutableDictionary<string, ModelDocument>? Prepared,
    int NextId)
{
    public static TwoPhaseCommitModel Empty { get; } = new(EmptyDocuments(), EmptyDocuments(), null, 0);

    public bool HasPreparedCommit => Prepared is not null;

    public TwoPhaseCommitModel Add(ModelDocument document) => this with
    {
        Working = Working.SetItem(document.Id, document),
        NextId = NextId + 1
    };

    public TwoPhaseCommitModel Delete(string id) => this with { Working = Working.Remove(id) };

    public TwoPhaseCommitModel Prepare() => this with { Prepared = Working };

    public TwoPhaseCommitModel Commit() => this with { Committed = Working, Prepared = null };

    public TwoPhaseCommitModel Rollback() => this with { Working = Committed, Prepared = null };

    public TwoPhaseCommitModel Restart() => Prepared is null
        ? this with { Working = Committed }
        : this with { Working = Prepared, Committed = Prepared, Prepared = null };

    private static ImmutableDictionary<string, ModelDocument> EmptyDocuments() =>
        ImmutableDictionary.Create<string, ModelDocument>(StringComparer.Ordinal);
}

internal sealed class TwoPhaseCommitHarness : IDisposable
{
    private readonly StateMachineTestDirectory _testDirectory = new();
    private readonly string _indexPath;
    private MMapDirectory? _directory;
    private IndexWriter? _writer;

    public TwoPhaseCommitHarness()
    {
        _indexPath = _testDirectory.CreateChildPath("index");
        OpenWriter();
        Writer.Commit();
    }

    public void Add(ModelDocument document) => Writer.AddDocument(document.ToLeanDocument());

    public void Delete(string id) => Writer.DeleteDocuments(new TermQuery("id", id));

    public void Prepare() => Writer.PrepareCommit();

    public void Commit() => Writer.Commit();

    public void Rollback() => Writer.Rollback();

    public void Restart()
    {
        CloseWriter();
        OpenWriter();
    }

    public void AssertPrepared(bool expected) => Assert.Equal(expected, Writer.HasPreparedCommit);

    public void AssertCommitted(IReadOnlyDictionary<string, ModelDocument> expected)
    {
        using var directory = new MMapDirectory(_indexPath);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new MatchAllDocsQuery(), Math.Max(1, expected.Count + 1));
        Assert.Equal(expected.Count, results.TotalHits);
        var actualIds = results.ScoreDocs
            .Select(scoreDocument => searcher.GetStoredFields(scoreDocument.DocId)["id"][0])
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Keys.OrderBy(static id => id, StringComparer.Ordinal), actualIds);
    }

    public void Dispose()
    {
        CloseWriter();
        _testDirectory.Dispose();
    }

    private IndexWriter Writer => _writer ?? throw new ObjectDisposedException(nameof(TwoPhaseCommitHarness));

    private void OpenWriter()
    {
        _directory = new MMapDirectory(_indexPath);
        _writer = new IndexWriter(_directory, new IndexWriterConfig { MaxBufferedDocs = 3, MergePolicy = NoMergePolicy.Instance });
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
        _directory?.Dispose();
        _directory = null;
    }
}

internal abstract class TwoPhaseCommitOperation : Operation<TwoPhaseCommitHarness, TwoPhaseCommitModel>
{
    protected static Property Succeeds() => Prop.ToProperty(true);
}

internal sealed class TwoPhaseAddOperation(ModelDocument document) : TwoPhaseCommitOperation
{
    public override bool Pre(TwoPhaseCommitModel model) => !model.HasPreparedCommit;
    public override TwoPhaseCommitModel Run(TwoPhaseCommitModel model) => model.Add(document);
    public override Property Check(TwoPhaseCommitHarness actual, TwoPhaseCommitModel model) { actual.Add(document); return Succeeds(); }
    public override string ToString() => $"Add({document.Id})";
}

internal sealed class TwoPhaseDeleteOperation(string id) : TwoPhaseCommitOperation
{
    public override bool Pre(TwoPhaseCommitModel model) => !model.HasPreparedCommit && model.Working.ContainsKey(id);
    public override TwoPhaseCommitModel Run(TwoPhaseCommitModel model) => model.Delete(id);
    public override Property Check(TwoPhaseCommitHarness actual, TwoPhaseCommitModel model) { actual.Delete(id); return Succeeds(); }
    public override string ToString() => $"Delete({id})";
}

internal sealed class TwoPhasePrepareOperation : TwoPhaseCommitOperation
{
    public override bool Pre(TwoPhaseCommitModel model) => !model.HasPreparedCommit;
    public override TwoPhaseCommitModel Run(TwoPhaseCommitModel model) => model.Prepare();
    public override Property Check(TwoPhaseCommitHarness actual, TwoPhaseCommitModel model) { actual.Prepare(); actual.AssertPrepared(true); actual.AssertCommitted(model.Committed); return Succeeds(); }
    public override string ToString() => "PrepareCommit()";
}

internal sealed class TwoPhasePublishOperation : TwoPhaseCommitOperation
{
    public override TwoPhaseCommitModel Run(TwoPhaseCommitModel model) => model.Commit();
    public override Property Check(TwoPhaseCommitHarness actual, TwoPhaseCommitModel model) { actual.Commit(); actual.AssertPrepared(false); actual.AssertCommitted(model.Committed); return Succeeds(); }
    public override string ToString() => "Commit()";
}

internal sealed class TwoPhaseRollbackOperation : TwoPhaseCommitOperation
{
    public override bool Pre(TwoPhaseCommitModel model) => model.HasPreparedCommit;
    public override TwoPhaseCommitModel Run(TwoPhaseCommitModel model) => model.Rollback();
    public override Property Check(TwoPhaseCommitHarness actual, TwoPhaseCommitModel model) { actual.Rollback(); actual.AssertPrepared(false); actual.AssertCommitted(model.Committed); return Succeeds(); }
    public override string ToString() => "Rollback()";
}

internal sealed class TwoPhaseRestartOperation : TwoPhaseCommitOperation
{
    public override TwoPhaseCommitModel Run(TwoPhaseCommitModel model) => model.Restart();
    public override Property Check(TwoPhaseCommitHarness actual, TwoPhaseCommitModel model) { actual.Restart(); actual.AssertPrepared(false); actual.AssertCommitted(model.Committed); return Succeeds(); }
    public override string ToString() => "Restart()";
}

internal sealed class TwoPhaseCommitMachine : Machine<TwoPhaseCommitHarness, TwoPhaseCommitModel>
{
    public TwoPhaseCommitMachine() : base(30) { }

    public override Arbitrary<Setup<TwoPhaseCommitHarness, TwoPhaseCommitModel>> Setup =>
        Arb.ToArbitrary(Gen.Fresh(static () => (Setup<TwoPhaseCommitHarness, TwoPhaseCommitModel>)new TwoPhaseSetup()));

    public override Gen<Operation<TwoPhaseCommitHarness, TwoPhaseCommitModel>> Next(TwoPhaseCommitModel model)
    {
        var choices = new List<(int, Gen<Operation<TwoPhaseCommitHarness, TwoPhaseCommitModel>>)>
        {
            (15, Constant(new TwoPhasePublishOperation())),
            (10, Constant(new TwoPhaseRestartOperation()))
        };
        if (!model.HasPreparedCommit)
        {
            choices.Add((25, Constant(new TwoPhaseAddOperation(ModelDocument.Create(model.NextId)))));
            choices.Add((15, Constant(new TwoPhasePrepareOperation())));
            if (model.Working.Count > 0)
                choices.Add((15, Gen.Elements(model.Working.Keys.Select(id => (Operation<TwoPhaseCommitHarness, TwoPhaseCommitModel>)new TwoPhaseDeleteOperation(id)).ToArray())));
        }
        else
            choices.Add((20, Constant(new TwoPhaseRollbackOperation())));
        return Gen.Frequency(choices.ToArray());
    }

    public override TearDown<TwoPhaseCommitHarness> TearDown => new TwoPhaseTearDown();

    private static Gen<Operation<TwoPhaseCommitHarness, TwoPhaseCommitModel>> Constant(TwoPhaseCommitOperation operation) => Gen.Constant<Operation<TwoPhaseCommitHarness, TwoPhaseCommitModel>>(operation);
    private sealed class TwoPhaseSetup : Setup<TwoPhaseCommitHarness, TwoPhaseCommitModel> { public override TwoPhaseCommitModel Model() => TwoPhaseCommitModel.Empty; public override TwoPhaseCommitHarness Actual() => new(); }
    private sealed class TwoPhaseTearDown : TearDown<TwoPhaseCommitHarness> { public override void Actual(TwoPhaseCommitHarness actual) => actual.Dispose(); }
}
