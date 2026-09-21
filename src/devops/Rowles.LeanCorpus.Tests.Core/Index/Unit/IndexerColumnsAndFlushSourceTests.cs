using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer.Columns;
using Rowles.LeanCorpus.Index.Indexer.Postings;
using System.Text;

namespace Rowles.LeanCorpus.Tests.Core.Index.Indexer;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class IndexerColumnsAndFlushSourceTests
{
    [Fact(DisplayName = "DenseColumn: expands, limits reads, and clears")]
    public void DenseColumn_ExpandsLimitsReadsAndClears()
    {
        var column = new DenseColumn<int>();

        column.Set(2, 20);
        Assert.Equal(3, column.Count);
        Assert.Equal([0, 0, 20], column.GetValues(10).ToArray());

        column.Set(0, 10);
        column.Set(5, 50);
        Assert.Equal(6, column.Count);
        Assert.Equal([10, 0, 20], column.GetValues(3).ToArray());
        Assert.Equal([10, 0, 20, 0, 0, 50], column.GetValues(10).ToArray());

        column.Clear();

        Assert.Equal(0, column.Count);
        Assert.Empty(column.GetValues(10).ToArray());
    }

    [Fact(DisplayName = "SparseColumn: grows, returns entries, and clears")]
    public void SparseColumn_GrowsReturnsEntriesAndClears()
    {
        var column = new SparseColumn<string>();

        column.Set(3, "three");
        column.Set(7, "seven");
        column.Set(0, "zero");
        column.Set(1, "one");
        column.Set(2, "two");

        Assert.Equal(5, column.Count);
        Assert.Equal(5, column.GetCount());
        var (docIds, values) = column.GetEntries();
        Assert.Equal([3, 7, 0, 1, 2], docIds.AsSpan(0, column.GetCount()).ToArray());
        Assert.Equal(["three", "seven", "zero", "one", "two"], values.AsSpan(0, column.GetCount()).ToArray());

        column.Clear();

        Assert.Equal(0, column.Count);
        Assert.Equal(0, column.GetCount());
        Assert.Empty(column.GetEntries().DocIds);
        Assert.Empty(column.GetEntries().Values);
    }

    [Fact(DisplayName = "MultiValuedColumn: stores multiple values and preserves gaps")]
    public void MultiValuedColumn_StoresMultipleValuesAndPreservesGaps()
    {
        var column = new MultiValuedColumn<int>();

        column.Add(0, 10);
        column.Add(0, 11);
        column.Add(2, 20);

        Assert.Equal([10, 11], column.GetValues(0).ToArray());
        Assert.Empty(column.GetValues(1).ToArray());
        Assert.Equal([20], column.GetValues(2).ToArray());
        Assert.Empty(column.GetValues(3).ToArray());

        var raw = column.GetRawData();
        Assert.Equal([0, -1, 2, -1], raw.DocStarts.AsSpan(0, 4).ToArray());
        Assert.Equal([10, 11, 20], raw.Values.AsSpan(0, raw.ValueCount).ToArray());
        Assert.Equal(3, raw.ValueCount);
        Assert.Equal(2, raw.MaxDocId);

        column.Clear();

        Assert.Empty(column.GetValues(0).ToArray());
        var cleared = column.GetRawData();
        Assert.Empty(cleared.DocStarts);
        Assert.Empty(cleared.Values);
        Assert.Equal(0, cleared.ValueCount);
        Assert.Equal(0, cleared.MaxDocId);
    }

    [Fact(DisplayName = "DwptFlushSnapshot: owns detached state after DWPT reset")]
    public void DwptFlushSnapshot_OwnsDetachedStateAfterDwptReset()
    {
        var dwpt = CreateDwpt();
        dwpt.AddDocument(CreateFullDocument());
        dwpt.ParentDocIds = [0];

        DwptFlushSnapshot batch;
        lock (dwpt)
            batch = DwptFlushSnapshot.CaptureFrom(dwpt);

        Assert.Equal(1, batch.DocCount);
        Assert.Equal(0, dwpt.DocCount);
        AssertBatchContainsAllFields(batch);
        Assert.Contains(Enumerable.Range(0, batch.Postings.TermCount),
            termId => Encoding.UTF8.GetString(batch.Postings.GetTerm(termId)) == "body\0alpha");

        var pending = new FlushPendingState
        {
            Snapshot = batch,
            SegmentOrdinal = 4,
            CommitGeneration = 0,
            SeqStart = 10,
            SeqEnd = 10,
            ExecutionTask = Task.FromResult(new SegmentInfo { SegmentId = "seg_4", DocCount = 1 })
        };

        Assert.Equal(1, pending.DocCount);
        Assert.Equal(4, pending.SegmentOrdinal);
        Assert.Equal(10, pending.SeqStart);
        Assert.Equal(10, pending.SeqEnd);
        Assert.Equal("seg_4", pending.ExecutionTask!.Result.SegmentId);
        batch.Dispose();
    }

    private static void AssertBatchContainsAllFields(DwptFlushSnapshot batch)
    {
        Assert.Equal(1, batch.DocCount);
        Assert.Contains("body", batch.FieldNames);
        Assert.NotEmpty(batch.DocTokenCounts);
        Assert.NotEmpty(batch.FieldBoosts);
        Assert.NotEmpty(batch.StoredDocStarts);
        Assert.NotEmpty(batch.StoredFieldIds);
        Assert.NotEmpty(batch.StoredValues);
        Assert.NotEmpty(batch.StoredFieldIdToName);
        Assert.NotEmpty(batch.NumericIndex);
        Assert.NotEmpty(batch.Int64Index);
        Assert.NotEmpty(batch.Vectors);
        Assert.NotEmpty(batch.NumericDocValues);
        Assert.NotEmpty(batch.Int64DocValues);
        Assert.NotEmpty(batch.SortedDocValues);
        Assert.NotEmpty(batch.SortedSetDocValues);
        Assert.NotEmpty(batch.SortedNumericDocValues);
        Assert.NotEmpty(batch.Int64SortedDocValues);
        Assert.NotEmpty(batch.BinaryDocValues);
        Assert.NotNull(batch.ParentDocIds);
        Assert.True(batch.Postings.TermCount > 0);
    }

    private static DocumentsWriterPerThread CreateDwpt()
    {
        var analyser = new WhitespaceAnalyser();
        return new DocumentsWriterPerThread(analyser, new Dictionary<string, IAnalyser>(), new IndexWriterConfig
        {
            DefaultAnalyser = analyser,
            StoreTermVectors = true
        });
    }

    private static LeanDocument CreateFullDocument()
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", "alpha beta", stored: true, boost: 2.0f));
        document.Add(new StringField(
            "tag",
            "alpha",
            stored: true,
            boost: 1.5f,
            docValues: StringDocValues.Sorted | StringDocValues.SortedSet | StringDocValues.Binary));
        document.Add(new NumericField("price", 1.5, stored: true, boost: 1.2f));
        document.Add(new Int64Field("count", 3, stored: true, boost: 1.3f));
        document.Add(new StoredField("note", "stored"));
        document.Add(new BinaryField("payload", new byte[] { 1, 2 }));
        document.Add(new InetAddressField("ip", System.Net.IPAddress.Parse("192.0.2.1")));
        document.Add(new VectorField("embedding", new float[] { 1, 2 }, boost: 1.4f));
        document.Add(new GeoPointField("place", 51.5, -0.12, boost: 1.5f));
        return document;
    }
}
