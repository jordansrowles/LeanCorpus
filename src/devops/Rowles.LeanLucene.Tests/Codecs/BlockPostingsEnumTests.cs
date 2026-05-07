using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Block Postings Enum.
/// </summary>
public sealed class BlockPostingsEnumTests : IDisposable
{
    private readonly string _tempDir;

    public BlockPostingsEnumTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leanlucene_bpe_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private (string docPath, TermPostingMetadata meta) WritePostings(
        int[] docIds, int[] freqs)
    {
        var docPath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.doc");
        TermPostingMetadata meta;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            for (int i = 0; i < docIds.Length; i++)
                writer.AddPosting(docIds[i], freqs[i]);
            meta = writer.FinishTerm();
        }
        return (docPath, meta);
    }

    /// <summary>
    /// Verifies the Next Doc: Returns All Doc IDs scenario.
    /// </summary>
    /// <param name="count">The count value for the test case.</param>
    [Theory(DisplayName = "Next Doc: Returns All Doc IDs")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(256)]
    [InlineData(1000)]
    public void NextDoc_ReturnsAllDocIds(int count)
    {
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 3; // gaps of 3
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        var result = new List<int>();
        int doc;
        while ((doc = pe.NextDoc()) != BlockPostingsEnum.NoMoreDocs)
            result.Add(doc);

        Assert.Equal(docIds, result.ToArray());
    }

    /// <summary>
    /// Verifies the Freq: Round Trips scenario.
    /// </summary>
    /// <param name="count">The count value for the test case.</param>
    [Theory(DisplayName = "Freq: Round Trips")]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(200)]
    public void Freq_RoundTrips(int count)
    {
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i;
            freqs[i] = (i % 5) + 1; // 1..5
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        var resultFreqs = new List<int>();
        while (pe.NextDoc() != BlockPostingsEnum.NoMoreDocs)
            resultFreqs.Add(pe.Freq);

        Assert.Equal(freqs, resultFreqs.ToArray());
    }

    /// <summary>
    /// Verifies the Advance: Skips To Target scenario.
    /// </summary>
    [Fact(DisplayName = "Advance: Skips To Target")]
    public void Advance_SkipsToTarget()
    {
        int count = 500;
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 2; // even: 0, 2, 4, ..., 998
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        // Advance to doc 400 (exists at index 200)
        int doc = pe.Advance(400);
        Assert.Equal(400, doc);

        // Advance to doc 801 → should land on 802
        doc = pe.Advance(801);
        Assert.Equal(802, doc);

        // Advance past all → NoMoreDocs
        doc = pe.Advance(9999);
        Assert.Equal(BlockPostingsEnum.NoMoreDocs, doc);
    }

    /// <summary>
    /// Verifies the Advance: Within Current Block scenario.
    /// </summary>
    [Fact(DisplayName = "Advance: Within Current Block")]
    public void Advance_WithinCurrentBlock()
    {
        int count = 128; // exactly one block
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 10;
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        // Move into the block first
        pe.NextDoc(); // doc 0
        Assert.Equal(0, pe.DocId);

        // Advance within the same block
        int doc = pe.Advance(500);
        Assert.Equal(500, doc);

        doc = pe.Advance(1270);
        Assert.Equal(1270, doc);
    }

    /// <summary>
    /// Verifies the Advance: Across Multiple Blocks scenario.
    /// </summary>
    [Fact(DisplayName = "Advance: Across Multiple Blocks")]
    public void Advance_AcrossMultipleBlocks()
    {
        int count = 10_000;
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i;
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        // Jump to different blocks
        Assert.Equal(5000, pe.Advance(5000));
        Assert.Equal(7777, pe.Advance(7777));
        Assert.Equal(9999, pe.Advance(9999));
        Assert.Equal(BlockPostingsEnum.NoMoreDocs, pe.Advance(10000));
    }

    /// <summary>
    /// Verifies the Empty Postings: Immediately Exhausted scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Postings: Immediately Exhausted")]
    public void EmptyPostings_ImmediatelyExhausted()
    {
        // Create a posting with 0 docs (write header only)
        var docPath = Path.Combine(_tempDir, "empty.doc");
        TermPostingMetadata meta;
        using (var docOut = new IndexOutput(docPath))
        {
            using var writer = new BlockPostingsWriter(docOut);
            writer.StartTerm();
            meta = writer.FinishTerm();
        }

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        Assert.Equal(BlockPostingsEnum.NoMoreDocs, pe.NextDoc());
        Assert.True(pe.IsExhausted);
    }

    /// <summary>
    /// Verifies the Large Scale: 100 K All Docs Returned scenario.
    /// </summary>
    [Fact(DisplayName = "Large Scale: 100 K All Docs Returned")]
    public void LargeScale_100K_AllDocsReturned()
    {
        int count = 100_000;
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 2;
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        int resultCount = 0;
        int lastDoc = -1;
        int doc;
        while ((doc = pe.NextDoc()) != BlockPostingsEnum.NoMoreDocs)
        {
            Assert.True(doc > lastDoc, "Doc IDs must be strictly increasing");
            Assert.Equal(docIds[resultCount], doc);
            lastDoc = doc;
            resultCount++;
        }

        Assert.Equal(count, resultCount);
    }

    /// <summary>
    /// Verifies the Advance: To Exact Block Boundary scenario.
    /// </summary>
    [Fact(DisplayName = "Advance: To Exact Block Boundary")]
    public void Advance_ToExactBlockBoundary()
    {
        // 256 docs = exactly 2 blocks, advance to first doc of second block
        int count = 256;
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i;
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        // Advance to doc 128 (first doc of second block)
        int doc = pe.Advance(128);
        Assert.Equal(128, doc);
        Assert.Equal(1, pe.Freq);
    }

    /// <summary>
    /// Verifies the Singleton: Doc Freq One Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Singleton: Doc Freq One Round Trips")]
    public void Singleton_DocFreqOne_RoundTrips()
    {
        var (docPath, meta) = WritePostings([42], [3]);

        Assert.Equal(1, meta.DocFreq);
        Assert.Equal(42, meta.SingletonDocId);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        Assert.Equal(42, pe.NextDoc());
        Assert.Equal(3, pe.Freq);
        Assert.Equal(BlockPostingsEnum.NoMoreDocs, pe.NextDoc());
    }

    /// <summary>
    /// Verifies the Advance: Binary Search Skip Correct At All Scales scenario.
    /// </summary>
    /// <param name="count">The count value for the test case.</param>
    [Theory(DisplayName = "Advance: Binary Search Skip Correct At All Scales")]
    [InlineData(128)]    // 1 full block, no skip binary search
    [InlineData(256)]    // 2 blocks
    [InlineData(1024)]   // 8 blocks
    [InlineData(10_000)] // 78 blocks — binary search is meaningful
    [InlineData(100_000)]// 781 blocks — binary search critical
    public void Advance_BinarySearchSkip_CorrectAtAllScales(int count)
    {
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 3; // gaps of 3
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        // Advance to various targets across the entire range
        int maxDocId = (count - 1) * 3;
        int[] targets = [0, 3, 126, 384, count / 2 * 3, (count - 2) * 3, (count - 1) * 3];
        foreach (int target in targets)
        {
            if (target > maxDocId) continue;
            pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);
            int doc = pe.Advance(target);
            Assert.Equal(target, doc);
        }

        // Advance to a value between entries (should land on next entry)
        pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);
        int midTarget = (count / 2) * 3 + 1; // between two entries
        int expected = ((count / 2) + 1) * 3;
        int result = pe.Advance(midTarget);
        Assert.Equal(expected, result);

        // Advance past end
        pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);
        Assert.Equal(BlockPostingsEnum.NoMoreDocs, pe.Advance(count * 3 + 1));
    }

    /// <summary>
    /// Verifies the Advance: Sequential Skips Across Many Blocks scenario.
    /// </summary>
    [Fact(DisplayName = "Advance: Sequential Skips Across Many Blocks")]
    public void Advance_SequentialSkips_AcrossManyBlocks()
    {
        // Simulates BooleanQuery follower advancement pattern:
        // leader iterates, followers call Advance() repeatedly
        int count = 5000;
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 2;
            freqs[i] = 1;
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        // Simulate follower: advance to every 100th doc ID
        for (int target = 0; target < count * 2; target += 200)
        {
            int doc = pe.Advance(target);
            Assert.Equal(target, doc);
        }
    }

    /// <summary>
    /// Verifies the Mixed Frequencies: Large Values scenario.
    /// </summary>
    [Fact(DisplayName = "Mixed Frequencies: Large Values")]
    public void MixedFrequencies_LargeValues()
    {
        int count = 300;
        var docIds = new int[count];
        var freqs = new int[count];
        for (int i = 0; i < count; i++)
        {
            docIds[i] = i * 100;
            freqs[i] = (i % 2 == 0) ? 1 : 1000; // alternating 1 and 1000
        }

        var (docPath, meta) = WritePostings(docIds, freqs);

        using var input = new IndexInput(docPath);
        var pe = BlockPostingsEnum.Create(input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

        for (int i = 0; i < count; i++)
        {
            Assert.NotEqual(BlockPostingsEnum.NoMoreDocs, pe.NextDoc());
            Assert.Equal(docIds[i], pe.DocId);
            Assert.Equal(freqs[i], pe.Freq);
        }
        Assert.Equal(BlockPostingsEnum.NoMoreDocs, pe.NextDoc());
    }
}
