using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Facet;
using Lucene.Net.Facet.SortedSet;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneSortedDocValuesField = Lucene.Net.Documents.SortedDocValuesField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using LeanMatchAllDocsQuery = Rowles.LeanCorpus.Search.Queries.MatchAllDocsQuery;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures collapse and matched facet collection against Lucene.NET's sorted-set
/// DocValues facets, including cardinality, paging, missing-value and multi-value workloads.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class CollapseAndFacetBenchmarks
{
    private const int TopN = 25;
    private const int CategoryCount = 10;
    private const int HighCardinalityCount = 10_000;
    private const int FacetPageSize = 10;
    private const int FacetOffset = 100;
    private const string Bval07FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-07-collapse-and-facet-pagination.json";
    private const string FieldFixtureGroup = "fixture-group";
    private const string FieldFixtureFacet = "fixture-facet";
    private const string FieldBody = "body";
    private const string FieldCategory = "category";
    private const string FieldHighCardinality = "tag";
    private const string FieldMissingSentinel = "tag_with_missing_sentinel";
    private const string FieldRegion = "region";
    private const string FieldTopic = "topic";
    private const string QueryTerm = "government";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private TermQuery? _leanQuery;
    private LeanMatchAllDocsQuery? _leanMatchAllQuery;
    private CollapseField? _leanCollapse;
    private FacetRequest? _leanLowCardinalityFacet;
    private FacetRequest? _leanHighCardinalityFacet;
    private FacetRequest? _leanHighCardinalityOffsetFacet;
    private FacetRequest? _leanMissingFacet;
    private IFacetRequest[]? _leanMultiDimensionFacets;

    // Lucene.NET index state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;
    private LuceneTermQuery? _luceneQuery;
    private Lucene.Net.Search.MatchAllDocsQuery? _luceneMatchAllQuery;
    private DefaultSortedSetDocValuesReaderState? _luceneFacetState;
    private SortedDocValues? _luceneCollapseValues;

    [GlobalSetup]
    public void Setup()
    {
        ValidateBval07Fixture();
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        _leanQuery = new TermQuery(FieldBody, QueryTerm);
        _leanMatchAllQuery = new LeanMatchAllDocsQuery();
        _leanCollapse = new CollapseField(FieldCategory);
        _leanLowCardinalityFacet = new FacetRequest(FieldCategory, limit: FacetPageSize);
        _leanHighCardinalityFacet = new FacetRequest(FieldHighCardinality, limit: FacetPageSize);
        _leanHighCardinalityOffsetFacet = new FacetRequest(FieldHighCardinality, offset: FacetOffset, limit: FacetPageSize);
        _leanMissingFacet = new FacetRequest(FieldHighCardinality, limit: FacetPageSize, includeMissing: true);
        _leanMultiDimensionFacets =
        [
            _leanLowCardinalityFacet,
            new FacetRequest(FieldRegion, limit: FacetPageSize),
            new FacetRequest(FieldTopic, limit: FacetPageSize)
        ];
        _luceneQuery = new LuceneTermQuery(new LuceneTerm(FieldBody, QueryTerm));
        _luceneMatchAllQuery = new Lucene.Net.Search.MatchAllDocsQuery();
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        DeleteDir(_leanIndexPath);

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        DeleteDir(_luceneIndexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BaseSearch()
        => _leanSearcher!.Search(_leanQuery!, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithCollapse()
        => _leanSearcher!.SearchWithCollapse(_leanQuery!, TopN, _leanCollapse!).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithFacets()
    {
        var (results, facets) = _leanSearcher!.SearchWithFacets(_leanQuery!, TopN, FieldCategory);
        return results.TotalHits + CountFacetBuckets(facets);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_HighCardinalityFacets()
        => CountFacetResults(_leanSearcher!.SearchWithFacetRequests(_leanMatchAllQuery!, TopN, [_leanHighCardinalityFacet!]));

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_HighCardinalityFacetsWithOffset()
        => CountFacetResults(_leanSearcher!.SearchWithFacetRequests(_leanMatchAllQuery!, TopN, [_leanHighCardinalityOffsetFacet!]));

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MissingValueFacets()
        => CountFacetResults(_leanSearcher!.SearchWithFacetRequests(_leanMatchAllQuery!, TopN, [_leanMissingFacet!]));

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MultiDimensionMultiValueFacets()
        => CountFacetResults(_leanSearcher!.SearchWithFacetRequests(_leanMatchAllQuery!, TopN, _leanMultiDimensionFacets!));

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithCollapseAndFacets()
    {
        var collapsed = _leanSearcher!.SearchWithCollapse(_leanQuery!, TopN, _leanCollapse!);
        var (_, facets) = _leanSearcher!.SearchWithFacets(_leanQuery!, TopN, FieldCategory);
        return collapsed.TotalHits + CountFacetBuckets(facets);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
        => _luceneSearcher!.Search(_luceneQuery!, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SearchWithCollapse()
    {
        var collapsed = CollectLuceneCollapse(
            _luceneSearcher!, _luceneReader!, _luceneQuery!, _luceneCollapseValues!,
            TopN, _luceneReader!.MaxDoc);
        return collapsed.TotalGroupCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SearchWithFacets()
    {
        var collector = new Lucene.Net.Facet.FacetsCollector();
        var hits = Lucene.Net.Facet.FacetsCollector.Search(
            _luceneSearcher!, _luceneQuery!, TopN, collector);
        var facets = new SortedSetDocValuesFacetCounts(_luceneFacetState!, collector);
        var result = facets.GetTopChildren(CategoryCount, FieldCategory);
        return hits.TotalHits + (result?.ChildCount ?? 0);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_HighCardinalityFacets()
        => SearchLuceneFacets(FieldHighCardinality, FacetPageSize);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_HighCardinalityFacetsWithOffset()
        => SearchLuceneFacetPage(FieldHighCardinality, FacetOffset, FacetPageSize);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MissingValueFacetsWithSentinel()
        => SearchLuceneFacets(FieldMissingSentinel, FacetPageSize);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MultiDimensionMultiValueFacets()
    {
        var collector = new Lucene.Net.Facet.FacetsCollector();
        var hits = Lucene.Net.Facet.FacetsCollector.Search(
            _luceneSearcher!, _luceneMatchAllQuery!, TopN, collector);
        var facets = new SortedSetDocValuesFacetCounts(_luceneFacetState!, collector);
        return hits.TotalHits
            + GetChildCount(facets.GetTopChildren(FacetPageSize, FieldCategory))
            + GetChildCount(facets.GetTopChildren(FacetPageSize, FieldRegion))
            + GetChildCount(facets.GetTopChildren(FacetPageSize, FieldTopic));
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SearchWithCollapseAndFacets()
    {
        int collapsedCount = LuceneNet_SearchWithCollapse();
        var collector = new Lucene.Net.Facet.FacetsCollector();
        _ = Lucene.Net.Facet.FacetsCollector.Search(
            _luceneSearcher!, _luceneQuery!, TopN, collector);
        var facets = new SortedSetDocValuesFacetCounts(_luceneFacetState!, collector);
        var result = facets.GetTopChildren(CategoryCount, FieldCategory);
        return collapsedCount + (result?.ChildCount ?? 0);
    }

    private static int CountFacetBuckets(IReadOnlyList<Rowles.LeanCorpus.Search.Scoring.FacetResult> facets)
    {
        int count = 0;
        for (int i = 0; i < facets.Count; i++)
            count += facets[i].Buckets.Count;
        return count;
    }

    private static int CountFacetResults((Rowles.LeanCorpus.Search.Scoring.TopDocs Results, IReadOnlyList<Rowles.LeanCorpus.Search.Scoring.FacetResult> Facets) result)
        => result.Results.TotalHits + CountFacetBuckets(result.Facets);

    private int SearchLuceneFacets(string dimension, int topChildren)
    {
        var collector = new Lucene.Net.Facet.FacetsCollector();
        var hits = Lucene.Net.Facet.FacetsCollector.Search(
            _luceneSearcher!, _luceneMatchAllQuery!, TopN, collector);
        var facets = new SortedSetDocValuesFacetCounts(_luceneFacetState!, collector);
        return hits.TotalHits + GetChildCount(facets.GetTopChildren(topChildren, dimension));
    }

    private int SearchLuceneFacetPage(string dimension, int offset, int pageSize)
    {
        var page = CollectLuceneFacetPage(
            _luceneSearcher!, _luceneFacetState!, _luceneMatchAllQuery!, TopN,
            dimension, offset, pageSize);
        return page.TotalHits + page.Labels.Length;
    }

    private static int GetChildCount(Lucene.Net.Facet.FacetResult? result) => result?.ChildCount ?? 0;

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-collapse-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        var facetsConfig = new FacetsConfig();
        facetsConfig.SetMultiValued(FieldTopic, true);
        foreach (var (i, id, category, body) in EnumerateDocuments(documents))
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id", id, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField(FieldBody, body, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneStringField(FieldCategory, category, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneSortedDocValuesField(FieldCategory, new BytesRef(category)));
            doc.Add(new SortedSetDocValuesFacetField(FieldCategory, category));
            if (i % 7 != 0)
            {
                string tag = $"tag{i % HighCardinalityCount}";
                doc.Add(new LuceneSortedDocValuesField(FieldHighCardinality, new BytesRef(tag)));
                doc.Add(new SortedSetDocValuesFacetField(FieldHighCardinality, tag));
                doc.Add(new LuceneSortedDocValuesField(FieldMissingSentinel, new BytesRef(tag)));
                doc.Add(new SortedSetDocValuesFacetField(FieldMissingSentinel, tag));
            }
            else
            {
                doc.Add(new LuceneSortedDocValuesField(FieldMissingSentinel, new BytesRef("__missing__")));
                doc.Add(new SortedSetDocValuesFacetField(FieldMissingSentinel, "__missing__"));
            }
            string region = $"region{i % 8}";
            doc.Add(new LuceneSortedDocValuesField(FieldRegion, new BytesRef(region)));
            doc.Add(new SortedSetDocValuesFacetField(FieldRegion, region));
            string topic = $"topic{i % 50}";
            doc.Add(new SortedSetDocValuesFacetField(FieldTopic, topic));
            doc.Add(new SortedSetDocValuesFacetField(FieldTopic, $"topic{(i + 11) % 50}"));
            writer.AddDocument(facetsConfig.Build(doc));
        }
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
        _luceneCollapseValues = MultiDocValues.GetSortedValues(_luceneReader, FieldCategory);
        _luceneFacetState = new DefaultSortedSetDocValuesReaderState(
            _luceneReader, FacetsConfig.DEFAULT_INDEX_FIELD_NAME);
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-collapse-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        foreach (var (i, id, category, body) in EnumerateDocuments(documents))
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", id));
            doc.Add(new LeanTextField(FieldBody, body));
            doc.Add(new LeanStringField(FieldCategory, category));
            if (i % 7 != 0)
                doc.Add(new LeanStringField(FieldHighCardinality, $"tag{i % HighCardinalityCount}"));
            doc.Add(new LeanStringField(FieldRegion, $"region{i % 8}"));
            doc.Add(new LeanStringField(FieldTopic, $"topic{i % 50}"));
            doc.Add(new LeanStringField(FieldTopic, $"topic{(i + 11) % 50}"));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private static IEnumerable<(int Index, string Id, string Category, string Body)> EnumerateDocuments(string[] documents)
    {
        for (int i = 0; i < documents.Length; i++)
            yield return (i, i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"cat{i % CategoryCount}", documents[i]);
    }

    private static void ValidateBval07Fixture()
    {
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            Bval07FixturePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-07-collapse-and-facet-pagination", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-07 fixture metadata is invalid: {Bval07FixturePath}.");

        JsonElement input = root.GetProperty("input");
        JsonElement expected = root.GetProperty("expected");
        int topN = input.GetProperty("topN").GetInt32();
        int originalCandidateLimit = input.GetProperty("luceneCandidateLimit").GetInt32();
        JsonElement orderedPrefix = input.GetProperty("orderedPrefix");
        int prefixCount = orderedPrefix.GetProperty("documentCount").GetInt32();
        int firstRank = orderedPrefix.GetProperty("firstRank").GetInt32();
        string prefixIdPattern = orderedPrefix.GetProperty("idPattern").GetString()!;
        string prefixGroupKey = orderedPrefix.GetProperty("groupKey").GetString()!;
        string queryTerm = input.GetProperty("queryTerm").GetString()!;
        string facetField = input.GetProperty("facetField").GetString()!;
        int facetOffset = input.GetProperty("facetOffset").GetInt32();
        int facetPageSize = input.GetProperty("facetPageSize").GetInt32();
        var documents = new List<Bval07Document>(expected.GetProperty("documentCount").GetInt32());

        for (int rank = firstRank; rank < firstRank + prefixCount; rank++)
        {
            string id = prefixIdPattern.Replace(
                "{rank:D3}", rank.ToString("D3", CultureInfo.InvariantCulture), StringComparison.Ordinal);
            documents.Add(new Bval07Document(id, prefixGroupKey, queryTerm, FacetBucket: null));
        }

        var afterPrefix = input.GetProperty("afterPrefix").EnumerateArray()
            .Select(static item => new Bval07Document(
                item.GetProperty("id").GetString()!,
                item.GetProperty("groupKey").GetString()!,
                "",
                FacetBucket: null,
                Rank: item.GetProperty("rank").GetInt32()))
            .ToArray();
        for (int index = 0; index < afterPrefix.Length; index++)
            documents.Add(afterPrefix[index] with { Body = queryTerm });

        foreach (JsonElement item in input.GetProperty("facetDocuments").EnumerateArray())
        {
            documents.Add(new Bval07Document(
                item.GetProperty("id").GetString()!,
                item.GetProperty("groupKey").GetString()!,
                queryTerm,
                item.GetProperty("bucket").GetString()!));
        }

        string[] expectedGroupKeys = expected.GetProperty("collapsedGroupKeys").EnumerateArray()
            .Select(static item => item.GetString()!)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> expectedRepresentatives = expected.GetProperty("representativesByGroup")
            .EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetString()!, StringComparer.Ordinal);
        string[] expectedFacetPage = expected.GetProperty("facetPageIds").EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        bool pageMustBeCompleteAndUnique = expected.GetProperty("noMissingOrDuplicatePageEntries").GetBoolean();

        if (topN <= 0
            || originalCandidateLimit != topN * 10
            || prefixCount != originalCandidateLimit
            || firstRank != 1
            || string.IsNullOrWhiteSpace(prefixIdPattern)
            || !string.Equals(prefixGroupKey, "A", StringComparison.Ordinal)
            || afterPrefix.Length != 1
            || afterPrefix[0].Rank != originalCandidateLimit + 1
            || !string.Equals(afterPrefix[0].GroupKey, "B", StringComparison.Ordinal)
            || documents.Count != expected.GetProperty("documentCount").GetInt32()
            || documents.Select(static document => document.Id).Distinct(StringComparer.Ordinal).Count() != documents.Count
            || !string.Equals(expectedRepresentatives["B"], expected.GetProperty("representativeForGroupB").GetString(), StringComparison.Ordinal)
            || facetPageSize != expectedFacetPage.Length
            || facetOffset < 0
            || string.IsNullOrWhiteSpace(facetField))
            throw new InvalidOperationException($"BVAL-07 fixture structure is invalid: {Bval07FixturePath}.");

        string leanPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-07-collapse-facet");
        string lucenePath = BenchmarkHelpers.CreateTempDirectory("lucene-bval-07-collapse-facet");
        try
        {
            using (var directory = new LeanMMapDirectory(leanPath))
            {
                using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                    directory, new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig());
                foreach (Bval07Document item in documents)
                {
                    var leanDocument = new LeanDocument();
                    leanDocument.Add(new LeanStringField("id", item.Id));
                    leanDocument.Add(new LeanTextField(FieldBody, item.Body));
                    leanDocument.Add(new LeanStringField(FieldFixtureGroup, item.GroupKey));
                    if (item.FacetBucket is not null)
                        leanDocument.Add(new LeanStringField(facetField, item.FacetBucket));
                    writer.AddDocument(leanDocument);
                }
                writer.Commit();
            }

            using (var directory = new LeanMMapDirectory(leanPath))
            using (var searcher = new LeanIndexSearcher(directory))
            {
                var collapsed = searcher.SearchWithCollapse(
                    new TermQuery(FieldBody, queryTerm), topN,
                    new Rowles.LeanCorpus.Search.Scoring.CollapseField(FieldFixtureGroup));
                var representatives = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var scoreDoc in collapsed.ScoreDocs)
                {
                    var stored = searcher.GetStoredFields(scoreDoc.DocId);
                    if (!representatives.TryAdd(stored[FieldFixtureGroup][0], stored["id"][0]))
                        throw new InvalidOperationException("BVAL-07 LeanCorpus collapse returned a duplicate group representative.");
                }
                AssertBval07Collapse(
                    collapsed.TotalHits, representatives, expected.GetProperty("totalGroupCount").GetInt32(),
                    expectedGroupKeys, expectedRepresentatives, "LeanCorpus");

                var (facetHits, leanFacets) = searcher.SearchWithFacetRequests(
                    new LeanMatchAllDocsQuery(), topN,
                    [new Rowles.LeanCorpus.Search.Scoring.FacetRequest(
                        facetField, facetOffset, facetPageSize,
                        Rowles.LeanCorpus.Search.Scoring.FacetBucketOrder.ValueAscending)]);
                var leanFacet = leanFacets.Single();
                AssertBval07FacetPage(
                    facetHits.TotalHits, leanFacet.TotalBucketCount,
                    leanFacet.Buckets.Select(static bucket => bucket.Value).ToArray(),
                    expected.GetProperty("facetMatchedDocumentCount").GetInt32(),
                    expected.GetProperty("facetTotalBucketCount").GetInt32(),
                    expectedFacetPage, pageMustBeCompleteAndUnique, "LeanCorpus");
            }

            using (var directory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(lucenePath)))
            using (var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48))
            {
                var facetsConfig = new FacetsConfig();
                using var writer = new Lucene.Net.Index.IndexWriter(
                    directory,
                    new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
                foreach (Bval07Document item in documents)
                {
                    var luceneDocument = new LuceneDocument();
                    luceneDocument.Add(new LuceneStringField("id", item.Id, Lucene.Net.Documents.Field.Store.YES));
                    luceneDocument.Add(new LuceneStringField(FieldFixtureGroup, item.GroupKey, Lucene.Net.Documents.Field.Store.YES));
                    luceneDocument.Add(new LuceneSortedDocValuesField(FieldFixtureGroup, new BytesRef(item.GroupKey)));
                    luceneDocument.Add(new LuceneTextField(FieldBody, item.Body, Lucene.Net.Documents.Field.Store.NO));
                    if (item.FacetBucket is not null)
                        luceneDocument.Add(new SortedSetDocValuesFacetField(facetField, item.FacetBucket));
                    writer.AddDocument(facetsConfig.Build(luceneDocument));
                }
                writer.Commit();
            }

            using (var directory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(lucenePath)))
            using (var reader = LuceneDirectoryReader.Open(directory))
            {
                if (reader.NumDocs != documents.Count || reader.MaxDoc != documents.Count)
                    throw new InvalidOperationException("BVAL-07 Lucene.NET fixture changed the exact document count.");
                var searcher = new LuceneIndexSearcher(reader);
                var luceneQuery = new LuceneTermQuery(new LuceneTerm(FieldBody, queryTerm));
                var collapseValues = MultiDocValues.GetSortedValues(reader, FieldFixtureGroup)
                    ?? throw new InvalidOperationException("BVAL-07 Lucene.NET fixture is missing collapse DocValues.");
                var collapsed = CollectLuceneCollapse(
                    searcher, reader, luceneQuery, collapseValues, topN, reader.MaxDoc);
                var representatives = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var scoreDoc in collapsed.Representatives)
                {
                    var stored = reader.Document(scoreDoc.Doc);
                    if (!representatives.TryAdd(stored.Get(FieldFixtureGroup)!, stored.Get("id")!))
                        throw new InvalidOperationException("BVAL-07 Lucene.NET collapse returned a duplicate group representative.");
                }
                AssertBval07Collapse(
                    collapsed.TotalGroupCount, representatives, expected.GetProperty("totalGroupCount").GetInt32(),
                    expectedGroupKeys, expectedRepresentatives, "Lucene.NET");

                var facetState = new DefaultSortedSetDocValuesReaderState(reader);
                var facetPage = CollectLuceneFacetPage(
                    searcher, facetState, new Lucene.Net.Search.MatchAllDocsQuery(), topN,
                    facetField, facetOffset, facetPageSize);
                AssertBval07FacetPage(
                    facetPage.TotalHits, facetPage.TotalBucketCount, facetPage.Labels,
                    expected.GetProperty("facetMatchedDocumentCount").GetInt32(),
                    expected.GetProperty("facetTotalBucketCount").GetInt32(),
                    expectedFacetPage, pageMustBeCompleteAndUnique, "Lucene.NET");
            }
        }
        finally
        {
            DeleteDir(leanPath);
            DeleteDir(lucenePath);
        }
    }

    private static LuceneCollapseResult CollectLuceneCollapse(
        LuceneIndexSearcher searcher,
        LuceneDirectoryReader reader,
        Lucene.Net.Search.Query query,
        SortedDocValues collapseValues,
        int topN,
        int candidateLimit)
    {
        var hits = searcher.Search(query, Math.Min(reader.MaxDoc, candidateLimit));
        var firstByGroup = new Dictionary<int, Lucene.Net.Search.ScoreDoc>();
        foreach (var hit in hits.ScoreDocs)
        {
            int groupOrd = collapseValues.GetOrd(hit.Doc);
            if (groupOrd >= 0)
                firstByGroup.TryAdd(groupOrd, hit);
        }

        Lucene.Net.Search.ScoreDoc[] representatives = firstByGroup.Values
            .OrderByDescending(static hit => hit.Score)
            .ThenBy(static hit => hit.Doc)
            .Take(topN)
            .ToArray();
        return new LuceneCollapseResult(firstByGroup.Count, representatives);
    }

    private static LuceneFacetPage CollectLuceneFacetPage(
        LuceneIndexSearcher searcher,
        DefaultSortedSetDocValuesReaderState facetState,
        Lucene.Net.Search.Query query,
        int topN,
        string field,
        int offset,
        int pageSize)
    {
        var collector = new Lucene.Net.Facet.FacetsCollector();
        var hits = Lucene.Net.Facet.FacetsCollector.Search(searcher, query, topN, collector);
        var counts = new SortedSetDocValuesFacetCounts(facetState, collector);
        var result = counts.GetTopChildren(checked(offset + pageSize), field);
        string[] labels = result?.LabelValues
            .Skip(offset)
            .Take(pageSize)
            .Select(static value => value.Label)
            .ToArray() ?? [];
        return new LuceneFacetPage(hits.TotalHits, result?.ChildCount ?? 0, labels);
    }

    private static void AssertBval07Collapse(
        int actualTotalGroups,
        IReadOnlyDictionary<string, string> actualRepresentatives,
        int expectedTotalGroups,
        string[] expectedGroupKeys,
        IReadOnlyDictionary<string, string> expectedRepresentatives,
        string engine)
    {
        string[] actualGroupKeys = actualRepresentatives.Keys
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        bool representativesMatch = expectedRepresentatives.Count == actualRepresentatives.Count
            && expectedRepresentatives.All(pair => actualRepresentatives.TryGetValue(pair.Key, out string? id)
                && string.Equals(id, pair.Value, StringComparison.Ordinal));
        if (actualTotalGroups != expectedTotalGroups
            || actualRepresentatives.Count != expectedGroupKeys.Length
            || !actualGroupKeys.SequenceEqual(expectedGroupKeys, StringComparer.Ordinal)
            || !representativesMatch)
        {
            throw new InvalidOperationException(
                $"BVAL-07 {engine} collapse mismatch. Expected groups=[{string.Join(",", expectedGroupKeys)}], " +
                $"total={expectedTotalGroups}, representatives=[{string.Join(",", expectedRepresentatives.Select(static pair => $"{pair.Key}:{pair.Value}"))}]; " +
                $"actual groups=[{string.Join(",", actualGroupKeys)}], total={actualTotalGroups}, " +
                $"representatives=[{string.Join(",", actualRepresentatives.Select(static pair => $"{pair.Key}:{pair.Value}"))}].");
        }
    }

    private static void AssertBval07FacetPage(
        int actualTotalHits,
        int actualTotalBuckets,
        string[] actualPage,
        int expectedTotalHits,
        int expectedTotalBuckets,
        string[] expectedPage,
        bool requireCompleteUniquePage,
        string engine)
    {
        bool pageIsCompleteAndUnique = actualPage.Length == expectedPage.Length
            && actualPage.Distinct(StringComparer.Ordinal).Count() == actualPage.Length;
        if (actualTotalHits != expectedTotalHits
            || actualTotalBuckets != expectedTotalBuckets
            || !actualPage.SequenceEqual(expectedPage, StringComparer.Ordinal)
            || (requireCompleteUniquePage && !pageIsCompleteAndUnique))
        {
            throw new InvalidOperationException(
                $"BVAL-07 {engine} facet page mismatch. Expected totalHits={expectedTotalHits}, " +
                $"totalBuckets={expectedTotalBuckets}, page=[{string.Join(",", expectedPage)}]; " +
                $"actual totalHits={actualTotalHits}, totalBuckets={actualTotalBuckets}, " +
                $"page=[{string.Join(",", actualPage)}].");
        }
    }

    private readonly record struct Bval07Document(
        string Id,
        string GroupKey,
        string Body,
        string? FacetBucket,
        int Rank = 0);

    private readonly record struct LuceneCollapseResult(
        int TotalGroupCount,
        Lucene.Net.Search.ScoreDoc[] Representatives);

    private readonly record struct LuceneFacetPage(
        int TotalHits,
        int TotalBucketCount,
        string[] Labels);

    private static void DeleteDir(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && IODirectory.Exists(path))
            IODirectory.Delete(path, recursive: true);
    }
}
