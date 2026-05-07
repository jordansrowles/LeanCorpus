using Rowles.LeanLucene.Index.Indexer;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Unit tests for PostingAccumulator: ArrayPool-backed accumulator for
/// doc IDs, frequencies, positions, and payloads during indexing.
/// </summary>
public sealed class PostingAccumulatorTests
{
    /// <summary>
    /// Verifies the Add: Single Doc Single Position Records Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Add: Single Doc Single Position Records Correctly")]
    public void Add_SingleDocSinglePosition_RecordsCorrectly()
    {
        var acc = new PostingAccumulator();

        acc.Add(0, 5);

        Assert.Equal(1, acc.Count);
        Assert.Equal(0, acc.DocIds[0]);
        Assert.Equal(1, acc.GetFreq(0));
        var positions = acc.GetPositions(0);
        Assert.Equal(1, positions.Length);
        Assert.Equal(5, positions[0]);
    }

    /// <summary>
    /// Verifies the Add: Same Doc Multiple Positions Increments Freq And Stores All scenario.
    /// </summary>
    [Fact(DisplayName = "Add: Same Doc Multiple Positions Increments Freq And Stores All")]
    public void Add_SameDocMultiplePositions_IncrementsFreqAndStoresAll()
    {
        var acc = new PostingAccumulator();

        acc.Add(3, 0);
        acc.Add(3, 10);
        acc.Add(3, 20);

        Assert.Equal(1, acc.Count);
        Assert.Equal(3, acc.GetFreq(0));
        var positions = acc.GetPositions(0);
        Assert.Equal(3, positions.Length);
        Assert.Equal(0, positions[0]);
        Assert.Equal(10, positions[1]);
        Assert.Equal(20, positions[2]);
    }

    /// <summary>
    /// Verifies the Add: Multiple Docs Tracks Each Separately scenario.
    /// </summary>
    [Fact(DisplayName = "Add: Multiple Docs Tracks Each Separately")]
    public void Add_MultipleDocs_TracksEachSeparately()
    {
        var acc = new PostingAccumulator();

        acc.Add(0, 1);
        acc.Add(1, 2);
        acc.Add(2, 3);

        Assert.Equal(3, acc.Count);
        Assert.Equal(0, acc.DocIds[0]);
        Assert.Equal(1, acc.DocIds[1]);
        Assert.Equal(2, acc.DocIds[2]);
    }

    /// <summary>
    /// Verifies the Add Doc Only: Records Doc With No Positions scenario.
    /// </summary>
    [Fact(DisplayName = "Add Doc Only: Records Doc With No Positions")]
    public void AddDocOnly_RecordsDocWithNoPositions()
    {
        var acc = new PostingAccumulator();

        acc.AddDocOnly(7);

        Assert.Equal(1, acc.Count);
        Assert.Equal(7, acc.DocIds[0]);
        Assert.Equal(0, acc.GetFreq(0));
        Assert.True(acc.GetPositions(0).IsEmpty);
    }

    /// <summary>
    /// Verifies the Add Doc Only: Duplicate Doc Ignores Second Add scenario.
    /// </summary>
    [Fact(DisplayName = "Add Doc Only: Duplicate Doc Ignores Second Add")]
    public void AddDocOnly_DuplicateDoc_IgnoresSecondAdd()
    {
        var acc = new PostingAccumulator();

        acc.AddDocOnly(5);
        acc.AddDocOnly(5);

        Assert.Equal(1, acc.Count);
    }

    /// <summary>
    /// Verifies the Add With Payload: Stores And Retrieves Payload scenario.
    /// </summary>
    [Fact(DisplayName = "Add With Payload: Stores And Retrieves Payload")]
    public void AddWithPayload_StoresAndRetrievesPayload()
    {
        var acc = new PostingAccumulator();
        byte[] payload = [0xDE, 0xAD];

        acc.AddWithPayload(0, 1, payload);

        Assert.Equal(1, acc.Count);
        Assert.True(acc.HasPayloads);
        var retrieved = acc.GetPayload(0, 0);
        Assert.NotNull(retrieved);
        Assert.Equal(payload, retrieved);
    }

    /// <summary>
    /// Verifies the Add With Payload: Multiple Positions Same Doc All Payloads Retrievable scenario.
    /// </summary>
    [Fact(DisplayName = "Add With Payload: Multiple Positions Same Doc All Payloads Retrievable")]
    public void AddWithPayload_MultiplePositionsSameDoc_AllPayloadsRetrievable()
    {
        var acc = new PostingAccumulator();
        byte[] p1 = [0x01];
        byte[] p2 = [0x02];

        acc.AddWithPayload(0, 0, p1);
        acc.AddWithPayload(0, 5, p2);

        Assert.Equal(2, acc.GetFreq(0));
        Assert.Equal(p1, acc.GetPayload(0, 0));
        Assert.Equal(p2, acc.GetPayload(0, 1));
    }

    /// <summary>
    /// Verifies the Grow: Triggered By Exceeding Initial Capacity All Data Preserved scenario.
    /// </summary>
    [Fact(DisplayName = "Grow: Triggered By Exceeding Initial Capacity All Data Preserved")]
    public void Grow_TriggeredByExceedingInitialCapacity_AllDataPreserved()
    {
        var acc = new PostingAccumulator();

        // Initial capacity is 4 — adding 5 docs forces Grow()
        for (int i = 0; i < 5; i++)
            acc.Add(i, i * 10);

        Assert.Equal(5, acc.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, acc.DocIds[i]);
            Assert.Equal(1, acc.GetFreq(i));
            Assert.Equal(i * 10, acc.GetPositions(i)[0]);
        }
    }

    /// <summary>
    /// Verifies the Ensure Pos Buf Capacity: Many Positions Trigger Growth scenario.
    /// </summary>
    [Fact(DisplayName = "Ensure Pos Buf Capacity: Many Positions Trigger Growth")]
    public void EnsurePosBufCapacity_ManyPositionsTriggerGrowth()
    {
        var acc = new PostingAccumulator();

        // Add many positions to a single doc to overflow the initial posBuf (capacity 8)
        for (int p = 0; p < 20; p++)
            acc.Add(0, p);

        Assert.Equal(1, acc.Count);
        Assert.Equal(20, acc.GetFreq(0));
        var positions = acc.GetPositions(0);
        Assert.Equal(20, positions.Length);
        for (int p = 0; p < 20; p++)
            Assert.Equal(p, positions[p]);
    }

    /// <summary>
    /// Verifies the Has Freqs: Returns True When Any Doc Has Positions scenario.
    /// </summary>
    [Fact(DisplayName = "Has Freqs: Returns True When Any Doc Has Positions")]
    public void HasFreqs_ReturnsTrue_WhenAnyDocHasPositions()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 1);

        Assert.True(acc.HasFreqs);
    }

    /// <summary>
    /// Verifies the Has Freqs: Returns False When Only Doc Only Entries scenario.
    /// </summary>
    [Fact(DisplayName = "Has Freqs: Returns False When Only Doc Only Entries")]
    public void HasFreqs_ReturnsFalse_WhenOnlyDocOnlyEntries()
    {
        var acc = new PostingAccumulator();
        acc.AddDocOnly(0);

        Assert.False(acc.HasFreqs);
    }

    /// <summary>
    /// Verifies the Has Positions: Returns True When Positions Exist scenario.
    /// </summary>
    [Fact(DisplayName = "Has Positions: Returns True When Positions Exist")]
    public void HasPositions_ReturnsTrue_WhenPositionsExist()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 5);

        Assert.True(acc.HasPositions);
    }

    /// <summary>
    /// Verifies the Has Positions: Returns False When No Positions scenario.
    /// </summary>
    [Fact(DisplayName = "Has Positions: Returns False When No Positions")]
    public void HasPositions_ReturnsFalse_WhenNoPositions()
    {
        var acc = new PostingAccumulator();
        acc.AddDocOnly(0);

        Assert.False(acc.HasPositions);
    }

    /// <summary>
    /// Verifies the Has Payloads: Returns False When No Payloads Added scenario.
    /// </summary>
    [Fact(DisplayName = "Has Payloads: Returns False When No Payloads Added")]
    public void HasPayloads_ReturnsFalse_WhenNoPayloadsAdded()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 1);

        Assert.False(acc.HasPayloads);
    }

    /// <summary>
    /// Verifies the Return Buffers: Resets State And Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Return Buffers: Resets State And Does Not Throw")]
    public void ReturnBuffers_ResetsStateAndDoesNotThrow()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 1);
        acc.Add(1, 2);

        acc.ReturnBuffers();

        Assert.Equal(0, acc.Count);
        Assert.True(acc.DocIds.IsEmpty);
    }

    /// <summary>
    /// Verifies the Remap Doc IDs: Translates And Sorts Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Remap Doc IDs: Translates And Sorts Correctly")]
    public void RemapDocIds_TranslatesAndSortsCorrectly()
    {
        var acc = new PostingAccumulator();

        // Add docs 0, 1, 2 with distinct positions
        acc.Add(0, 100);
        acc.Add(1, 200);
        acc.Add(2, 300);

        // Inverse perm: doc 0→2, doc 1→0, doc 2→1
        int[] inversePerm = [2, 0, 1];
        acc.RemapDocIds(inversePerm);

        // After remap: new doc IDs should be [0, 1, 2] (sorted)
        // Original doc 1 → new 0, original doc 2 → new 1, original doc 0 → new 2
        Assert.Equal(3, acc.Count);
        Assert.Equal(0, acc.DocIds[0]);
        Assert.Equal(1, acc.DocIds[1]);
        Assert.Equal(2, acc.DocIds[2]);

        // Positions should follow their original docs
        Assert.Equal(200, acc.GetPositions(0)[0]); // was doc 1
        Assert.Equal(300, acc.GetPositions(1)[0]); // was doc 2
        Assert.Equal(100, acc.GetPositions(2)[0]); // was doc 0
    }

    /// <summary>
    /// Verifies the Remap Doc IDs: Empty Accumulator Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Remap Doc IDs: Empty Accumulator Does Not Throw")]
    public void RemapDocIds_EmptyAccumulator_DoesNotThrow()
    {
        var acc = new PostingAccumulator();
        int[] inversePerm = [0, 1, 2];

        acc.RemapDocIds(inversePerm);

        Assert.Equal(0, acc.Count);
    }

    /// <summary>
    /// Verifies the Remap Doc IDs: Preserves Payloads scenario.
    /// </summary>
    [Fact(DisplayName = "Remap Doc IDs: Preserves Payloads")]
    public void RemapDocIds_PreservesPayloads()
    {
        var acc = new PostingAccumulator();
        byte[] p0 = [0xAA];
        byte[] p1 = [0xBB];

        acc.AddWithPayload(0, 10, p0);
        acc.AddWithPayload(1, 20, p1);

        // Swap: doc 0→1, doc 1→0
        int[] inversePerm = [1, 0];
        acc.RemapDocIds(inversePerm);

        Assert.Equal(0, acc.DocIds[0]);
        Assert.Equal(1, acc.DocIds[1]);
        Assert.Equal(p1, acc.GetPayload(0, 0)); // was doc 1
        Assert.Equal(p0, acc.GetPayload(1, 0)); // was doc 0
    }

    /// <summary>
    /// Verifies the Get Payload: Returns Null When No Payloads Initialised scenario.
    /// </summary>
    [Fact(DisplayName = "Get Payload: Returns Null When No Payloads Initialised")]
    public void GetPayload_ReturnsNull_WhenNoPayloadsInitialised()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 1);

        Assert.Null(acc.GetPayload(0, 0));
    }

    /// <summary>
    /// Verifies the Estimated Bytes: After Add Reflects Buffer Size scenario.
    /// </summary>
    [Fact(DisplayName = "Estimated Bytes: After Add Reflects Buffer Size")]
    public void EstimatedBytes_AfterAdd_ReflectsBufferSize()
    {
        var acc = new PostingAccumulator();

        acc.Add(0, 1);

        // After a single Add the rented buffers are live; estimated bytes must exceed
        // the 64-byte object overhead and be a reasonable size for the initial buffers.
        // Initial rent: 4 × int[4] (docIds, freqs, posStarts, posLengths) + int[8] (posBuf)
        // ArrayPool may return arrays >= requested, so we check a lower bound.
        long bytes = acc.EstimatedBytes;
        Assert.True(bytes > 64, $"Expected > 64 but was {bytes}");

        // 5 arrays × 4 elements × 4 bytes = 80 bytes minimum for doc arrays alone,
        // plus 8 × 4 = 32 for posBuf, plus 64 overhead = 176 minimum.
        Assert.True(bytes >= 176, $"Expected >= 176 but was {bytes}");
    }

    /// <summary>
    /// Verifies the Estimated Bytes: After Resize Increases scenario.
    /// </summary>
    [Fact(DisplayName = "Estimated Bytes: After Resize Increases")]
    public void EstimatedBytes_AfterResize_Increases()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 1);
        long bytesBefore = acc.EstimatedBytes;

        // Add enough distinct docs to force Grow() past the initial ArrayPool bucket.
        // ArrayPool.Rent(4) typically returns a 16-element array, so we need to
        // exceed 16 docs to trigger a rental from a larger bucket.
        for (int i = 1; i <= 20; i++)
            acc.Add(i, i * 10);

        long bytesAfter = acc.EstimatedBytes;
        Assert.True(bytesAfter > bytesBefore,
            $"Expected EstimatedBytes to increase after resize: before={bytesBefore}, after={bytesAfter}");
    }

    /// <summary>
    /// Verifies the Estimated Bytes: After Return Buffers Is Minimal scenario.
    /// </summary>
    [Fact(DisplayName = "Estimated Bytes: After Return Buffers Is Minimal")]
    public void EstimatedBytes_AfterReturnBuffers_IsMinimal()
    {
        var acc = new PostingAccumulator();
        for (int i = 0; i < 10; i++)
            acc.Add(i, i);

        acc.ReturnBuffers();

        // After returning all buffers, only the 64-byte object overhead remains.
        Assert.True(acc.EstimatedBytes <= 64,
            $"Expected <= 64 after ReturnBuffers but was {acc.EstimatedBytes}");
    }
}
