using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class FuzzySearcherCacheIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public FuzzySearcherCacheIntegrationTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CachedAndBypassedSearchersKeepIndependentPoliciesOnTheSameMultiSegmentIndex()
    {
        var indexPath = Path.Combine(_fixture.Path, "fuzzy-searcher-cache-multisegment");
        using var directory = new MMapDirectory(indexPath);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            AddBody(writer, "government policy");
            writer.Commit();
            AddBody(writer, "governance report");
            writer.Commit();
            AddBody(writer, "garden archive");
            writer.Commit();
        }

        using var cachedSearcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        using var bypassedSearcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false
        });

        Assert.True(cachedSearcher.GetSegmentReaders().Count >= 2);
        Assert.True(bypassedSearcher.GetSegmentReaders().Count >= 2);

        var positiveQuery = new FuzzyQuery("body", "goverment", maxEdits: 1, maxExpansions: 50);
        var expected = cachedSearcher.Search(positiveQuery, 10, TestContext.Current.CancellationToken)
            .ScoreDocs.Select(static hit => hit.DocId).ToArray();
        Assert.NotEmpty(expected);

        var spanQuery = new SpanMultiTermQueryWrapper(positiveQuery);
        var expectedSpan = cachedSearcher.Search(spanQuery, 10, TestContext.Current.CancellationToken)
            .ScoreDocs.Select(static hit => hit.DocId).ToArray();
        Assert.NotEmpty(expectedSpan);
        Assert.Equal(expectedSpan, bypassedSearcher.Search(spanQuery, 10, TestContext.Current.CancellationToken)
            .ScoreDocs.Select(static hit => hit.DocId));

        var noHitQuery = new FuzzyQuery("body", "zzzznomatch", maxEdits: 2, maxExpansions: 50);
        Assert.Equal(0, cachedSearcher.Search(noHitQuery, 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, bypassedSearcher.Search(noHitQuery, 10, TestContext.Current.CancellationToken).TotalHits);

        var observed = new int[64][];
        Parallel.For(0, observed.Length, iteration =>
        {
            var searcher = (iteration & 1) == 0 ? cachedSearcher : bypassedSearcher;
            observed[iteration] = searcher.Search(positiveQuery, 10, TestContext.Current.CancellationToken)
                .ScoreDocs.Select(static hit => hit.DocId).ToArray();
        });

        Assert.All(observed, ids => Assert.Equal(expected, ids));
        Assert.Equal(0, bypassedSearcher.Search(noHitQuery, 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact]
    public void CachedAndBypassedExpansionsKeepTermsAndDistancesForUnicodeAcrossSegments()
    {
        const int maxExpansions = 50;
        const string fieldPrefix = "body\0";
        var indexPath = Path.Combine(_fixture.Path, "fuzzy-searcher-cache-unicode-multisegment");
        using var directory = new MMapDirectory(indexPath);
        var indexedTerms = new[] { "hello", "hallo", "helo", "café", "cafex", "cafexy", "日本語", "日本語x", "日本語xy" };
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            DefaultAnalyser = new KeywordAnalyser(),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            foreach (var term in indexedTerms)
            {
                AddBody(writer, term);
                writer.Commit();
            }
        }

        using var cachedSearcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        using var bypassedSearcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false
        });

        var segmentReaders = cachedSearcher.GetSegmentReaders();
        Assert.True(segmentReaders.Count >= indexedTerms.Length);
        Assert.Equal(indexedTerms.Order(StringComparer.Ordinal),
            segmentReaders.SelectMany(reader => reader.GetAllTermsForField(fieldPrefix))
                .Select(term => term.Term[fieldPrefix.Length..]).Order(StringComparer.Ordinal));

        var cases = new (string Query, int MaxEdits, (string Term, int Distance)[] Expected)[]
        {
            ("hello", 0, [("hello", 0)]),
            ("hallo", 1, [("hallo", 0), ("hello", 1)]),
            ("helo", 2, [("hallo", 2), ("hello", 1), ("helo", 0)]),
            ("café", 0, [("café", 0)]),
            ("cafex", 1, [("cafex", 0), ("cafexy", 1)]),
            ("cafex", 2, [("café", 2), ("cafex", 0), ("cafexy", 1)]),
            ("cafexy", 2, [("cafex", 1), ("cafexy", 0)]),
            ("日本語", 0, [("日本語", 0)]),
            ("日本語x", 1, [("日本語", 1), ("日本語x", 0), ("日本語xy", 1)]),
            ("日本語xy", 2, [("日本語", 2), ("日本語x", 1), ("日本語xy", 0)])
        };

        foreach (var (queryTerm, maxEdits, expected) in cases)
        {
            var cachedMatches = new List<(string Term, int Distance)>();
            var bypassedMatches = new List<(string Term, int Distance)>();
            foreach (var reader in segmentReaders)
            {
                var cached = reader.GetFuzzyMatches(fieldPrefix, queryTerm.AsSpan(), maxEdits, maxExpansions, useCache: true);
                var bypassed = reader.GetFuzzyMatches(fieldPrefix, queryTerm.AsSpan(), maxEdits, maxExpansions, useCache: false);

                Assert.NotSame(cached, bypassed);
                Assert.Same(cached, reader.GetFuzzyMatches(fieldPrefix, queryTerm.AsSpan(), maxEdits, maxExpansions, useCache: true));
                cachedMatches.AddRange(cached.Select(match => (match.Term[fieldPrefix.Length..], match.Distance)));
                bypassedMatches.AddRange(bypassed.Select(match => (match.Term[fieldPrefix.Length..], match.Distance)));
            }

            var expectedOrdered = expected.OrderBy(static match => match.Term, StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedOrdered, cachedMatches.OrderBy(static match => match.Term, StringComparer.Ordinal));
            Assert.Equal(expectedOrdered, bypassedMatches.OrderBy(static match => match.Term, StringComparer.Ordinal));

            var query = new FuzzyQuery("body", queryTerm, maxEdits, maxExpansions);
            var cachedHits = cachedSearcher.Search(query, 20, TestContext.Current.CancellationToken)
                .ScoreDocs.Select(static hit => hit.DocId).Order().ToArray();
            var bypassedHits = bypassedSearcher.Search(query, 20, TestContext.Current.CancellationToken)
                .ScoreDocs.Select(static hit => hit.DocId).Order().ToArray();
            Assert.Equal(cachedHits, bypassedHits);
        }
    }

    private static void AddBody(IndexWriter writer, string body)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        writer.AddDocument(document);
    }
}
