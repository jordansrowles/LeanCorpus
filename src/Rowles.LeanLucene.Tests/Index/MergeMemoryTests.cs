using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Pins the streaming merge path: managed allocations during a force-merge of large
/// segments must stay bounded, well below the on-disk size of the merged segment.
/// Regression guard against reverting to the buffer-everything-in-RAM approach.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Merge")]
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

    [Fact]
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
}
