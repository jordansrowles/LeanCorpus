using System.Globalization;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Covers Boolean execution on unchecked postings cursors under the segment query lease: the
/// unchecked steps return the guarded streams, seeks stay monotonic across block boundaries,
/// absent terms, deletions and segment-state eviction behave, refresh runs concurrently, and
/// results and scores are invariant across segment count and a never-matching SHOULD clause.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class BooleanUncheckedCursorTests : IClassFixture<TestDirectoryFixture>
{
    // Posting lists for "alpha" and "beta" span several 128-document blocks.
    private const int DocumentCount = 1_500;
    private static readonly string[] Vocabulary = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta"];

    private readonly TestDirectoryFixture _fixture;

    public BooleanUncheckedCursorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Unchecked Cursor: Steps Return The Guarded Document And Frequency Streams")]
    public void UncheckedCursor_MatchesGuardedStreams()
    {
        using var directory = BuildIndex("unchecked_streams", segments: 3);
        using var searcher = new IndexSearcher(directory);
        var random = new Random(4021);

        foreach (var reader in searcher.GetSegmentReaders())
        foreach (string term in Vocabulary.Append("absent"))
        {
            string qualified = string.Concat("body", "\x00", term);

            // Sequential stream.
            using (var guarded = reader.GetPostingsEnum(qualified))
            using (var unchecked_ = reader.GetPostingsEnum(qualified))
            {
                while (true)
                {
                    bool guardedHas = guarded.MoveNext();
                    bool uncheckedHas = unchecked_.MoveNextUnchecked(out int docId, out int frequency);
                    Assert.Equal(guardedHas, uncheckedHas);
                    if (!guardedHas) break;
                    Assert.Equal(guarded.DocId, docId);
                    Assert.Equal(guarded.Freq, frequency);
                }
            }

            // Random forward seeks interleaved with steps.
            using (var guarded = reader.GetPostingsEnum(qualified))
            using (var unchecked_ = reader.GetPostingsEnum(qualified))
            {
                int target = 0;
                while (target <= reader.MaxDoc + 1)
                {
                    bool step = random.Next(3) == 0;
                    bool guardedHas = step ? guarded.MoveNext() : guarded.Advance(target);
                    int docId, frequency;
                    bool uncheckedHas = step
                        ? unchecked_.MoveNextUnchecked(out docId, out frequency)
                        : unchecked_.AdvanceUnchecked(target, out docId, out frequency);
                    Assert.Equal(guardedHas, uncheckedHas);
                    if (!guardedHas) break;
                    Assert.Equal(guarded.DocId, docId);
                    Assert.Equal(guarded.Freq, frequency);
                    target = docId + random.Next(1, 300);
                }
            }
        }
    }

    [Fact(DisplayName = "Unchecked Cursor: Advance Is Monotonic At Block Boundaries")]
    public void UncheckedCursor_AdvanceIsMonotonicAtBlockBoundaries()
    {
        using var directory = BuildIndex("unchecked_block_boundaries", segments: 1);
        using var searcher = new IndexSearcher(directory);
        var reader = Assert.Single(searcher.GetSegmentReaders());
        string qualified = string.Concat("body", "\x00", "alpha");

        int[] all = ReadAll(reader, qualified);
        Assert.True(all.Length > 3 * 128, "alpha must span at least three postings blocks");

        // Targets on, just before and just after each block's first and last documents.
        var targets = new SortedSet<int>();
        for (int block = 0; block * 128 < all.Length; block++)
        {
            int first = all[block * 128];
            int last = all[Math.Min(all.Length - 1, (block * 128) + 127)];
            foreach (int doc in new[] { first - 1, first, first + 1, last - 1, last, last + 1 })
                targets.Add(Math.Max(0, doc));
        }
        targets.Add(all[^1] + 1);

        using var postings = reader.GetPostingsEnum(qualified);
        int previous = -1;
        foreach (int target in targets)
        {
            int expectedIndex = Array.FindIndex(all, doc => doc >= target);
            bool found = postings.AdvanceUnchecked(target, out int docId, out _);
            if (expectedIndex < 0)
            {
                Assert.False(found);
                break;
            }
            Assert.True(found, $"target {target}");
            Assert.Equal(all[expectedIndex], docId);
            Assert.True(docId >= previous, $"seek to {target} moved backwards from {previous} to {docId}");
            previous = docId;
        }
    }

    [Fact(DisplayName = "Boolean: Absent Terms Follow Their Occur Semantics")]
    public void Boolean_AbsentTerms_FollowOccurSemantics()
    {
        using var directory = BuildIndex("unchecked_absent", segments: 3);
        using var searcher = new IndexSearcher(directory);

        Assert.Equal(0, Search(searcher, ("alpha", Occur.Must), ("absent", Occur.Must)).TotalHits);
        Assert.Equal(Hits(searcher, [("alpha", Occur.Must)]), Hits(searcher, [("alpha", Occur.Must), ("absent", Occur.MustNot)]));
        Assert.Equal(Hits(searcher, [("beta", Occur.Should)]), Hits(searcher, [("beta", Occur.Should), ("absent", Occur.Should)]));
        Assert.Equal(0, Search(searcher, ("absent", Occur.Should), ("missing", Occur.Should)).TotalHits);
    }

    [Fact(DisplayName = "Boolean: Deleted Documents Never Match")]
    public void Boolean_DeletedDocuments_NeverMatch()
    {
        string path = NewPath("unchecked_deletes");
        using var directory = new MMapDirectory(path);
        WriteDocuments(directory, segments: 4);
        var deleted = new HashSet<int>(Enumerable.Range(0, DocumentCount).Where(i => i % 7 == 3 || i % 11 == 0));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            foreach (int id in deleted)
                writer.DeleteDocuments(new TermQuery("id", Id(id)));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        foreach (var clauses in Shapes())
        {
            var results = Search(searcher, clauses);
            var expected = Enumerable.Range(0, DocumentCount)
                .Where(i => !deleted.Contains(i) && Matches(Terms(i), clauses))
                .Select(Id).Order(StringComparer.Ordinal).ToArray();
            var actual = results.ScoreDocs.Select(hit => StoredId(searcher, hit.DocId)).Order(StringComparer.Ordinal).ToArray();
            Assert.True(expected.SequenceEqual(actual), $"shape {Describe(clauses)}");
            Assert.Equal(expected.Length, results.TotalHits);
        }
    }

    [Fact(DisplayName = "Boolean: Evicted Segment States Are Not Used After Release")]
    public void Boolean_EvictedSegmentStates_AreNotUsedAfterRelease()
    {
        using var directory = BuildIndex("unchecked_eviction", segments: 12);
        using var reference = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaders = 16 });
        using var evicting = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaders = 1 });

        foreach (var clauses in Shapes())
        {
            // Each segment's state is released, and evicted, before the next segment runs.
            // A cursor read after its state was disposed would fail or return different hits.
            AssertSameHitsAndScores(reference, evicting, clauses);
            Assert.True(evicting.SegmentReaderCacheMetrics.EntryCount <= 1);
        }
        Assert.True(evicting.SegmentReaderCacheMetrics.EvictionCount > 0);

        evicting.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Search(evicting, ("alpha", Occur.Must), ("beta", Occur.Must)));
    }

    [Fact(DisplayName = "Boolean: Queries Stay Correct During Concurrent Refresh")]
    public async Task Boolean_QueriesStayCorrect_DuringConcurrentRefresh()
    {
        string path = NewPath("unchecked_refresh");
        using var directory = new MMapDirectory(path);
        WriteDocuments(directory, segments: 3);
        int baseline;
        using (var searcher = new IndexSearcher(directory))
            baseline = Search(searcher, ("alpha", Occur.Must), ("beta", Occur.Must)).TotalHits;

        using var manager = new SearcherManager(directory);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int added = 0;

        var writerTask = Task.Run(() =>
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 16, MergePolicy = NoMergePolicy.Instance });
            for (int round = 0; round < 20; round++)
            {
                for (int i = 0; i < 16; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", "extra" + round.ToString(CultureInfo.InvariantCulture) + "_" + i.ToString(CultureInfo.InvariantCulture), stored: true));
                    document.Add(new TextField("body", "alpha beta gamma"));
                    writer.AddDocument(document);
                }
                writer.Commit();
                Volatile.Write(ref added, (round + 1) * 16);
                manager.MaybeRefresh();
            }
        }, TestContext.Current.CancellationToken);

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!writerTask.IsCompleted)
            {
                using var lease = manager.AcquireLease();
                int hits = Search(lease.Searcher, ("alpha", Occur.Must), ("beta", Occur.Must)).TotalHits;
                // A snapshot sees the original documents plus whole committed batches of 16.
                Assert.InRange(hits, baseline, baseline + Volatile.Read(ref added) + 16);
                Assert.Equal(0, (hits - baseline) % 16);
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await writerTask;
        await Task.WhenAll(readers);
        manager.MaybeRefresh();
        using var final = manager.AcquireLease();
        Assert.Equal(baseline + (20 * 16), Search(final.Searcher, ("alpha", Occur.Must), ("beta", Occur.Must)).TotalHits);
    }

    [Fact(DisplayName = "Boolean: Results And Scores Are Invariant Across Segment Count")]
    public void Boolean_ResultsAndScores_InvariantAcrossSegmentCount()
    {
        using var one = BuildIndex("unchecked_segments_one", segments: 1);
        using var many = BuildIndex("unchecked_segments_many", segments: 9);
        using var single = new IndexSearcher(one);
        using var segmented = new IndexSearcher(many);

        foreach (var clauses in Shapes())
            AssertSameHitsAndScores(single, segmented, clauses);
    }

    [Fact(DisplayName = "Boolean: A Never-Matching Should Clause Changes Nothing")]
    public void Boolean_NeverMatchingShould_ChangesNothing()
    {
        using var directory = BuildIndex("unchecked_never_matching", segments: 4);
        using var searcher = new IndexSearcher(directory);

        foreach (var clauses in Shapes())
        {
            var extended = clauses.Append(("neverpresent", Occur.Should)).ToArray();
            Assert.Equal(Hits(searcher, clauses), Hits(searcher, extended));
        }
    }

    private static IEnumerable<(string Term, Occur Occur)[]> Shapes() =>
    [
        [("alpha", Occur.Must), ("beta", Occur.Must)],
        [("alpha", Occur.Must), ("gamma", Occur.Must), ("delta", Occur.Must)],
        [("alpha", Occur.Must), ("gamma", Occur.MustNot)],
        [("alpha", Occur.Must), ("delta", Occur.Should), ("epsilon", Occur.Should)],
        [("beta", Occur.Should), ("gamma", Occur.Should)],
        [("alpha", Occur.Should), ("gamma", Occur.Should), ("delta", Occur.Should), ("zeta", Occur.Should)],
        [("beta", Occur.Should), ("epsilon", Occur.Should), ("zeta", Occur.MustNot)],
        // More than 64 SHOULD clauses, which takes the heap union path.
        Vocabulary.Skip(2)
            .Concat(Enumerable.Range(0, 40).Select(static i => "filler" + i.ToString(CultureInfo.InvariantCulture)))
            .Concat(Enumerable.Range(0, 24).Select(static i => "unused" + i.ToString(CultureInfo.InvariantCulture)))
            .Select(static term => (term, Occur.Should))
            .Append(("alpha", Occur.MustNot))
            .ToArray()
    ];

    // Deterministic per-document term choice: alpha and beta are common, the rest rarer, with
    // repeated occurrences so frequencies vary.
    private static string[] Terms(int i)
    {
        var random = new Random(i * 7919 + 17);
        var terms = new List<string>();
        int[] weights = [70, 55, 30, 18, 12, 6];
        for (int t = 0; t < Vocabulary.Length; t++)
        {
            if (random.Next(100) < weights[t])
            {
                int repeats = 1 + random.Next(3);
                for (int r = 0; r < repeats; r++)
                    terms.Add(Vocabulary[t]);
            }
        }
        int filler = random.Next(6);
        for (int f = 0; f < filler; f++)
            terms.Add("filler" + random.Next(40).ToString(CultureInfo.InvariantCulture));
        return terms.OrderBy(_ => random.Next()).ToArray();
    }

    private static bool Matches(string[] terms, (string Term, Occur Occur)[] clauses)
    {
        bool hasMust = clauses.Any(c => c.Occur == Occur.Must);
        int shouldMatches = clauses.Count(c => c.Occur == Occur.Should && terms.Contains(c.Term));
        return clauses.Where(c => c.Occur == Occur.Must).All(c => terms.Contains(c.Term))
               && clauses.Where(c => c.Occur == Occur.MustNot).All(c => !terms.Contains(c.Term))
               && (hasMust || shouldMatches > 0);
    }

    private MMapDirectory BuildIndex(string name, int segments)
    {
        var directory = new MMapDirectory(NewPath(name));
        WriteDocuments(directory, segments);
        return directory;
    }

    private static void WriteDocuments(MMapDirectory directory, int segments)
    {
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(DocumentCount / (double)segments),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", Id(i), stored: true));
            document.Add(new TextField("body", string.Join(' ', Terms(i))));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    private string NewPath(string name)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int[] ReadAll(SegmentReader reader, string qualified)
    {
        var docs = new List<int>();
        using var postings = reader.GetPostingsEnum(qualified);
        while (postings.MoveNext(out int docId, out _))
            docs.Add(docId);
        return docs.ToArray();
    }

    private static TopDocs Search(IndexSearcher searcher, params (string Term, Occur Occur)[] clauses)
    {
        var builder = new BooleanQuery.Builder();
        foreach (var (term, occur) in clauses)
            builder.Add(new TermQuery("body", term), occur);
        return searcher.Search(builder.Build(), DocumentCount, TestContext.Current.CancellationToken);
    }

    // Compares by stored identifier, so segment layout does not affect the comparison.
    private static void AssertSameHitsAndScores(IndexSearcher expected, IndexSearcher actual, (string Term, Occur Occur)[] clauses)
    {
        var expectedHits = Hits(expected, clauses);
        var actualHits = Hits(actual, clauses);
        Assert.True(expectedHits.Count > 0, $"{Describe(clauses)} should match");
        Assert.Equal(expectedHits, actualHits);
    }

    private static SortedDictionary<string, float> Hits(IndexSearcher searcher, (string Term, Occur Occur)[] clauses)
    {
        var results = Search(searcher, clauses);
        Assert.Equal(results.TotalHits, results.ScoreDocs.Length);
        var hits = new SortedDictionary<string, float>(StringComparer.Ordinal);
        foreach (var hit in results.ScoreDocs)
            hits.Add(StoredId(searcher, hit.DocId), hit.Score);
        return hits;
    }

    private static string StoredId(IndexSearcher searcher, int docId) => searcher.GetStoredFields(docId)["id"][0];

    private static string Id(int i) => i.ToString(CultureInfo.InvariantCulture);

    private static string Describe((string Term, Occur Occur)[] clauses)
        => string.Join(' ', clauses.Select(c => c.Occur + ":" + c.Term));
}
