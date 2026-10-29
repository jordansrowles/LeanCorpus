using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
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
    public void FuzzyMaxExpansionsAreSelectedGloballyAcrossSegmentsWithUtf8Ties()
    {
        var indexPath = Path.Combine(_fixture.Path, "fuzzy-searcher-global-expansion-cap");
        using var directory = new MMapDirectory(indexPath);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            DefaultAnalyser = new KeywordAnalyser(),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            foreach (string body in new[] { "hat", "bat", "bat", "mat" })
            {
                AddBody(writer, body);
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false
        });
        Assert.Equal(4, searcher.GetSegmentReaders().Count);

        var query = new FuzzyQuery("body", "cat", maxEdits: 1, maxExpansions: 1);
        var context = new FuzzyQueryExecutionContext(searcher.GetSegmentReaders(), useExpansionCache: false);
        Parallel.ForEach(searcher.GetSegmentReaders(), reader => context.GetMatches(query, reader));

        Assert.Equal(1, context.AutomatonCompilationCount);
        Assert.Equal(new[] { "body\0bat" }, context.GetSelectedTerms(query));

        var results = searcher.Search(query, 10,
            TestContext.Current.CancellationToken);
        var bodies = results.ScoreDocs
            .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["body"][0])
            .ToArray();

        Assert.Equal(2, results.TotalHits);
        Assert.All(bodies, static body => Assert.Equal("bat", body));

        Query[] nestedQueries =
        [
            new BooleanQuery.Builder().Add(query, Occur.Must).Build(),
            new SpanMultiTermQueryWrapper(query)
        ];
        foreach (var nestedQuery in nestedQueries)
        {
            var nestedResults = searcher.Search(nestedQuery, 10, TestContext.Current.CancellationToken);
            var nestedBodies = nestedResults.ScoreDocs
                .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["body"][0])
                .ToArray();

            Assert.Equal(2, nestedResults.TotalHits);
            Assert.All(nestedBodies, static body => Assert.Equal("bat", body));
        }
    }

    [Fact]
    public void WarmSegmentExpansionCacheSkipsAutomatonCompilationAcrossSegments()
    {
        var indexPath = Path.Combine(_fixture.Path, "fuzzy-searcher-warm-expansion-cache");
        using var directory = new MMapDirectory(indexPath);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            DefaultAnalyser = new KeywordAnalyser(),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            foreach (string body in new[] { "cat", "bat", "hat" })
            {
                AddBody(writer, body);
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        var readers = searcher.GetSegmentReaders();
        var query = new FuzzyQuery("body", "cat", maxEdits: 1, maxExpansions: 10);
        foreach (var reader in readers)
            reader.GetFuzzyMatches("body\0", query.Term.AsSpan(), query.MaxEdits, query.MaxExpansions, useCache: true);

        var context = new FuzzyQueryExecutionContext(readers, useExpansionCache: true);
        Parallel.ForEach(readers, reader => context.GetMatches(query, reader));

        Assert.Equal(0, context.AutomatonCompilationCount);
        Assert.Equal(new[] { "body\0cat", "body\0bat", "body\0hat" }, context.GetSelectedTerms(query));
    }

    [Fact]
    public void WarmCachedEmptyExpansionPlanSharesOneImmutableNoMatchResultAcrossSegments()
    {
        const string fieldPrefix = "body\0";
        var indexPath = Path.Combine(_fixture.Path, "fuzzy-searcher-warm-empty-expansion");
        using var directory = new MMapDirectory(indexPath);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            DefaultAnalyser = new KeywordAnalyser(),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            foreach (string body in new[] { "government", "market", "policy" })
            {
                AddBody(writer, body);
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        var readers = searcher.GetSegmentReaders();
        Assert.True(readers.Count >= 2);

        var query = new FuzzyQuery("body", "zzzznomatch", maxEdits: 2, maxExpansions: 50);
        foreach (var reader in readers)
            reader.GetFuzzyMatches(fieldPrefix, query.Term.AsSpan(), query.MaxEdits, query.MaxExpansions, useCache: true);

        var context = new FuzzyQueryExecutionContext(readers, useExpansionCache: true);
        var matchesByReader = readers.Select(reader => context.GetMatches(query, reader)).ToArray();

        Assert.All(matchesByReader, Assert.Empty);
        Assert.All(matchesByReader, matches => Assert.Same(matchesByReader[0], matches));
        Assert.Empty(context.GetSelectedTerms(query));
        Assert.Equal(0, context.AutomatonCompilationCount);
    }

    [Fact]
    public void FuzzyExpansionCacheProbeReturnsOnlyExistingEntries()
    {
        const string fieldPrefix = "body\0";
        var indexPath = Path.Combine(_fixture.Path, "fuzzy-searcher-cache-probe");
        using var directory = new MMapDirectory(indexPath);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            DefaultAnalyser = new KeywordAnalyser(),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            foreach (string body in new[] { "government", "market", "policy" })
            {
                AddBody(writer, body);
                writer.Commit();
            }
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        var readers = searcher.GetSegmentReaders();
        var query = new FuzzyQuery("body", "zzzznomatch", maxEdits: 2, maxExpansions: 50);
        var reader = readers[0];

        Assert.False(reader.TryGetCachedFuzzyMatches(
            fieldPrefix, query.Term, query.MaxEdits, query.MaxExpansions, out _));

        var expanded = reader.GetFuzzyMatches(
            fieldPrefix, query.Term.AsSpan(), query.MaxEdits, query.MaxExpansions, useCache: true);
        Assert.Empty(expanded);
        Assert.True(reader.TryGetCachedFuzzyMatches(
            fieldPrefix, query.Term, query.MaxEdits, query.MaxExpansions, out var cached));
        Assert.Same(expanded, cached);
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
