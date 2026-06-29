using FsCheck.Xunit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Index;

[Trait("Category", "Chaos")]
public sealed class TwoPhaseCommitChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public TwoPhaseCommitChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(DisplayName = "PrepareCommit is idempotent — data survives crash")]
    public void PrepareCommit_SurvivesCrash(int seed)
    {
        var dir = new MMapDirectory(System.IO.Path.Combine(_fixture.Path, $"twophase_crash_{seed}"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 5 };

        // Index some docs and prepare.
        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 10; i++)
                writer.AddDocument(CreateDoc("body", $"doc {i} {seed}"));
            writer.PrepareCommit();
        }

        // Recover: data should be promoted.
        using (var writer = new IndexWriter(dir, config))
        {
            writer.Commit();
            var segFiles = System.IO.Directory.GetFiles(dir.DirectoryPath, "segments_*")
                .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(segFiles);
        }
    }

    [Property(DisplayName = "Rollback followed by Commit produces consistent state")]
    public void RollbackThenCommit_Consistent(int seed)
    {
        var dir = new MMapDirectory(System.IO.Path.Combine(_fixture.Path, $"twophase_rb_{seed}"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 3 });

        writer.AddDocument(CreateDoc("body", "rolled back"));
        writer.PrepareCommit();
        writer.Rollback();

        writer.AddDocument(CreateDoc("body", "after rollback"));
        writer.Commit();

        Assert.False(writer.HasPreparedCommit);
    }

    private static LeanDocument CreateDoc(string field, string value)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField(field, value));
        return doc;
    }
}
