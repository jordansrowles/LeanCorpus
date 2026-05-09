using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Reproduces the BlockJoinBenchmarks crash: ParentBitSet.Set throws
/// IndexOutOfRangeException when a mid-indexing flush occurs during
/// block-join indexing (blockCount × docsPerBlock > MaxBufferedDocs).
/// </summary>
public sealed class BlockJoinBenchmarkReproTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public BlockJoinBenchmarkReproTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Block Join Query: Flush During Block Indexing Does Not Crash scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Flush During Block Indexing Does Not Crash")]
    public void BlockJoinQuery_FlushDuringBlockIndexing_DoesNotCrash()
    {
        var dir = Path.Combine(_path, nameof(BlockJoinQuery_FlushDuringBlockIndexing_DoesNotCrash));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        // 100 blocks × 4 docs = 400 docs, MaxBufferedDocs = 50 → forces multiple mid-indexing flushes
        const int blockCount = 100;
        const int childrenPerBlock = 3;

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = 50
        }))
        {
            for (int i = 0; i < blockCount; i++)
            {
                var block = new List<LeanDocument>();

                for (int c = 0; c < childrenPerBlock; c++)
                {
                    var child = new LeanDocument();
                    child.Add(new TextField("body", $"child {c} comment on topic {i}"));
                    child.Add(new StringField("type", "child"));
                    block.Add(child);
                }

                var parent = new LeanDocument();
                parent.Add(new TextField("title", $"parent {i} post"));
                parent.Add(new StringField("type", "parent"));
                block.Add(parent);

                writer.AddDocumentBlock(block);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var childQuery = new TermQuery("body", "comment");
        var blockJoin = new BlockJoinQuery(childQuery);
        var topDocs = searcher.Search(blockJoin, 25);

        Assert.True(topDocs.TotalHits > 0, "Expected at least 1 parent hit from block-join query");
    }
}
