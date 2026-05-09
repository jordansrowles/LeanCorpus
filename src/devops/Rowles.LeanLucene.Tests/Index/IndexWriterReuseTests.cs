using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Contains unit tests for Index Writer Reuse.
/// </summary>
[Trait("Category", "Index")]
public class IndexWriterReuseTests
{
    /// <summary>
    /// Verifies the Multiple Flushes: Reuse Buffers Produce Correct Results scenario.
    /// </summary>
    [Fact(DisplayName = "Multiple Flushes: Reuse Buffers Produce Correct Results")]
    public void MultipleFlushes_ReuseBuffers_ProduceCorrectResults()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ll_reuse_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var config = new IndexWriterConfig { MaxBufferedDocs = 5 };
            using (var writer = new IndexWriter(new MMapDirectory(dir), config))
            {
                // First batch — triggers flush at 5 docs
                for (int i = 0; i < 5; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new TextField("body", $"batch one document {i}"));
                    writer.AddDocument(doc);
                }
                writer.Commit();

                // Second batch — buffers should have been cleared and reused
                for (int i = 0; i < 5; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new TextField("body", $"batch two document {i}"));
                    writer.AddDocument(doc);
                }
                writer.Commit();
            }

            using var searcher = new IndexSearcher(new MMapDirectory(dir));
            var results1 = searcher.Search(new TermQuery("body", "one"), 10);
            var results2 = searcher.Search(new TermQuery("body", "two"), 10);

            Assert.Equal(5, results1.TotalHits);
            Assert.Equal(5, results2.TotalHits);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Verifies the Phrase Query: After Multiple Flushes Finds Correct Phrases scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: After Multiple Flushes Finds Correct Phrases")]
    public void PhraseQuery_AfterMultipleFlushes_FindsCorrectPhrases()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ll_phrase_reuse_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var config = new IndexWriterConfig { MaxBufferedDocs = 10 };
            using (var writer = new IndexWriter(new MMapDirectory(dir), config))
            {
                var doc1 = new LeanDocument();
                doc1.Add(new TextField("body", "the quick brown fox"));
                writer.AddDocument(doc1);

                var doc2 = new LeanDocument();
                doc2.Add(new TextField("body", "quick brown rabbit"));
                writer.AddDocument(doc2);

                var doc3 = new LeanDocument();
                doc3.Add(new TextField("body", "slow brown fox"));
                writer.AddDocument(doc3);

                writer.Commit();
            }

            using var searcher = new IndexSearcher(new MMapDirectory(dir));
            var results = searcher.Search(new PhraseQuery("body", ["quick", "brown"]), 10);

            Assert.Equal(2, results.TotalHits);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
