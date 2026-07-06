using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

public sealed class BlockMaxWandScorerTests
{
    [Fact(DisplayName = "Score: Single Term Returns All Docs")]
    public void Score_SingleTerm_ReturnsAllDocs()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "single.doc");
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

            var termScorer = new BlockMaxWandScorer.TermScorer(
                postings, idf: 1.0f, k1: 1.2f, b: 0.75f, avgDl: 100f);

            var wand = new BlockMaxWandScorer([termScorer]);
            var results = wand.Score(topN: 10);

            Assert.Equal(docIds.Length, results.ScoreDocs.Length);
            var returned = results.ScoreDocs.Select(r => r.DocId).OrderBy(id => id).ToArray();
            Assert.Equal(docIds, returned);
            Assert.All(results.ScoreDocs, r => Assert.True(r.Score > 0f));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact(DisplayName = "Blocks Skipped: Is Non Negative")]
    public void BlocksSkipped_IsNonNegative()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "blocks.doc");
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

            var wand = new BlockMaxWandScorer([termScorer]);
            _ = wand.Score(topN: 5);

            Assert.True(wand.BlocksSkipped >= 0);
            Assert.True(wand.BlocksScored >= 0);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact(DisplayName = "WAND: Top-K Matches Brute Force")]
    public void Wand_TopKMatchesBruteForce()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPathA = Path.Combine(dir, "a.doc");
            var docPathB = Path.Combine(dir, "b.doc");

            TermPostingMetadata metaA, metaB;
            using (var docOutA = new IndexOutput(docPathA))
            {
                using var writer = new BlockPostingsWriter(docOutA);
                writer.StartTerm();
                for (int i = 0; i < 200; i += 2) writer.AddPosting(i, freq: 2);
                metaA = writer.FinishTerm();
            }
            using (var docOutB = new IndexOutput(docPathB))
            {
                using var writer = new BlockPostingsWriter(docOutB);
                writer.StartTerm();
                for (int i = 0; i < 200; i++) writer.AddPosting(i, freq: 1);
                metaB = writer.FinishTerm();
            }

            using var inputA = new IndexInput(docPathA);
            using var inputB = new IndexInput(docPathB);
            var pa = BlockPostingsEnum.Create(inputA, metaA.DocStartOffset, metaA.SkipOffset, metaA.DocFreq);
            var pb = BlockPostingsEnum.Create(inputB, metaB.DocStartOffset, metaB.SkipOffset, metaB.DocFreq);

            var scorers = new[]
            {
                new BlockMaxWandScorer.TermScorer(pa, idf: 1.5f, k1: 1.2f, b: 0.75f, avgDl: 50f),
                new BlockMaxWandScorer.TermScorer(pb, idf: 1.0f, k1: 1.2f, b: 0.75f, avgDl: 50f),
            };
            var wand = new BlockMaxWandScorer(scorers);
            var wandResults = wand.Score(topN: 10);

            using var inputA2 = new IndexInput(docPathA);
            using var inputB2 = new IndexInput(docPathB);
            var pa2 = BlockPostingsEnum.Create(inputA2, metaA.DocStartOffset, metaA.SkipOffset, metaA.DocFreq);
            var pb2 = BlockPostingsEnum.Create(inputB2, metaB.DocStartOffset, metaB.SkipOffset, metaB.DocFreq);

            var bf = new TopNCollector(10);
            int curA = pa2.NextDoc(), curB = pb2.NextDoc();
            while (true)
            {
                int minDoc = Math.Min(curA, curB);
                if (minDoc == BlockPostingsEnum.NoMoreDocs) break;
                float score = 0f;
                if (curA == minDoc)
                {
                    float tf = pa2.Freq;
                    score += 1.5f * ((tf * 2.2f) / (tf + 1.2f * (0.25f + 0.75f * (50f / 50f))));
                    curA = pa2.NextDoc();
                }
                if (curB == minDoc)
                {
                    float tf = pb2.Freq;
                    score += 1.0f * ((tf * 2.2f) / (tf + 1.2f * (0.25f + 0.75f * (50f / 50f))));
                    curB = pb2.NextDoc();
                }
                bf.Collect(minDoc, score);
            }
            var bfResults = bf.ToTopDocs();

            Assert.Equal(bfResults.ScoreDocs.Length, wandResults.ScoreDocs.Length);
            for (int i = 0; i < bfResults.ScoreDocs.Length; i++)
            {
                Assert.Equal(bfResults.ScoreDocs[i].DocId, wandResults.ScoreDocs[i].DocId);
                Assert.Equal(bfResults.ScoreDocs[i].Score, wandResults.ScoreDocs[i].Score, 4);
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}