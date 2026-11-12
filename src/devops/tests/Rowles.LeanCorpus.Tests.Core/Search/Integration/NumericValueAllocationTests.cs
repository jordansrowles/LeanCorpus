using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Per-hit allocation regression checks for numeric document value reads. Each read runs under a
/// query pin, as search execution does, after a warm-up read has built the segment's indexes.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class NumericValueAllocationTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 2_000;

    private readonly TestDirectoryFixture _fixture;

    public NumericValueAllocationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Numeric Values: Built Index Reads Do Not Allocate Per Document")]
    public void NumericValues_BuiltIndexReads_DoNotAllocatePerDocument()
    {
        using var directory = Build("numeric_alloc");
        using var searcher = new IndexSearcher(directory);
        var reader = Assert.Single(searcher.GetSegmentReaders());

        long allocated = reader.WithQueryPin(0, static (pinned, _) =>
        {
            // Warm up: build the numeric and Int64 indexes and JIT the read path.
            double doubleSum = 0;
            long int64Sum = 0;
            ReadAll(pinned, ref doubleSum, ref int64Sum);

            long before = GC.GetAllocatedBytesForCurrentThread();
            ReadAll(pinned, ref doubleSum, ref int64Sum);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(Enumerable.Range(0, DocumentCount).Sum(static i => i * 0.5) * 2, doubleSum);
            Assert.Equal(Enumerable.Range(0, DocumentCount).Sum(static i => (long)i * 3) * 2, int64Sum);
            return bytes;
        });

        Assert.Equal(0, allocated);
    }

    private static void ReadAll(SegmentReader reader, ref double doubleSum, ref long int64Sum)
    {
        for (int docId = 0; docId < DocumentCount; docId++)
        {
            Assert.True(reader.TryGetNumericValue("price", docId, out double price));
            Assert.True(reader.TryGetInt64Value("count", docId, out long count));
            doubleSum += price;
            int64Sum += count;
        }
    }

    private MMapDirectory Build(string name)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = DocumentCount + 1 });
        for (int i = 0; i < DocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new NumericField("price", i * 0.5));
            document.Add(new Int64Field("count", (long)i * 3));
            writer.AddDocument(document);
        }
        writer.Commit();
        return directory;
    }
}
