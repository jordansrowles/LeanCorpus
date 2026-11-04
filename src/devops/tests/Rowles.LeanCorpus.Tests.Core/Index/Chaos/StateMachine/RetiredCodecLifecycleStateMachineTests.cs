using System.Security.Cryptography;
using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal enum RetiredCodecAction
{
    Read,
    OpenWriter,
    Import,
    Merge,
    Migrate
}

internal sealed record RetiredCodecLifecycleModel(int Step = 0);

internal sealed class RetiredCodecLifecycleMachine : Machine<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>
{
    public RetiredCodecLifecycleMachine() : base(30) { }

    public override Arbitrary<Setup<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>> Setup =>
        Arb.ToArbitrary(Gen.Fresh(static () =>
            (Setup<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>)new RetiredCodecSetup()));

    public override Gen<Operation<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>> Next(
        RetiredCodecLifecycleModel model)
        => Gen.Elements(Enum.GetValues<RetiredCodecAction>())
            .Select(static action => (Operation<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>)
                new RetiredCodecOperation(action));

    public override TearDown<RetiredCodecLifecycleHarness> TearDown => new RetiredCodecTearDown();

    private sealed class RetiredCodecSetup : Setup<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>
    {
        public override RetiredCodecLifecycleModel Model() => new();
        public override RetiredCodecLifecycleHarness Actual() => new();
    }

    private sealed class RetiredCodecTearDown : TearDown<RetiredCodecLifecycleHarness>
    {
        public override void Actual(RetiredCodecLifecycleHarness actual) => actual.Dispose();
    }
}

internal sealed class RetiredCodecOperation(RetiredCodecAction action)
    : Operation<RetiredCodecLifecycleHarness, RetiredCodecLifecycleModel>
{
    public override RetiredCodecLifecycleModel Run(RetiredCodecLifecycleModel model)
        => model with { Step = model.Step + 1 };

    public override Property Check(RetiredCodecLifecycleHarness actual, RetiredCodecLifecycleModel model)
    {
        actual.Execute(action, model.Step);
        return Prop.ToProperty(true);
    }

    public override string ToString() => $"{action}()";
}

internal sealed class RetiredCodecLifecycleHarness : IDisposable
{
    private readonly StateMachineTestDirectory _owner = new();
    private readonly string _indexPath;
    private readonly Dictionary<string, string> _sourceSnapshot;
    private readonly ScoreSnapshot[] _initialSearch;

    internal RetiredCodecLifecycleHarness()
    {
        _indexPath = _owner.CreateChildPath("retired-index");
        HistoricalBbqIndexFixtures.CopyTo(_indexPath, "lifecycle-loose");
        _sourceSnapshot = Snapshot(_indexPath);
        _initialSearch = Search();
        Assert.NotEmpty(_initialSearch);
    }

    internal void Execute(RetiredCodecAction action, int step)
    {
        switch (action)
        {
            case RetiredCodecAction.Read:
                AssertReadable();
                break;
            case RetiredCodecAction.OpenWriter:
                RejectWriterOpen();
                break;
            case RetiredCodecAction.Import:
                RejectImport(step);
                break;
            case RetiredCodecAction.Merge:
                RejectMerge();
                break;
            case RetiredCodecAction.Migrate:
                RejectMigration();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown retired-index operation.");
        }

        AssertUnchanged();
    }

    private void AssertReadable()
    {
        using var directory = new MMapDirectory(_indexPath);
        var compatibility = IndexCompatibility.Check(directory);
        Assert.True(compatibility.CanRead);
        Assert.False(compatibility.CanWrite);
        Assert.False(compatibility.CanMigrate);
        Assert.Equal(IndexCompatibilityStatus.ReadOnlyRetired, compatibility.Status);
        Assert.Equal(_initialSearch, Search());
    }

    private void RejectWriterOpen()
    {
        using var directory = new MMapDirectory(_indexPath);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new IndexWriter(directory, new IndexWriterConfig()));
        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
    }

    private void RejectImport(int step)
    {
        string targetPath = _owner.CreateChildPath($"import-{step}");
        using var source = new MMapDirectory(_indexPath);
        using var target = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(target, new IndexWriterConfig
        {
            MaxBufferedDocs = 8,
            VectorQuantisation = VectorQuantisation.None,
        });
        var pending = new LeanDocument();
        pending.Add(new StringField("id", $"pending-{step}"));
        writer.AddDocument(pending);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => writer.AddIndexes(source));
        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(targetPath, "seg_*.seg"));
        writer.Commit();
        using var searcher = new IndexSearcher(target);
        Assert.Equal(1, searcher.Search(new TermQuery("id", $"pending-{step}"), 10).TotalHits);
    }

    private void RejectMerge()
    {
        using var directory = new MMapDirectory(_indexPath);
        var segments = IndexRecovery.RecoverLatestCommit(_indexPath, cleanupOrphans: false)!.SegmentInfos;
        int ordinal = 1000;
        var merger = new SegmentMerger(directory, mergeThreshold: 100, softDeleteRetentionSeconds: 0);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => merger.MergeAll(segments, ref ordinal));
        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1000, ordinal);
    }

    private void RejectMigration()
    {
        using var directory = new MMapDirectory(_indexPath);
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.ReadOnlyRetiredCodec);
        if (!string.IsNullOrWhiteSpace(result.StagingDirectory))
            Assert.False(Directory.Exists(result.StagingDirectory));
    }

    private ScoreSnapshot[] Search()
    {
        var query = new float[128];
        query[0] = 1f;
        using var searcher = new IndexSearcher(new MMapDirectory(_indexPath));
        return searcher.Search(new VectorQuery("embedding", query, topK: 5), 5)
            .ScoreDocs.Select(static hit => new ScoreSnapshot(hit.DocId, hit.Score)).ToArray();
    }

    private void AssertUnchanged() => Assert.Equal(_sourceSnapshot, Snapshot(_indexPath));

    private static Dictionary<string, string> Snapshot(string path)
        => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(static file => !Path.GetFileName(file).Equals("write.lock", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToDictionary(file => Path.GetRelativePath(path, file),
                static file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), StringComparer.Ordinal);

    public void Dispose() => _owner.Dispose();

    private readonly record struct ScoreSnapshot(int DocId, float Score);
}
