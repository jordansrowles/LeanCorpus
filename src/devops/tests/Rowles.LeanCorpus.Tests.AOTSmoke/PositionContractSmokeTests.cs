using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Xunit;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public sealed class PositionContractSmokeTests
{
    [Fact]
    public void SegmentMarkerAndGappedQueriesSurviveNativeSerialisation()
    {
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("aot"), "aot-position-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "said the government"));
                writer.AddDocument(document);
                writer.Commit();
            }
            string metadata = Assert.Single(Directory.GetFiles(path, "*.seg"));
            var info = SegmentInfo.ReadFrom(metadata);
            Assert.Equal(SegmentInfo.CurrentPositionContract, info.PositionContract);
            Assert.Equal(info.PositionContract, info.DeepCopy().PositionContract);
            using var searcher = new IndexSearcher(directory);
            var parser = new QueryParser("body", new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser());
            Assert.Equal(0, searcher.Search(parser.Parse("\"said government\""), 10,
                TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, searcher.Search(parser.Parse("\"said the government\""), 10,
                TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
