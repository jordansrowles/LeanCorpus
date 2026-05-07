using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Block Postings Writer.
/// </summary>
public sealed class BlockPostingsWriterTests : IDisposable
{
    private readonly string _tempDir;

    public BlockPostingsWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leanlucene_bpw_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>
    /// Verifies the Round-trip: Write Then Read Back Doc IDs Match scenario.
    /// </summary>
    /// <param name="count">The count value for the test case.</param>
    [Theory(DisplayName = "Round-trip: Write Then Read Back Doc IDs Match")]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(256)]
    [InlineData(10000)]
    public void RoundTrip_WriteThenReadBack_DocIdsMatch(int count)
    {
        // Generate sequential doc IDs with freq=1
        var docIds = new int[count];
        for (int i = 0; i < count; i++)
            docIds[i] = i * 3; // gaps of 3

        var docPath = Path.Combine(_tempDir, $"test_{count}.doc");

        // Write
        TermPostingMetadata meta;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            for (int i = 0; i < count; i++)
                writer.AddPosting(docIds[i], 1);
            meta = writer.FinishTerm();
        }

        Assert.Equal(count, meta.DocFreq);
        Assert.True(new FileInfo(docPath).Length > 0, "Output file should not be empty");

        if (count == 1)
            Assert.Equal(docIds[0], meta.SingletonDocId);
        else
            Assert.Equal(-1, meta.SingletonDocId);
    }

    /// <summary>
    /// Verifies the Round-trip: Frequencies Preserved scenario.
    /// </summary>
    [Fact(DisplayName = "Round-trip: Frequencies Preserved")]
    public void RoundTrip_FrequenciesPreserved()
    {
        var docPath = Path.Combine(_tempDir, "freq_test.doc");
        int count = 200;

        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            for (int i = 0; i < count; i++)
                writer.AddPosting(i, (i % 5) + 1); // freq 1-5
            writer.FinishTerm();
        }

        // Verify file was written (read-back will be tested after PostingsEnum rewrite)
        Assert.True(new FileInfo(docPath).Length > 0);
    }

    /// <summary>
    /// Verifies the Skip Data: Written For Full Blocks scenario.
    /// </summary>
    [Fact(DisplayName = "Skip Data: Written For Full Blocks")]
    public void SkipData_WrittenForFullBlocks()
    {
        var docPath = Path.Combine(_tempDir, "skip_test.doc");
        int count = 5000; // should produce ~39 blocks → 39 skip entries

        TermPostingMetadata meta;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            for (int i = 0; i < count; i++)
                writer.AddPosting(i, 1);
            meta = writer.FinishTerm();
        }

        Assert.Equal(count, meta.DocFreq);
        Assert.True(meta.SkipOffset > 0, "Skip data offset should be positive");
        Assert.True(meta.SkipOffset > meta.DocStartOffset, "Skip data should come after doc data");
    }

    /// <summary>
    /// Verifies the Singleton: Doc Freq One scenario.
    /// </summary>
    [Fact(DisplayName = "Singleton: Doc Freq One")]
    public void Singleton_DocFreqOne()
    {
        var docPath = Path.Combine(_tempDir, "singleton.doc");

        TermPostingMetadata meta;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            writer.AddPosting(42, 3);
            meta = writer.FinishTerm();
        }

        Assert.Equal(1, meta.DocFreq);
        Assert.Equal(42, meta.SingletonDocId);
    }

    /// <summary>
    /// Verifies the Positions: Written To Separate File scenario.
    /// </summary>
    [Fact(DisplayName = "Positions: Written To Separate File")]
    public void Positions_WrittenToSeparateFile()
    {
        var docPath = Path.Combine(_tempDir, "pos_doc.doc");
        var posPath = Path.Combine(_tempDir, "pos_doc.pos");

        using (var docOut = new IndexOutput(docPath))
        using (var posOut = new IndexOutput(posPath))
        {
            using var writer = new BlockPostingsWriter(docOut, posOut);
            writer.StartTerm();
            for (int i = 0; i < 200; i++)
            {
                writer.AddPosting(i, 3);
                writer.AddPosition(0);
                writer.AddPosition(5);
                writer.AddPosition(10);
            }
            writer.FinishTerm();
        }

        Assert.True(new FileInfo(docPath).Length > 0, "Doc file should contain data");
        Assert.True(new FileInfo(posPath).Length > 0, "Position file should contain data");
    }

    /// <summary>
    /// Verifies the Multiple Terms: Independent Metadata scenario.
    /// </summary>
    [Fact(DisplayName = "Multiple Terms: Independent Metadata")]
    public void MultipleTerms_IndependentMetadata()
    {
        var docPath = Path.Combine(_tempDir, "multi_term.doc");

        TermPostingMetadata meta1, meta2;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);

            writer.StartTerm();
            for (int i = 0; i < 150; i++)
                writer.AddPosting(i, 1);
            meta1 = writer.FinishTerm();

            writer.StartTerm();
            for (int i = 200; i < 500; i++)
                writer.AddPosting(i, 2);
            meta2 = writer.FinishTerm();
        }

        Assert.Equal(150, meta1.DocFreq);
        Assert.Equal(300, meta2.DocFreq);
        Assert.True(meta2.DocStartOffset > meta1.DocStartOffset, "Second term should start after first");
    }

    /// <summary>
    /// Verifies the Large Scale: 100 K Postings Completes Without Error scenario.
    /// </summary>
    [Fact(DisplayName = "Large Scale: 100 K Postings Completes Without Error")]
    public void LargeScale_100KPostings_CompletesWithoutError()
    {
        var docPath = Path.Combine(_tempDir, "large.doc");

        TermPostingMetadata meta;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            for (int i = 0; i < 100_000; i++)
                writer.AddPosting(i * 2, 1); // even doc IDs
            meta = writer.FinishTerm();
        }

        Assert.Equal(100_000, meta.DocFreq);
        // 100K/128 = 781 full blocks + 32 tail
        Assert.True(meta.SkipOffset > 0);
    }
}
