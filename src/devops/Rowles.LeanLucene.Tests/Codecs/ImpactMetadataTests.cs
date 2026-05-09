using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Tests that BlockPostingsWriter correctly writes per-block impact metadata
/// (MaxFreqInBlock, MaxNormInBlock) into skip entries, and that
/// BlockPostingsEnum reads them back faithfully.
/// </summary>
public sealed class ImpactMetadataTests
{
    /// <summary>
    /// Writes 256 docs (2 full blocks of 128) with varying frequencies.
    /// Verifies the skip entries contain the correct MaxFreqInBlock for each block.
    /// </summary>
    [Fact(DisplayName = "Skip Entries: Contain Max Freq")]
    public void SkipEntries_ContainMaxFreq()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "maxfreq.doc");

            // Block 0 (docs 0..127): freqs 1..128  → max = 128
            // Block 1 (docs 128..255): freqs 1..128 → max = 128
            const int blockSize = 128;
            const int totalDocs = blockSize * 2;
            int expectedMaxFreqBlock0 = blockSize;   // freq peaks at 128
            int expectedMaxFreqBlock1 = blockSize;

            TermPostingMetadata meta;
            using (var docOut = new IndexOutput(docPath))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int i = 0; i < totalDocs; i++)
                {
                    int freq = (i % blockSize) + 1; // 1..128 per block
                    writer.AddPosting(i, freq);
                }
                meta = writer.FinishTerm();
            }

            // Act
            using var input = new IndexInput(docPath);
            var pe = BlockPostingsEnum.Create(
                input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

            var skipEntries = pe.SkipEntries;

            // Assert
            Assert.Equal(2, skipEntries.Length);
            Assert.Equal(expectedMaxFreqBlock0, skipEntries[0].MaxFreqInBlock);
            Assert.Equal(expectedMaxFreqBlock1, skipEntries[1].MaxFreqInBlock);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Writes 256 docs with norm values and verifies MaxNormInBlock is round-tripped.
    /// </summary>
    [Fact(DisplayName = "Skip Entries: Contain Max Norm")]
    public void SkipEntries_ContainMaxNorm()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "maxnorm.doc");

            const int blockSize = 128;
            const int totalDocs = blockSize * 2;

            // Block 0: norms 1..128 → max = 128
            // Block 1: norms 200 for all → max = 200
            byte expectedMaxNormBlock0 = 128;
            byte expectedMaxNormBlock1 = 200;

            TermPostingMetadata meta;
            using (var docOut = new IndexOutput(docPath))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int i = 0; i < totalDocs; i++)
                {
                    byte norm = i < blockSize
                        ? (byte)((i % blockSize) + 1)  // 1..128
                        : expectedMaxNormBlock1;         // 200
                    writer.AddPosting(i, 1, norm);
                }
                meta = writer.FinishTerm();
            }

            // Act
            using var input = new IndexInput(docPath);
            var pe = BlockPostingsEnum.Create(
                input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

            var skipEntries = pe.SkipEntries;

            // Assert
            Assert.Equal(2, skipEntries.Length);
            Assert.Equal(expectedMaxNormBlock0, skipEntries[0].MaxNormInBlock);
            Assert.Equal(expectedMaxNormBlock1, skipEntries[1].MaxNormInBlock);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Writes 512 docs (4 full blocks) where each block has a distinct max freq and
    /// max norm. Verifies all 4 skip entries round-trip correctly.
    /// </summary>
    [Fact(DisplayName = "Impact Metadata: Round-trip Multiple Blocks")]
    public void ImpactMetadata_RoundTrip_MultipleBlocks()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var docPath = Path.Combine(dir, "multiblock.doc");

            const int blockSize = 128;
            const int blockCount = 4;
            const int totalDocs = blockSize * blockCount;

            // Each block's "peak" doc uses a distinctive freq and norm.
            // All other docs in the block use freq=1, norm=1.
            ushort[] expectedMaxFreqs = [50, 100, 200, 255];
            byte[] expectedMaxNorms = [10, 80, 180, 250];

            TermPostingMetadata meta;
            using (var docOut = new IndexOutput(docPath))
            {
                using var writer = new BlockPostingsWriter(docOut);
                writer.StartTerm();
                for (int block = 0; block < blockCount; block++)
                {
                    int blockStart = block * blockSize;
                    for (int j = 0; j < blockSize; j++)
                    {
                        int docId = blockStart + j;
                        // Last doc in each block carries the peak values
                        int freq = (j == blockSize - 1) ? expectedMaxFreqs[block] : 1;
                        byte norm = (j == blockSize - 1) ? expectedMaxNorms[block] : (byte)1;
                        writer.AddPosting(docId, freq, norm);
                    }
                }
                meta = writer.FinishTerm();
            }

            // Act
            using var input = new IndexInput(docPath);
            var pe = BlockPostingsEnum.Create(
                input, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);

            var skipEntries = pe.SkipEntries;

            // Assert
            Assert.Equal(blockCount, skipEntries.Length);

            for (int i = 0; i < blockCount; i++)
            {
                Assert.Equal(expectedMaxFreqs[i], skipEntries[i].MaxFreqInBlock);
                Assert.Equal(expectedMaxNorms[i], skipEntries[i].MaxNormInBlock);
            }

            // Bonus: verify doc IDs still round-trip correctly through all blocks
            var allDocs = new List<int>();
            int doc;
            // Re-open to get a fresh enum positioned before first doc
            using var input2 = new IndexInput(docPath);
            var pe2 = BlockPostingsEnum.Create(
                input2, meta.DocStartOffset, meta.SkipOffset, meta.DocFreq);
            while ((doc = pe2.NextDoc()) != BlockPostingsEnum.NoMoreDocs)
                allDocs.Add(doc);

            Assert.Equal(totalDocs, allDocs.Count);
            for (int i = 0; i < totalDocs; i++)
                Assert.Equal(i, allDocs[i]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
