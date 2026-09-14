using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer.Columns;

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

    [Fact(DisplayName = "DwptFlushBatchSource: exposes captured state after DWPT reset")]
    public void DwptFlushBatchSource_ExposesCapturedStateAfterDwptReset()
    {
        var dwpt = CreateDwpt();
        dwpt.AddDocument(CreateFullDocument());
        dwpt.ParentDocIds = [0];

        DwptFlushBatch snapshot;
        lock (dwpt)
            snapshot = DwptFlushBatch.CaptureFrom(dwpt);

        IFlushSource source = new DwptFlushBatchSource(snapshot);

        Assert.Equal(1, snapshot.DocCount);
        Assert.Equal(0, dwpt.DocCount);
        AssertSourceContainsAllFields(source);
        Assert.Contains(snapshot.EnumeratePostings(), static posting => posting.Term == "body\0alpha");

        var utf8Postings = new (byte[] TermUtf8, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostingsUtf8(utf8Postings);
        Assert.Contains(
            utf8Postings,
            static posting => System.Text.Encoding.UTF8.GetString(posting.TermUtf8) == "body\0alpha");

        var pending = new FlushPendingState
        {
            Batch = snapshot,
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
        snapshot.Dispose();
    }

    private static void AssertSourceContainsAllFields(IFlushSource source)
    {
        Assert.Equal(1, source.DocCount);
        Assert.Contains("body", source.FieldNames);
        Assert.NotEmpty(source.DocTokenCounts);
        Assert.NotEmpty(source.FieldBoosts);
        Assert.NotEmpty(source.StoredDocStarts);
        Assert.NotEmpty(source.StoredFieldIds);
        Assert.NotEmpty(source.StoredFieldValues);
        Assert.NotEmpty(source.StoredFieldIdToName);
        Assert.NotEmpty(source.NumericIndex);
        Assert.NotEmpty(source.Int64Index);
        Assert.NotEmpty(source.Vectors);
        Assert.NotEmpty(source.NumericDocValues);
        Assert.NotEmpty(source.Int64DocValues);
        Assert.NotEmpty(source.SortedDocValues);
        Assert.NotEmpty(source.SortedSetDocValues);
        Assert.NotEmpty(source.SortedNumericDocValues);
        Assert.NotEmpty(source.Int64SortedDocValues);
        Assert.NotEmpty(source.BinaryDocValues);
        Assert.NotNull(source.ParentDocIds);
        Assert.NotEmpty(source.PostingAccumulators);
        Assert.True(source.PostingsCount > 0);
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
