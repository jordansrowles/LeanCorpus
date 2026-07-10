using Xunit;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public class IndexSmokeFixture : IDisposable
{
    public string RootPath { get; }

    public IndexSmokeFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"lc-aot-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(RootPath, recursive: true); }
        catch { /* best effort */ }
    }
}

public class IndexSmokeTests : IClassFixture<IndexSmokeFixture>
{
    private readonly IndexSmokeFixture _fixture;

    public IndexSmokeTests(IndexSmokeFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(FieldCompressionPolicy.None)]
    [InlineData(FieldCompressionPolicy.Deflate)]
    [InlineData(FieldCompressionPolicy.Brotli)]
    public void RunPolicy(FieldCompressionPolicy policy)
    {
        var rootPath = _fixture.RootPath;
        var indexPath = Path.Combine(rootPath, policy.ToString());

        Directory.CreateDirectory(indexPath);
        using var directory = new MMapDirectory(indexPath);

        // --- Indexing ---
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            CompressionPolicy = policy,
            MaxBufferedDocs = 2,
            RamBufferSizeMB = 1,
            SoftDeletesEnabled = true,
            StoreTermVectors = true,
        }))
        {
            foreach (var document in BuildDocuments())
                writer.AddDocument(document);
            writer.Commit();

            // Soft-delete then hard-commit to exercise deletion path
            writer.SoftDeleteDocuments(new TermQuery("status", "archived"));
            writer.Commit();

            // Re-add a document
            writer.AddDocument(Document(
                id: "doc-4b",
                title: "recovered archive",
                body: "re-added after deletion",
                code: "recover",
                status: "archived",
                category: "archive",
                year: 2021,
                latitude: 48.8566,
                longitude: 2.3522,
                embedding: [0f, 0f, 0.9f, 0f]));
            writer.Commit();
        }

        // Re-open for search
        var analyticsPath = Path.Combine(indexPath, "search-analytics.json");
        var slowLogPath = Path.Combine(indexPath, "slow-queries.jsonl");
        var analytics = new SearchAnalytics(capacity: 64);

        using (var slowLog = SlowQueryLog.ToFile(thresholdMs: 0, slowLogPath))
        {
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
            {
                SearchAnalytics = analytics,
                SlowQueryLog = slowLog,
            });

            // --- Core queries ---
            Assert.Equal(3, searcher.Search(new TermQuery("status", "active"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, searcher.Search(new PhraseQuery("title", "native", "aot"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, searcher.Search(new PrefixQuery("title", "nat"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, searcher.Search(new RegexpQuery("code", "^nat"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(2, searcher.Search(new RangeQuery("year", 2024, 2026), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(2, searcher.Search(new GeoDistanceQuery("location", 51.5074, -0.1278, 250_000), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(2, searcher.Search(new GeoBoundingBoxQuery("location", 51.0, 52.0, -3.0, 0.5), 10, TestContext.Current.CancellationToken).TotalHits);
            var vectorResults = searcher.Search(new VectorQuery("embedding", [1f, 0f, 0f, 0f], topK: 2), 2, TestContext.Current.CancellationToken);
            Assert.Equal(2, vectorResults.ScoreDocs.Length);

            Assert.Equal(1, searcher.Search(new WildcardQuery("title", "corp*"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, searcher.Search(new FuzzyQuery("title", "nativ", maxEdits: 1), 10, TestContext.Current.CancellationToken).TotalHits);

            {
                var bq = new BooleanQuery.Builder()
                    .Add(new TermQuery("status", "active"), Occur.Must)
                    .Add(new TermQuery("status", "archived"), Occur.MustNot)
                    .Build();
                Assert.Equal(3, searcher.Search(bq, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var bq = new BooleanQuery.Builder()
                    .Add(new TermQuery("status", "active"), Occur.Should)
                    .Add(new TermQuery("status", "archived"), Occur.Should)
                    .Build();
                Assert.Equal(4, searcher.Search(bq, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var parser = new QueryParser("title", new StandardAnalyser());
                var parsed = parser.Parse("native aot");
                var result = searcher.Search(parsed, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits >= 1,
                    $"query parser 'native aot' returned {result.TotalHits} hit(s), expected >=1.");
            }

            {
                var parser = new QueryParser("title", new StandardAnalyser());
                var parsed = parser.Parse("status:active");
                var result = searcher.Search(parsed, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits >= 3,
                    $"query parser 'status:active' returned {result.TotalHits} hit(s), expected >=3.");
            }

            {
                var parser = new QueryParser("title", new StandardAnalyser());
                var parsed = parser.Parse("\"native aot\"");
                var result = searcher.Search(parsed, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits >= 1,
                    $"query parser '\"native aot\"' returned {result.TotalHits} hit(s), expected >=1.");
            }

            {
                var q = new TermInSetQuery("status", ["active", "archived"]);
                Assert.Equal(4, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new MatchAllDocsQuery();
                Assert.Equal(4, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var inner = new TermQuery("status", "active");
                var q = new BlockJoinQuery(inner);
                Assert.True(q.ChildQuery.Equals(inner), "BlockJoinQuery ChildQuery mismatch");
                Assert.True(q.Field == inner.Field, "BlockJoinQuery Field mismatch");
            }

            {
                var q = new CombinedFieldsQuery(
                    fields: new[] { "title", "body" },
                    terms: new[] { "native", "aot" });
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new CombinedFieldsQuery(
                    fields: new[] { "title", "body" },
                    terms: new[] { "native", "nonexistent" },
                    minimumShouldMatch: 2);
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits == 0,
                    $"combined fields minShouldMatch=2 expected 0 hits, got {result.TotalHits}");
            }

            {
                var inner = new TermQuery("status", "active");
                var q = new ConstantScoreQuery(inner);
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits == 3,
                    $"constant score query expected 3 hits, got {result.TotalHits}");
                Assert.True(result.ScoreDocs.All(sd => sd.Score == 1.0f),
                    "constant score query expected all scores == 1.0");
            }

            {
                var inner = new TermQuery("status", "active");
                var q = new ConstantScoreQuery(inner, score: 2.5f);
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits == 3,
                    $"constant score (2.5) query expected 3 hits, got {result.TotalHits}");
                Assert.True(result.ScoreDocs.All(sd => sd.Score == 2.5f),
                    "constant score (2.5) query expected all scores == 2.5");
            }

            {
                var q = new DisjunctionMaxQuery(tieBreakerMultiplier: 0.1f)
                    .Add(new TermQuery("title", "native"))
                    .Add(new TermQuery("title", "fast"))
                    .Freeze();
                Assert.Equal(2, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new DisjunctionMaxQuery.Builder()
                    .WithTieBreakerMultiplier(0.5f)
                    .Add(new TermQuery("title", "native"))
                    .Add(new TermQuery("title", "corpus"))
                    .Build();
                Assert.Equal(2, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new FieldExistsQuery("status");
                Assert.Equal(4, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new FieldExistsQuery("nonexistent_field");
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits == 0,
                    $"field exists (nonexistent) expected 0 hits, got {result.TotalHits}");
            }

            {
                var inner = new TermQuery("status", "active");
                var q = new FunctionScoreQuery(inner, "year", ScoreMode.Multiply);
                Assert.Equal(3, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var inner = new TermQuery("status", "active");
                var q = new FunctionScoreQuery(inner, "year", ScoreMode.Replace);
                Assert.Equal(3, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var inner = new TermQuery("status", "active");
                var q = new FunctionScoreQuery(inner, "year", ScoreMode.Sum);
                Assert.Equal(3, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var source = new IntervalsTermSource("title", "native");
                var q = new IntervalsQuery(source);
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var source = new IntervalsPhraseSource("title", "fast", "corpus");
                var q = new IntervalsQuery(source);
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var source = new IntervalsOrSource(
                    new IntervalsTermSource("title", "native"),
                    new IntervalsTermSource("title", "fast"));
                var q = new IntervalsQuery(source);
                Assert.Equal(2, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var source = new IntervalsOrderedSource(
                    maxGaps: 2,
                    new IntervalsTermSource("title", "native"),
                    new IntervalsTermSource("title", "aot"));
                var q = new IntervalsQuery(source);
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new MatchNoDocsQuery();
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits == 0,
                    $"match no docs expected 0 hits, got {result.TotalHits}");
            }

            {
                var q = new MatchNoDocsQuery("test reason");
                Assert.True(q.Reason == "test reason", "MatchNoDocsQuery Reason mismatch");
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits == 0,
                    $"match no docs (reason) expected 0 hits, got {result.TotalHits}");
            }

            {
                var q = new MoreLikeThisQuery(
                    docId: 0,
                    fields: new[] { "title", "body" });
                Assert.True(q.Fields.Length == 2, "MoreLikeThisQuery Fields length mismatch");
                Assert.True(q.Parameters is not null, "MoreLikeThisQuery Parameters should not be null");
            }

            {
                var parameters = new MoreLikeThisParameters
                {
                    MaxQueryTerms = 10,
                    MinTermFreq = 1,
                };
                var q = new MoreLikeThisQuery(
                    docId: 0,
                    fields: new[] { "title" },
                    parameters: parameters);
                Assert.True(q.Parameters.MaxQueryTerms == 10,
                    "MoreLikeThisQuery custom MaxQueryTerms mismatch");
            }

            {
                var q = new MultiPhraseQuery(
                    field: "title",
                    termGroups: new[] { new[] { "native" }, new[] { "aot", "search" } });
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new MultiPhraseQuery(
                    field: "title",
                    termGroups: new[] { new[] { "native" }, new[] { "search" } },
                    slop: 1);
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new PointInSetQuery("year", 2024, 2025);
                Assert.Equal(2, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new PointInSetQuery("year", new[] { 2023.0 });
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new RrfQuery(k: 60)
                    .Add(new TermQuery("title", "native"))
                    .Add(new TermQuery("title", "fast"));
                Assert.Equal(2, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var setA = new TopDocs(2, new[]
                {
                    new ScoreDoc(0, 2.0f),
                    new ScoreDoc(1, 1.0f),
                });
                var setB = new TopDocs(2, new[]
                {
                    new ScoreDoc(1, 3.0f),
                    new ScoreDoc(2, 0.5f),
                });
                var fused = RrfQuery.Combine(new[] { setA, setB }, topN: 3, k: 60);
                Assert.True(fused.TotalHits == 3,
                    $"RRF Combine expected 3 total, got {fused.TotalHits}");
                Assert.True(fused.ScoreDocs.Length <= 3,
                    $"RRF Combine expected <=3 results, got {fused.ScoreDocs.Length}");
            }

            {
                var q = new TermRangeQuery("code", "api", "native",
                    includeLower: true, includeUpper: true);
                Assert.Equal(3, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new TermRangeQuery("code", "api", "recover",
                    includeLower: false, includeUpper: true);
                Assert.Equal(3, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new TermRangeQuery("code", "api", null,
                    includeLower: true, includeUpper: true);
                Assert.Equal(4, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new SpanTermQuery("title", "native");
                Assert.True(q.Field == "title", "SpanTermQuery Field mismatch");
                Assert.True(q.Term == "native", "SpanTermQuery Term mismatch");
            }

            {
                var q = new SpanNearQuery(
                    new SpanQuery[]
                    {
                        new SpanTermQuery("title", "native"),
                        new SpanTermQuery("title", "aot"),
                    },
                    slop: 2,
                    inOrder: true);
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new SpanNearQuery(
                    new SpanQuery[]
                    {
                        new SpanTermQuery("title", "aot"),
                        new SpanTermQuery("title", "native"),
                    },
                    slop: 2,
                    inOrder: false);
                Assert.Equal(1, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var q = new SpanOrQuery(
                    new SpanTermQuery("title", "native"),
                    new SpanTermQuery("title", "fast"));
                Assert.Equal(2, searcher.Search(q, 10, TestContext.Current.CancellationToken).TotalHits);
            }

            {
                var include = new SpanTermQuery("title", "corpus");
                var exclude = new SpanTermQuery("title", "native");
                var q = new SpanNotQuery(include, exclude);
                var result = searcher.Search(q, 10, TestContext.Current.CancellationToken);
                Assert.True(result.TotalHits >= 1,
                    $"span not query expected >=1 hit, got {result.TotalHits}");
            }

            // --- Facets ---
            {
                var (results, facets) = searcher.SearchWithFacets(
                    new MatchAllDocsQuery(), 10, "category", "status");
                Assert.True(facets.Count == 2,
                    $"facets expected 2 facet results, got {facets.Count}");
                Assert.True(facets[0].FieldName is not null,
                    "facet FieldName should not be null");
                Assert.True(facets[0].Buckets.Count >= 1,
                    $"facet buckets expected >=1, got {facets[0].Buckets.Count}");
            }

            {
                var sorted = searcher.Search(new TermQuery("status", "active"), 10,
                    SortField.Numeric("year", descending: true));
                Assert.True(sorted.ScoreDocs.Length >= 1,
                    $"numeric sort returned {sorted.ScoreDocs.Length} doc(s), expected >=1.");
            }

            {
                var sorted = searcher.Search(new MatchAllDocsQuery(), 10, SortField.DocId, TestContext.Current.CancellationToken);
                Assert.True(sorted.ScoreDocs.Length >= 1,
                    $"docid sort returned {sorted.ScoreDocs.Length} doc(s), expected >=1.");
            }

            {
                var sorted = searcher.Search(new MatchAllDocsQuery(), 10, SortField.Score, TestContext.Current.CancellationToken);
                Assert.True(sorted.ScoreDocs.Length >= 1,
                    $"score sort returned {sorted.ScoreDocs.Length} doc(s), expected >=1.");
            }

            // --- Collapse search ---
            var collapsed = searcher.SearchWithCollapse(
                new TermQuery("status", "active"), 10, new CollapseField("category"));
            Assert.True(collapsed.TotalHits == 2,
                $"collapsed search returned {collapsed.TotalHits} group(s), expected 2.");

            // --- Stored fields ---
            var storedResult = searcher.Search(new TermQuery("id", "doc-1"), 1, TestContext.Current.CancellationToken);
            Assert.Equal(1, storedResult.TotalHits);
            var stored = searcher.GetStoredFields(storedResult.ScoreDocs[0].DocId);
            Assert.True(stored.TryGetValue("title", out var title) && title.Contains("native aot search"),
                "stored field 'title' was not readable after reopening the index.");
            Assert.True(stored.TryGetValue("location", out var location) && location.Contains("51.5074,-0.1278"),
                "stored geo field was not readable after reopening the index.");

            {
                var highlighter = new Highlighter("<b>", "</b>");
                var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "native", "aot" };
                var snippet = highlighter.GetBestFragment(
                    "LeanCorpus supports native AOT publishing for compact binaries.",
                    terms,
                    maxSnippetLength: 200);
                Assert.True(snippet.Contains("<b>") || snippet.Contains("native"),
                    $"Highlighter snippet doesn't contain expected content: '{snippet}'");
            }

            // --- LINQ queryable ---
            var resolver = (string name) => name switch
            {
                "Title"       => (IFieldDescriptor?)NativeSmokeFields.Title,
                "Year"        => NativeSmokeFields.Year,
                "Status"      => NativeSmokeFields.Status,
                "IsPublished" => NativeSmokeFields.IsPublished,
                _ => null,
            };
            var map = new NativeSmokeDocMap();
            var queryable = map.AsQueryable(searcher, resolver);

            var whereAnd = queryable.Where(d => d.Status == "active" && d.Year > 2023).ToList();
            Assert.True(whereAnd.Count == 2, $"Where && returned {whereAnd.Count}, expected 2.");

            var whereOr = queryable.Where(d => d.Status == "archived" || d.Status == "active").ToList();
            Assert.True(whereOr.Count == 4, $"Where || returned {whereOr.Count}, expected 4.");

            var first = queryable.Where(d => d.Status == "archived").First();
            Assert.True(first.Year == 2021, $"First returned year {first.Year}, expected 2021.");
            var firstDef = queryable.Where(d => d.Status == "nonexistent").FirstOrDefault();
            Assert.True(firstDef is null, "FirstOrDefault should return null for empty results.");

            var single = queryable.Where(d => d.Status == "archived").Single();
            Assert.True(single.Year == 2021, $"Single returned year {single.Year}, expected 2021.");
            var singleDef = queryable.Where(d => d.Status == "nonexistent").SingleOrDefault();
            Assert.True(singleDef is null, "SingleOrDefault should return null for empty results.");

            var count = queryable.Where(d => d.Status == "active").Count();
            Assert.True(count == 3, $"Count returned {count}, expected 3.");
            var countAll = queryable.Count();
            Assert.True(countAll == 4, $"Count (all) returned {countAll}, expected 4.");
            Assert.True(queryable.Where(d => d.Status == "active").Any(), "Any should return true.");
            Assert.True(!queryable.Where(d => d.Status == "nonexistent").Any(), "Any should return false for no matches.");

            var take = queryable.Where(d => d.Status == "active").Take(2).ToList();
            Assert.True(take.Count == 2, $"Take(2) returned {take.Count}, expected 2.");
            var skip = queryable.Where(d => d.Status == "active").Skip(1).ToList();
            Assert.True(skip.Count == 2, $"Skip(1) returned {skip.Count}, expected 2.");

            var titles = queryable.Where(d => d.Year >= 2024).Select(d => d.Title).ToList();
            Assert.True(titles.Count == 2, $"Select projection returned {titles.Count}, expected 2.");

            var projectedTake = queryable.Where(d => d.Status == "active").Select(d => d.Title).Take(1).ToList();
            Assert.True(projectedTake.Count == 1, $"Select+Take returned {projectedTake.Count}, expected 1.");

            var projectedSkip = queryable.Where(d => d.Status == "active").Select(d => d.Title).Skip(1).ToList();
            Assert.True(projectedSkip.Count == 2, $"Select+Skip returned {projectedSkip.Count}, expected 2.");

            var published = queryable.Where(d => d.IsPublished).ToList();
            Assert.True(published.Count == 4, $"IsPublished returned {published.Count}, expected 4.");

            var starts = queryable.Where(d => d.Title!.StartsWith("native")).ToList();
            Assert.True(starts.Count == 1, $"StartsWith returned {starts.Count}, expected 1.");
            var ends = queryable.Where(d => d.Title!.EndsWith("indexing")).ToList();
            Assert.True(ends.Count == 1, $"EndsWith returned {ends.Count}, expected 1.");

            var captured = queryable.Where(d => d.Status == "active");
            var recaptured = captured.Where(d => d.Year > 2023).ToList();
            Assert.True(recaptured.Count == 2, $"Captured+Where returned {recaptured.Count}, expected 2.");

            var ordered = queryable.Where(d => d.Status == "active").OrderBy(d => d.Year).ToList();
            Assert.True(ordered.Count == 3, $"OrderBy returned {ordered.Count}, expected 3.");
            Assert.True(ordered[0].Year == 2023, $"OrderBy first year {ordered[0].Year}, expected 2023.");
            Assert.True(ordered[2].Year == 2025, $"OrderBy last year {ordered[2].Year}, expected 2025.");
            var orderedDesc = queryable.Where(d => d.Status == "active").OrderByDescending(d => d.Year).ToList();
            Assert.True(orderedDesc[0].Year == 2025, $"OrderByDesc first year {orderedDesc[0].Year}, expected 2025.");

            var statuses = new[] { "active", "archived" };
            var inSet = queryable.Where(d => statuses.Contains(d.Status!)).ToList();
            Assert.True(inSet.Count == 4, $"TermInSet returned {inSet.Count}, expected 4.");

            var projectedThenWhere = queryable
                .Where(d => d.Title!.StartsWith("native"))
                .Select(d => d.Title)
                .ToList();
            Assert.True(projectedThenWhere.Count == 1,
                $"Where+Select returned {projectedThenWhere.Count}, expected 1.");
        }

        // --- Index backup ---
        {
            var backupPath = Path.Combine(rootPath, "backup-" + policy);
            Directory.CreateDirectory(backupPath);
            var manifest = IndexBackup.CreateManifest(indexPath);
            Assert.True(manifest is not null, "IndexBackup.CreateManifest returned null");
            Assert.True(manifest!.Files.Count >= 1,
                $"backup manifest has {manifest.Files.Count} files, expected >=1.");
        }

        // --- Analytics export ---
        using (var analyticsWriter = File.CreateText(analyticsPath))
            analytics.ExportJson(analyticsWriter);

        Assert.True(new FileInfo(analyticsPath).Length > 2, "search analytics JSON was not written.");
        Assert.True(new FileInfo(slowLogPath).Length > 0, "slow-query log was not written.");
    }

    // =========================================================================
    // Test data
    // =========================================================================

    private static LeanDocument[] BuildDocuments()
    {
        return
        [
            Document(
                id: "doc-1",
                title: "native aot search",
                body: "lean corpus supports compact native publishing",
                code: "native",
                status: "active",
                category: "docs",
                year: 2025,
                latitude: 51.5074,
                longitude: -0.1278,
                embedding: [1f, 0f, 0f, 0f]),
            Document(
                id: "doc-2",
                title: "fast corpus indexing",
                body: "stored fields and diagnostics stay available",
                code: "index",
                status: "active",
                category: "docs",
                year: 2024,
                latitude: 51.4545,
                longitude: -2.5879,
                embedding: [0.9f, 0.1f, 0f, 0f]),
            Document(
                id: "doc-3",
                title: "api filtering",
                body: "geo vector and collapse queries are covered",
                code: "api",
                status: "active",
                category: "api",
                year: 2023,
                latitude: 40.7128,
                longitude: -74.0060,
                embedding: [0f, 1f, 0f, 0f]),
            Document(
                id: "doc-4",
                title: "archived sample",
                body: "inactive documents remain searchable by explicit status",
                code: "archive",
                status: "archived",
                category: "archive",
                year: 2020,
                latitude: 48.8566,
                longitude: 2.3522,
                embedding: [0f, 0f, 1f, 0f]),
        ];
    }

    private static LeanDocument Document(
        string id,
        string title,
        string body,
        string code,
        string status,
        string category,
        int year,
        double latitude,
        double longitude,
        float[] embedding,
        bool isPublished = true)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("title", title));
        document.Add(new TextField("body", body));
        document.Add(new StringField("code", code));
        document.Add(new StringField("status", status));
        document.Add(new StringField("category", category));
        document.Add(new NumericField("year", year));
        document.Add(new GeoPointField("location", latitude, longitude));
        document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(embedding)));
        document.Add(new StringField("isPublished", isPublished ? "true" : "false", stored: true));
        return document;
    }
}
