using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentReaderDocValuesColumnTests(ITestOutputHelper output)
{
    [Fact(DisplayName = "SegmentReader: numeric index and DocValues columns remain distinct read paths")]
    public void NumericIndexAndDocValuesColumn_UseDistinctReadPaths()
    {
        const int documentCount = 3;
        string path = Path.Combine(Path.GetTempPath(), "ll_dv_paths_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = documentCount + 1
            }))
            {
                for (int i = 0; i < documentCount; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new NumericField("price", 17.0 + i));
                    writer.AddDocument(document);
                }

                writer.Commit();
            }

            string[] numericIndexes = Directory.GetFiles(path, "seg_*.num");
            Assert.Single(numericIndexes);
            NumericIndexCodec.WriteDouble(numericIndexes[0], new Dictionary<string, Dictionary<int, double>>
            {
                ["price"] = new() { [0] = 99.0 }
            });

            using var searcher = new IndexSearcher(directory);
            var reader = Assert.Single(searcher.GetSegmentReaders());
            Assert.True(reader.TryGetNumericValue("price", 0, out double numericIndexValue));
            Assert.Equal(99.0, numericIndexValue);

            Assert.True(reader.TryGetNumericDocValues("price", out var column));
            Assert.NotNull(column);
            Assert.True(column.TryGetValue(0, out double docValuesValue));
            Assert.Equal(17.0, docValuesValue);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact(DisplayName = "SegmentReader: numeric DocValues fallback reads one document without expanding the column")]
    public void NumericDocValuesFallback_ReadsOneDocumentWithoutExpandingTheColumn()
    {
        const int documentCount = 64_000;
        string path = Path.Combine(Path.GetTempPath(), "ll_dv_column_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = documentCount + 1
            }))
            {
                for (int i = 0; i < documentCount; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new NumericField("constant", 17.0));
                    writer.AddDocument(document);
                }

                writer.Commit();
            }

            // Exercise the supported legacy fallback where the sparse numeric index is absent.
            foreach (string numericIndex in Directory.GetFiles(path, "seg_*.num"))
                File.Delete(numericIndex);

            using var searcher = new IndexSearcher(directory);
            var reader = searcher.GetSegmentReaders().MaxBy(static candidate => candidate.MaxDoc)!;
            int segmentDocumentCount = reader.MaxDoc;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool found = reader.TryGetNumericValue("constant", segmentDocumentCount - 1, out double value);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine(
                $"One-value lookup allocated {allocated:N0} bytes for {segmentDocumentCount:N0} documents.");

            Assert.True(found);
            Assert.Equal(17.0, value);
            Assert.True(
                allocated < 128 * 1024,
                $"Reading one numeric DocValues entry allocated {allocated:N0} bytes for a {segmentDocumentCount:N0}-document column.");
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }
}
