using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentReaderDocValuesColumnTests(ITestOutputHelper output)
{
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
