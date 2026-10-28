using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Pins the streaming merge path: managed allocations during a force-merge of large
/// segments must stay bounded, well below the on-disk size of the merged segment.
/// Regression guard against reverting to the buffer-everything-in-RAM approach.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class MergeMemoryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MergeMemoryTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Verifies the Merge: Of Large Segments Allocates Far Less Than Total Doc Bytes scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Of Large Segments Allocates Far Less Than Total Doc Bytes")]
    public void Merge_OfLargeSegments_AllocatesFarLessThanTotalDocBytes()
    {
        const int docsPerSegment = 4_000;
        const int segmentCount = 4;
        const int bodyTokens = 60;

        var dir = SubDir(nameof(Merge_OfLargeSegments_AllocatesFarLessThanTotalDocBytes));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = docsPerSegment,
            MergeThreshold = 1000,
        }))
        {
            int doc = 0;
            for (int s = 0; s < segmentCount; s++)
            {
                for (int i = 0; i < docsPerSegment; i++)
                {
                    var d = new LeanDocument();
                    d.Add(new TextField("id", $"doc{doc}"));
                    d.Add(new TextField("body", string.Join(' ', Enumerable.Range(0, bodyTokens).Select(k => $"tok{(doc + k) % 200}"))));
                    d.Add(new NumericField("rank", doc));
                    writer.AddDocument(d);
                    doc++;
                }
                writer.Commit();
            }
        }

        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(s => int.Parse(s.SegmentId.AsSpan("seg_".Length)))
            .ToList();
        Assert.Equal(segmentCount, sourceSegments.Count);

        long totalSourceBytes = sourceSegments
            .Sum(s => Directory.GetFiles(dir, $"{s.SegmentId}.*").Sum(f => new FileInfo(f).Length));

        int nextOrdinal = sourceSegments.Max(s => int.Parse(s.SegmentId.AsSpan("seg_".Length))) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: segmentCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        merger.MaybeMerge(sourceSegments, ref nextOrdinal);

        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        long delta = allocAfter - allocBefore;
        long totalDocs = (long)segmentCount * docsPerSegment;
        long perDoc = delta / totalDocs;

        // Streaming merge: cumulative per-doc allocation stays small because working
        // buffers (block decoders, ArrayPool rentals) are reused. Use the current
        // thread counter so parallel test allocations do not contaminate the guard.
        Assert.True(perDoc < 32_000,
            $"Merge allocated {delta:N0} bytes ({perDoc:N0}/doc) over {totalDocs} docs and {totalSourceBytes:N0} source bytes. Streaming regression?");
    }

    [Fact(DisplayName = "Merge: Wide Sparse Payload Columns Stay Within Allocation Budget")]
    public void Merge_WideSparsePayloadColumns_StaysWithinAllocationBudgetAndPreservesValues()
    {
        const int documentCount = 1_024;
        const int fieldCount = 64;
        const int segmentCount = 4;
        const int documentsPerSegment = documentCount / segmentCount;
        const long allocationLimitBytes = 210L * 1024 * 1024;

        var dir = SubDir(nameof(Merge_WideSparsePayloadColumns_StaysWithinAllocationBudgetAndPreservesValues));
        using var mmap = new MMapDirectory(dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = documentsPerSegment,
            MergeThreshold = segmentCount + 1,
            BuildHnswOnFlush = false,
            NormaliseVectors = false,
            IndexSort = new IndexSort(SortField.Numeric("sort-key")),
        }))
        {
            for (int documentId = 0; documentId < documentCount; documentId++)
            {
                int fieldId = documentId % fieldCount;
                var document = new LeanDocument();
                document.Add(new StringField("id", documentId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new NumericField("sort-key", (documentId * 37) % documentCount));
                document.Add(new NumericField($"number-{fieldId}", documentId + 0.25));
                document.Add(new Int64Field($"int64-{fieldId}", documentId + 1_000L));
                document.Add(new StringField($"sorted-{fieldId}", $"value-{documentId:D4}"));
                document.Add(new StringField($"set-{fieldId}", $"a-{documentId:D4}"));
                document.Add(new StringField($"set-{fieldId}", $"b-{documentId:D4}"));
                document.Add(new NumericField($"multi-number-{fieldId}", documentId));
                document.Add(new NumericField($"multi-number-{fieldId}", documentId + 0.5));
                document.Add(new Int64Field($"multi-int64-{fieldId}", documentId));
                document.Add(new Int64Field($"multi-int64-{fieldId}", documentId + 1L));
                document.Add(new BinaryField($"binary-{fieldId}",
                    System.Text.Encoding.UTF8.GetBytes($"payload-{documentId:D4}")));
                document.Add(new TextField($"text-{fieldId}", "alpha beta"));

                int vectorFieldId = fieldId % 8;
                document.Add(new VectorField($"vector-{vectorFieldId}",
                    new ReadOnlyMemory<float>([documentId, vectorFieldId, 1f, 0.5f])));
                writer.AddDocument(document);

                if ((documentId + 1) % documentsPerSegment == 0)
                    writer.Commit();
            }

        }

        List<SegmentInfo> sources = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(segmentCount, sources.Count);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int nextOrdinal = sources.Count;
        var merger = new SegmentMerger(mmap, mergeThreshold: segmentCount + 1, softDeleteRetentionSeconds: 0);
        SegmentInfo merged = Assert.IsType<SegmentInfo>(merger.MergeAll(sources, ref nextOrdinal));
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        int[] expectedDocumentIds = Enumerable.Range(0, documentCount)
            .OrderBy(documentId => (documentId * 37) % documentCount)
            .ToArray();
        Assert.Equal(expectedDocumentIds.Length, merged.DocCount);
        Assert.True(allocatedBytes < allocationLimitBytes,
            $"Sparse {fieldCount}-field merge allocated {allocatedBytes:N0} bytes; budget is {allocationLimitBytes:N0} bytes.");

        using var reader = new SegmentReader(mmap, merged);
        for (int mergedDocumentId = 0; mergedDocumentId < expectedDocumentIds.Length; mergedDocumentId++)
        {
            int documentId = expectedDocumentIds[mergedDocumentId];
            int fieldId = documentId % fieldCount;
            Assert.True(reader.TryGetNumericValue($"number-{fieldId}", mergedDocumentId, out double numericValue));
            Assert.Equal(documentId + 0.25, numericValue);
            Assert.True(reader.TryGetInt64Value($"int64-{fieldId}", mergedDocumentId, out long int64Value));
            Assert.Equal(documentId + 1_000L, int64Value);
            Assert.Equal($"value-{documentId:D4}", reader.GetSortedDocValues($"sorted-{fieldId}")![mergedDocumentId]);
            Assert.True(reader.TryGetSortedSetDocValues($"set-{fieldId}", mergedDocumentId, out var sortedSetValues));
            Assert.Equal([$"a-{documentId:D4}", $"b-{documentId:D4}"], sortedSetValues);
            Assert.True(reader.TryGetSortedNumericDocValues($"multi-number-{fieldId}", mergedDocumentId, out var numericValues));
            Assert.Equal([documentId, documentId + 0.5], numericValues);
            Assert.True(reader.TryGetSortedInt64DocValues($"multi-int64-{fieldId}", mergedDocumentId, out var int64Values));
            Assert.Equal([(long)documentId, documentId + 1L], int64Values);
            Assert.True(reader.TryGetBinaryDocValues($"binary-{fieldId}", mergedDocumentId, out var binaryValues));
            Assert.Equal($"payload-{documentId:D4}", System.Text.Encoding.UTF8.GetString(Assert.Single(binaryValues)));
            Assert.Equal(2, reader.GetFieldLength(mergedDocumentId, $"text-{fieldId}"));
            int vectorFieldId = fieldId % 8;
            Assert.Equal<float>([documentId, vectorFieldId, 1f, 0.5f], reader.GetVector($"vector-{vectorFieldId}", mergedDocumentId) ?? []);
        }
    }
}
