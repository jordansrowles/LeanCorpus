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
        var hits = _luceneSearcher!.Search(_luceneQuery!, Math.Min(_luceneReader!.MaxDoc, TopN * 10));
        var seen = new HashSet<int>();
        int collapsedCount = 0;
        foreach (var sd in hits.ScoreDocs)
        {
            int categoryOrd = _luceneCollapseValues!.GetOrd(sd.Doc);
            if (categoryOrd >= 0 && seen.Add(categoryOrd) && ++collapsedCount == TopN)
                break;
        }
        return collapsedCount;
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
        => SearchLuceneFacets(FieldHighCardinality, FacetOffset + FacetPageSize);

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

    private static void DeleteDir(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && IODirectory.Exists(path))
            IODirectory.Delete(path, recursive: true);
    }
}
