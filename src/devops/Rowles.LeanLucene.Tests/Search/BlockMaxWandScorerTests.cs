using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Tests for the Block-Max WAND scorer that uses per-block impact metadata
/// to skip non-competitive blocks during top-K evaluation.
/// </summary>
public sealed class BlockMaxWandScorerTests
{
    /// <summary>
    /// Verifies the Score: Single Term Returns All Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Score: Single Term Returns All Docs")]
    public void Score_SingleTerm_ReturnsAllDocs()
    {
        // Arrange — write 5 docs via BlockPostingsWriter, read back through BlockMaxWandScorer
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "single_term.doc");
            int[] docIds = [10, 20, 30, 40, 50];
            int[] freqs = [1, 2, 3, 2, 1];

            TermPostingMetadata meta;
            using (var docOut = new IndexOutput(docPath))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int i = 0; i < docIds.Length; i++)
                    writer.AddPosting(docIds[i], freqs[i]);
                meta = writer.FinishTerm();
            }

            using var input = new IndexInput(docPath);
            var postings = BlockPostingsEnum.Create(
                input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

            // Wrap in a TermScorer with standard BM25 parameters
            var termScorer = new BlockMaxWandScorer.TermScorer(
                postings, idf: 1.0f, k1: 1.2f, b: 0.75f, avgDl: 100f);

            var wandScorer = new BlockMaxWandScorer(
                [termScorer], topN: 10, k1: 1.2f, b: 0.75f, avgDl: 100f);

            // Act
            var results = wandScorer.Score();

            // Assert — all 5 documents should be returned
            Assert.Equal(docIds.Length, results.Length);

            var returnedDocIds = results.Select(r => r.DocId).OrderBy(id => id).ToArray();
            Assert.Equal(docIds, returnedDocIds);

            // Every scored document should have a positive score
            Assert.All(results, r => Assert.True(r.Score > 0f,
                $"Doc {r.DocId} should have a positive score but was {r.Score}"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Verifies the Blocks Skipped: Is Non Negative scenario.
    /// </summary>
    [Fact(DisplayName = "Blocks Skipped: Is Non Negative")]
    public void BlocksSkipped_IsNonNegative()
    {
        // Arrange — write a posting list and score it; the counter must never go negative
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "blocks_skipped.doc");

            TermPostingMetadata meta;
            using (var docOut = new IndexOutput(docPath))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int i = 0; i < 300; i++)
                    writer.AddPosting(i, (i % 5) + 1);
                meta = writer.FinishTerm();
            }

            using var input = new IndexInput(docPath);
            var postings = BlockPostingsEnum.Create(
                input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

            var termScorer = new BlockMaxWandScorer.TermScorer(
                postings, idf: 2.5f, k1: 1.2f, b: 0.75f, avgDl: 100f);

            var wandScorer = new BlockMaxWandScorer(
                [termScorer], topN: 5, k1: 1.2f, b: 0.75f, avgDl: 100f);

            // Act
            _ = wandScorer.Score();

            // Assert
            Assert.True(wandScorer.BlocksSkipped >= 0,
                $"BlocksSkipped should be non-negative but was {wandScorer.BlocksSkipped}");
            Assert.True(wandScorer.BlocksScored >= 0,
                $"BlocksScored should be non-negative but was {wandScorer.BlocksScored}");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
