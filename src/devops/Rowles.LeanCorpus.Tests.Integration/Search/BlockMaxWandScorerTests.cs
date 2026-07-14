using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
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

    [Fact(DisplayName = "WAND: Small posting list does not skip valid tail docs")]
    public void Wand_SmallPostingList_DoesNotSkipValidTailDocs()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "small.doc");
            TermPostingMetadata meta;
            using (var docOut = new IndexOutput(docPath))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                // 5 docs in a single block; topN=2 means the collector fills before
                // the block is finished, so WAND must bound the remaining docs safely.
                for (int i = 0; i < 5; i++) writer.AddPosting(i, freq: i + 1);
                meta = writer.FinishTerm();
            }

            using var input = new IndexInput(docPath);
            var postings = BlockPostingsEnum.Create(
                input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

            var termScorer = new BlockMaxWandScorer.TermScorer(
                postings, idf: 1.0f, k1: 1.2f, b: 0.75f, avgDl: 100f);

            var wand = new BlockMaxWandScorer([termScorer]);
            var results = wand.Score(topN: 2);

            Assert.Equal(2, results.ScoreDocs.Length);
            // Highest-scoring docs are those with highest frequency: doc 4 (freq 5) and doc 3 (freq 4).
            Assert.Equal(4, results.ScoreDocs[0].DocId);
            Assert.Equal(3, results.ScoreDocs[1].DocId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact(DisplayName = "WAND: Tail block after full blocks is bounded safely")]
    public void Wand_TailBlockAfterFullBlocks_IsBoundedSafely()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPathA = Path.Combine(dir, "a.doc");
            var docPathB = Path.Combine(dir, "b.doc");

            // Term A: 140 docs, last 12 form a tail block. The tail contains doc 139 with freq 50.
            TermPostingMetadata metaA, metaB;
            using (var docOutA = new IndexOutput(docPathA))
            {
                using var writer = new BlockPostingsWriter(docOutA);
                writer.StartTerm();
                for (int i = 0; i < 140; i++) writer.AddPosting(i, freq: i == 139 ? 50 : 1);
                metaA = writer.FinishTerm();
            }
            // Term B: 140 docs, all freq 1.
            using (var docOutB = new IndexOutput(docPathB))
            {
                using var writer = new BlockPostingsWriter(docOutB);
                writer.StartTerm();
                for (int i = 0; i < 140; i++) writer.AddPosting(i, freq: 1);
                metaB = writer.FinishTerm();
            }

            using var inputA = new IndexInput(docPathA);
            using var inputB = new IndexInput(docPathB);
            var pa = BlockPostingsEnum.Create(inputA, metaA.DocStartOffset, metaA.SkipOffset, metaA.DocFreq);
            var pb = BlockPostingsEnum.Create(inputB, metaB.DocStartOffset, metaB.SkipOffset, metaB.DocFreq);

            var scorers = new[]
            {
                new BlockMaxWandScorer.TermScorer(pa, idf: 1.5f, k1: 1.2f, b: 0.75f, avgDl: 100f),
                new BlockMaxWandScorer.TermScorer(pb, idf: 1.0f, k1: 1.2f, b: 0.75f, avgDl: 100f),
            };
            var wand = new BlockMaxWandScorer(scorers);
            var results = wand.Score(topN: 5);

            // Doc 139 has a very high score because term A has freq 50 there.
            Assert.Contains(results.ScoreDocs, sd => sd.DocId == 139);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact(DisplayName = "Flush: per-document norms are written into posting skip entries")]
    public void Flush_WritesNormsIntoSkipEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            using var mmap = new MMapDirectory(dir);
            var config = new IndexWriterConfig
            {
                MaxBufferedDocs = 1000,
                MergePolicy = NoMergePolicy.Instance
            };
            using var writer = new IndexWriter(mmap, config);

            // 300 docs containing the same term with varying lengths, so norms vary.
            for (int i = 0; i < 300; i++)
            {
                var doc = new LeanDocument();
                int repeats = (i % 10) + 1;
                doc.Add(new TextField("body", string.Join(" ", Enumerable.Repeat("term", repeats))));
                writer.AddDocument(doc);
            }
            writer.Commit();

            var segPos = Directory.GetFiles(dir, "seg_*.pos").Single();
            var segDic = segPos.Replace(".pos", ".dic");

            using var input = new IndexInput(segPos);
            _ = PostingsEnum.ValidateFileHeader(input);
            using var dictionary = TermDictionaryReader.Open(segDic);
            var termEntry = dictionary.EnumerateAllTerms().Single(t => t.Term == "body\0term");

            input.Seek(termEntry.Offset);
            int docFreq = input.ReadInt32();
            long skipOffset = input.ReadInt64();
            input.ReadBoolean(); // hasFreqs
            input.ReadBoolean(); // hasPositions
            input.ReadBoolean(); // hasPayloads
            long docStart = input.Position;

            var postings = BlockPostingsEnum.Create(input, docStart, skipOffset, docFreq);
            Assert.True(postings.SkipEntries.Length > 0, "Expected at least one skip entry for 300 docs.");
            foreach (var skip in postings.SkipEntries)
                Assert.True(skip.MaxNormInBlock > 0, "MaxNormInBlock should carry the real quantised norm after flush.");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact(DisplayName = "WAND: real norms produce lower block-max scores than the zero-norm fallback")]
    public void Wand_RealNorms_LowerBlockMaxScores()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPathWith = Path.Combine(dir, "with.doc");
            var docPathWithout = Path.Combine(dir, "without.doc");

            TermPostingMetadata metaWithNorms, metaWithoutNorms;
            using (var docOut = new IndexOutput(docPathWith))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int i = 0; i < 300; i++)
                    writer.AddPosting(i, (i % 5) + 1, norm: (byte)((i % 10) + 1));
                metaWithNorms = writer.FinishTerm();
            }
            using (var docOut = new IndexOutput(docPathWithout))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int i = 0; i < 300; i++)
                    writer.AddPosting(i, (i % 5) + 1);
                metaWithoutNorms = writer.FinishTerm();
            }

            using var inputWith = new IndexInput(docPathWith);
            using var inputWithout = new IndexInput(docPathWithout);
            var postingsWith = BlockPostingsEnum.Create(inputWith, metaWithNorms.DocStartOffset, metaWithNorms.SkipOffset, metaWithNorms.DocFreq);
            var postingsWithout = BlockPostingsEnum.Create(inputWithout, metaWithoutNorms.DocStartOffset, metaWithoutNorms.SkipOffset, metaWithoutNorms.DocFreq);

            var scorerWith = new BlockMaxWandScorer.TermScorer(postingsWith, idf: 2.5f, k1: 1.2f, b: 0.75f, avgDl: 100f);
            var scorerWithout = new BlockMaxWandScorer.TermScorer(postingsWithout, idf: 2.5f, k1: 1.2f, b: 0.75f, avgDl: 100f);

            Assert.True(scorerWith.BlockMaxScores.Length > 0);
            for (int i = 0; i < scorerWith.BlockMaxScores.Length; i++)
            {
                Assert.True(scorerWith.BlockMaxScores[i] < scorerWithout.BlockMaxScores[i],
                    $"Block {i}: real norms should yield a tighter bound than the zero-norm fallback.");
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}