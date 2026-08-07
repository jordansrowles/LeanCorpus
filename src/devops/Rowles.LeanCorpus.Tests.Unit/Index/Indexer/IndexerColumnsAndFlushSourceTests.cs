using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer.Columns;

namespace Rowles.LeanCorpus.Tests.Unit.Index.Indexer;

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

    [Fact(DisplayName = "BufferFlushSource: exposes buffer state and copies postings")]
    public void BufferFlushSource_ExposesBufferStateAndCopiesPostings()
    {
        var buffer = CreateBuffer();
        IFlushSource source = new BufferFlushSource(buffer);

        AssertSourceContainsAllFields(source);
        Assert.Same(buffer.FieldNames, source.FieldNames);
        Assert.Same(buffer.DocTokenCounts, source.DocTokenCounts);
        Assert.Same(buffer.FieldBoosts, source.FieldBoosts);
        Assert.Same(buffer.StoredDocStarts, source.StoredDocStarts);
        Assert.Same(buffer.StoredFieldIds, source.StoredFieldIds);
        Assert.Same(buffer.StoredFieldValues, source.StoredFieldValues);
        Assert.Same(buffer.StoredFieldIdToName, source.StoredFieldIdToName);
        Assert.Same(buffer.NumericIndex, source.NumericIndex);
        Assert.Same(buffer.Int64Index, source.Int64Index);
        Assert.Same(buffer.Vectors, source.Vectors);
        Assert.Same(buffer.NumericDocValues, source.NumericDocValues);
        Assert.Same(buffer.Int64DocValues, source.Int64DocValues);
        Assert.Same(buffer.SortedDocValues, source.SortedDocValues);
        Assert.Same(buffer.SortedSetDocValues, source.SortedSetDocValues);
        Assert.Same(buffer.SortedNumericDocValues, source.SortedNumericDocValues);
        Assert.Same(buffer.Int64SortedDocValues, source.Int64SortedDocValues);
        Assert.Same(buffer.BinaryDocValues, source.BinaryDocValues);
        Assert.Same(buffer.ParentDocIds, source.ParentDocIds);
        Assert.Same(buffer.PostingAccumulators, source.PostingAccumulators);

        var postings = new (string Term, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostings(postings);
        Assert.Equal("body\0alpha", postings[0].Term);
        Assert.Same(buffer.PostingAccumulators[0], postings[0].Acc);

        var utf8Postings = new (byte[] TermUtf8, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostingsUtf8(utf8Postings);
        Assert.Equal("body\0alpha", System.Text.Encoding.UTF8.GetString(utf8Postings[0].TermUtf8));
        Assert.Same(buffer.PostingAccumulators[0], utf8Postings[0].Acc);
    }

    [Fact(DisplayName = "DwptFlushSource: exposes DWPT state and copies postings")]
    public void DwptFlushSource_ExposesDwptStateAndCopiesPostings()
    {
        var dwpt = CreateDwpt();
        dwpt.AddDocument(CreateFullDocument());
        dwpt.ParentDocIds = [0];
        IFlushSource source = new DwptFlushSource(dwpt);

        AssertSourceContainsAllFields(source);
        Assert.Equal(dwpt.DocCount, source.DocCount);
        Assert.Same(dwpt.FieldNames, source.FieldNames);
        Assert.Same(dwpt.DocTokenCounts, source.DocTokenCounts);
        Assert.Same(dwpt.FieldBoosts, source.FieldBoosts);
        Assert.Same(dwpt.StoredDocStarts, source.StoredDocStarts);
        Assert.Same(dwpt.StoredFieldIds, source.StoredFieldIds);
        Assert.Same(dwpt.StoredValues, source.StoredFieldValues);
        Assert.Same(dwpt.StoredFieldIdToName, source.StoredFieldIdToName);
        Assert.Same(dwpt.NumericIndex, source.NumericIndex);
        Assert.Same(dwpt.Int64Index, source.Int64Index);
        Assert.Same(dwpt.Vectors, source.Vectors);
        Assert.Same(dwpt.NumericDocValues, source.NumericDocValues);
        Assert.Same(dwpt.Int64DocValues, source.Int64DocValues);
        Assert.Same(dwpt.SortedDocValues, source.SortedDocValues);
        Assert.Same(dwpt.SortedSetDocValues, source.SortedSetDocValues);
        Assert.Same(dwpt.SortedNumericDocValues, source.SortedNumericDocValues);
        Assert.Same(dwpt.Int64SortedDocValues, source.Int64SortedDocValues);
        Assert.Same(dwpt.BinaryDocValues, source.BinaryDocValues);
        Assert.Same(dwpt.ParentDocIds, source.ParentDocIds);
        Assert.Same(dwpt.PostingAccumulators, source.PostingAccumulators);

        var postings = new (string Term, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostings(postings);
        Assert.Contains(postings, static posting => posting.Term == "body\0alpha");

        var utf8Postings = new (byte[] TermUtf8, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostingsUtf8(utf8Postings);
        Assert.Contains(
            utf8Postings,
            static posting => System.Text.Encoding.UTF8.GetString(posting.TermUtf8) == "body\0alpha");
    }

    [Fact(DisplayName = "SnapshotFlushSource: exposes captured state after DWPT reset")]
    public void SnapshotFlushSource_ExposesCapturedStateAfterDwptReset()
    {
        var dwpt = CreateDwpt();
        dwpt.AddDocument(CreateFullDocument());
        dwpt.ParentDocIds = [0];

        DwptFlushSnapshot snapshot;
        lock (dwpt)
            snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);

        IFlushSource source = new SnapshotFlushSource(snapshot);

        Assert.Equal(1, snapshot.DocCount);
        Assert.Equal(0, dwpt.DocCount);
        AssertSourceContainsAllFields(source);
        Assert.Contains(snapshot.EnumeratePostings(), static posting => posting.Term == "body\0alpha");

        var postings = new (string Term, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostings(postings);
        Assert.Contains(postings, static posting => posting.Term == "body\0alpha");

        var utf8Postings = new (byte[] TermUtf8, PostingAccumulator Acc)[source.PostingsCount];
        source.CopySortedPostingsUtf8(utf8Postings);
        Assert.Contains(
            utf8Postings,
            static posting => System.Text.Encoding.UTF8.GetString(posting.TermUtf8) == "body\0alpha");

        var pending = new FlushPendingState
        {
            Snapshot = snapshot,
            SegmentOrdinal = 4,
            SeqStart = 10,
            SeqEnd = 10,
            Result = new SegmentInfo { SegmentId = "seg_4", DocCount = 1 },
            Task = Task.CompletedTask
        };

        Assert.Equal(1, pending.DocCount);
        Assert.Equal(4, pending.SegmentOrdinal);
        Assert.Equal(10, pending.SeqStart);
        Assert.Equal(10, pending.SeqEnd);
        Assert.Equal("seg_4", pending.Result!.SegmentId);
        Assert.Same(Task.CompletedTask, pending.Task);
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

    private static DocumentBufferState CreateBuffer()
    {
        var buffer = new DocumentBufferState
        {
            DocCount = 1,
            ParentDocIds = [0]
        };
        buffer.FieldNames.Add("body");
        buffer.DocTokenCounts["body"] = [1];
        buffer.FieldBoosts["body"] = new Dictionary<int, float> { [0] = 1.5f };
        buffer.StoredDocStarts.Add(0);
        buffer.StoredFieldIds.Add(0);
        buffer.StoredFieldValues.Add(StoredFieldValue.FromString("stored"));
        buffer.StoredFieldIdToName.Add("stored");
        buffer.NumericIndex["price"] = new Dictionary<int, double> { [0] = 1.5 };
        buffer.Int64Index["count"] = new Dictionary<int, long> { [0] = 3 };
        buffer.Vectors["embedding"] = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1, 2 }
        };
        buffer.NumericDocValues["price"] = [1.5];
        buffer.Int64DocValues["count"] = [3];
        buffer.SortedDocValues["tag"] = ["alpha"];
        buffer.SortedSetDocValues["tags"] = new Dictionary<int, List<string>>
        {
            [0] = ["alpha", "beta"]
        };
        buffer.SortedNumericDocValues["price"] = new Dictionary<int, List<double>>
        {
            [0] = [1.5]
        };
        buffer.Int64SortedDocValues["count"] = new Dictionary<int, List<long>>
        {
            [0] = [3]
        };
        buffer.BinaryDocValues["payload"] = new Dictionary<int, List<byte[]>>
        {
            [0] = [new byte[] { 1, 2 }]
        };
        buffer.AccumulatePosting("body", "alpha", 0, 0, payload: null, storePayloads: false);
        return buffer;
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
