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
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="CollapseField"/> and <see cref="FacetsCollector"/> overhead
/// in a search with a categorical string field.
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
    private const string FieldBody = "body";
    private const string FieldCategory = "category";
    private const string QueryTerm = "government";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private TermQuery? _leanQuery;
    private CollapseField? _leanCollapse;

    // Lucene.NET index state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;
    private LuceneTermQuery? _luceneQuery;
    private DefaultSortedSetDocValuesReaderState? _luceneFacetState;
    private SortedDocValues? _luceneCollapseValues;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        _leanQuery = new TermQuery(FieldBody, QueryTerm);
        _leanCollapse = new CollapseField(FieldCategory);
        _luceneQuery = new LuceneTermQuery(new LuceneTerm(FieldBody, QueryTerm));
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
        foreach (var (i, id, category, body) in EnumerateDocuments(documents))
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id", id, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField(FieldBody, body, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneStringField(FieldCategory, category, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneSortedDocValuesField(FieldCategory, new BytesRef(category)));
            doc.Add(new SortedSetDocValuesFacetField(FieldCategory, category));
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
