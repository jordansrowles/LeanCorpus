using System.Text.Json;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
public sealed class FieldFeatureIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public FieldFeatureIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Binary Field: Stored Retrieval And Binary DocValues Round-Trip")]
    public void BinaryField_StoredRetrievalAndBinaryDocValues_RoundTrip()
    {
        var dir = new MMapDirectory(SubDir(nameof(BinaryField_StoredRetrievalAndBinaryDocValues_RoundTrip)));

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", "blob-1"));
            doc.Add(new TextField("body", "binary payload"));
            doc.Add(new BinaryField("blob", new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var hit = Assert.Single(searcher.Search(new TermQuery("id", "blob-1"), 10).ScoreDocs);
        var binaryFields = searcher.GetStoredBinaryFields(hit.DocId);

        Assert.Equal(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, binaryFields["blob"][0]);

        var reader = searcher.GetSegmentReaders()[0];
        Assert.True(reader.TryGetBinaryDocValues("blob", 0, out var binaryDocValues));
        Assert.Equal(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, binaryDocValues[0]);
    }

    [Fact(DisplayName = "Binary Field: Stored Reads Return Defensive Copies")]
    public void BinaryField_StoredReads_ReturnDefensiveCopies()
    {
        var dir = new MMapDirectory(SubDir(nameof(BinaryField_StoredReads_ReturnDefensiveCopies)));

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", "blob-1"));
            doc.Add(new BinaryField("blob", new byte[] { 1, 2, 3 }));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var hit = Assert.Single(searcher.Search(new TermQuery("id", "blob-1"), 10).ScoreDocs);
        var first = searcher.GetStoredBinaryFields(hit.DocId)["blob"][0];
        first[0] = 99;

        var second = searcher.GetStoredBinaryFields(hit.DocId)["blob"][0];
        Assert.Equal(new byte[] { 1, 2, 3 }, second);
    }

    [Fact(DisplayName = "Index-Time Boosting: Boosted Document Scores Higher")]
    public void IndexTimeBoosting_BoostedDocumentScoresHigher()
    {
        var dir = new MMapDirectory(SubDir(nameof(IndexTimeBoosting_BoostedDocumentScoresHigher)));

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var boosted = new LeanDocument();
            boosted.Add(new StringField("id", "boosted"));
            boosted.Add(new TextField("body", "alpha", stored: true, boost: 3.0f));
            writer.AddDocument(boosted);

            var regular = new LeanDocument();
            regular.Add(new StringField("id", "regular"));
            regular.Add(new TextField("body", "alpha"));
            writer.AddDocument(regular);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var hits = searcher.Search(new TermQuery("body", "alpha"), 10);

        Assert.Equal(2, hits.TotalHits);
        Assert.Equal("boosted", searcher.GetStoredFields(hits.ScoreDocs[0].DocId)["id"][0]);
        Assert.True(hits.ScoreDocs[0].Score > hits.ScoreDocs[1].Score);
    }

    [Fact(DisplayName = "Payload Term Vectors: Survive Merge And Postings Round-Trip")]
    public void PayloadTermVectors_SurviveMergeAndPostingsRoundTrip()
    {
        var dirPath = SubDir(nameof(PayloadTermVectors_SurviveMergeAndPostingsRoundTrip));
        var dir = new MMapDirectory(dirPath);
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new PayloadAnnotatingAnalyser(),
            StorePayloads = true,
            StoreTermVectors = true,
            MaxBufferedDocs = 1,
            MergeThreshold = 100
        };

        using (var writer = new IndexWriter(dir, config))
        {
            var first = new LeanDocument();
            first.Add(new StringField("id", "payload-a"));
            first.Add(new TextField("body", "blue green"));
            writer.AddDocument(first);

            var second = new LeanDocument();
            second.Add(new StringField("id", "payload-b"));
            second.Add(new TextField("body", "blue amber"));
            writer.AddDocument(second);

            writer.Commit();
        }

        MergeSegmentsForTest(dirPath, dir);

        using var searcher = new IndexSearcher(dir);
        var hit = Assert.Single(searcher.Search(new TermQuery("id", "payload-a"), 10).ScoreDocs);
        var reader = Assert.Single(searcher.GetSegmentReaders());
        int localDocId = hit.DocId - reader.DocBase;

        var termVectors = reader.GetTermVectors(localDocId);
        Assert.NotNull(termVectors);
        var blue = Assert.Single(termVectors!["body"], static entry => entry.Term == "blue");
        Assert.Equal(new byte[] { (byte)'B', (byte)'L', (byte)'U', (byte)'E' }, blue.Payloads![0]);

        using var postings = reader.GetPostingsEnumWithPositions("body\0blue");
        Assert.True(postings.Advance(localDocId));
        _ = postings.GetCurrentPositions();
        var payload = postings.GetPayload(0).ToArray();
        Assert.Equal(new byte[] { (byte)'B', (byte)'L', (byte)'U', (byte)'E' }, payload);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static int SegmentOrdinal(string segmentId)
    {
        Assert.StartsWith("seg_", segmentId);
        return int.Parse(segmentId.AsSpan("seg_".Length));
    }

    private static void MergeSegmentsForTest(string dir, MMapDirectory mmap)
    {
        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        Assert.True(sourceSegments.Count >= 2);

        int nextSegmentOrdinal = sourceSegments.Max(static segment => SegmentOrdinal(segment.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2);
        var mergedSegments = merger.MaybeMerge(sourceSegments, ref nextSegmentOrdinal);
        var mergedSegment = mergedSegments.Single(static candidate => candidate.SegmentId != "seg_0" && candidate.SegmentId != "seg_1");

        int generation = Directory.GetFiles(dir, "segments_*")
            .Select(path => int.TryParse(Path.GetFileName(path).AsSpan("segments_".Length), out int gen) ? gen : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var commitData = JsonSerializer.Serialize(new
        {
            Segments = new[] { mergedSegment.SegmentId },
            Generation = generation
        });
        File.WriteAllText(Path.Combine(dir, $"segments_{generation}"), commitData);

        var activeSegments = new HashSet<string>(mergedSegments.Select(static segment => segment.SegmentId), StringComparer.Ordinal);
        foreach (var segment in sourceSegments)
        {
            if (!activeSegments.Contains(segment.SegmentId))
                merger.CleanupSegmentFiles(segment);
        }
    }

    private sealed class PayloadAnnotatingAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            int offset = 0;
            foreach (var term in input.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int start = input.ToString().IndexOf(term, offset, StringComparison.Ordinal);
                int end = start + term.Length;
                sink.Add(
                    term.ToLowerInvariant().AsSpan(),
                    start,
                    end,
                    positionIncrement: 1,
                    payload: System.Text.Encoding.ASCII.GetBytes(term.ToUpperInvariant()));
                offset = end;
            }
        }
    }
}
