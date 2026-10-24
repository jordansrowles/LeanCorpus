using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Scoring;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LeanSortField = Rowles.LeanCorpus.Search.Scoring.SortField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneDoubleField = Lucene.Net.Documents.DoubleField;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneIndexWriter = Lucene.Net.Index.IndexWriter;
using LuceneIndexWriterConfig = Lucene.Net.Index.IndexWriterConfig;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures index-time sort overhead: sorted vs unsorted index creation.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class IndexSortIndexBenchmarks
{
    private const string Bval06FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-06-presorted-ingestion-identities.json";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private (string Body, double Price)[] _documentsWithPrices = [];
    private readonly List<string> _iterationPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        ValidateBval06Fixture();
        _documentsWithPrices = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupIterationPaths();

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        foreach (var path in _iterationPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _iterationPaths.Clear();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_Unsorted()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var directory = new LeanMMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        });

        IndexDocuments(writer);
        writer.Commit();
        return _documentsWithPrices.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_Sorted()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var directory = new LeanMMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            IndexSort = new IndexSort(SortField.Numeric("price"))
        });

        IndexDocuments(writer);
        writer.Commit();
        return _documentsWithPrices.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Index_Unsorted()
    {
        using var dir = new LuceneRAMDirectory();
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new LuceneIndexWriter(
            dir, new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < _documentsWithPrices.Length; i++)
        {
            var (body, price) = _documentsWithPrices[i];
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", body, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneDoubleField("price", price, Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return _documentsWithPrices.Length;
    }

    [Benchmark(Description = "Lucene.NET file-backed pre-sorted ingestion")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Index_PreSortedIngestion()
    {
        var sorted = _documentsWithPrices
            .Select((document, index) => (
                ExternalId: index.ToString(CultureInfo.InvariantCulture),
                Body: document.Body,
                Price: document.Price))
            .OrderBy(static document => document.Price)
            .ToArray();
        string path = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-presorted-index");
        _iterationPaths.Add(path);
        using var dir = new LuceneMMapDirectory(new System.IO.DirectoryInfo(path));
        using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        return BuildLucenePreSortedIndex(dir, analyser, sorted);
    }

    private void IndexDocuments(IndexWriter writer)
    {
        for (int i = 0; i < _documentsWithPrices.Length; i++)
        {
            var (body, price) = _documentsWithPrices[i];
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", body));
            doc.Add(new NumericField("price", price));
            writer.AddDocument(doc);
        }
    }

    private static void ValidateBval06Fixture()
    {
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            Bval06FixturePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-06-presorted-ingestion-identities", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-06 fixture metadata is invalid: {Bval06FixturePath}.");

        JsonElement input = root.GetProperty("input");
        var documents = input.GetProperty("originalDocuments").EnumerateArray()
            .Select(static item => (
                ExternalId: item.GetProperty("externalId").GetString()!,
                Body: item.GetProperty("body").GetString()!,
                Price: item.GetProperty("price").GetDouble()))
            .ToArray();
        JsonElement expected = root.GetProperty("expected");
        Dictionary<string, double> expectedPrices = expected.GetProperty("externalIdPriceMap")
            .EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetDouble(), StringComparer.Ordinal);
        string[] expectedSortedIds = expected.GetProperty("priceSortedIds").EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        string[] expectedDocumentIds = expected.GetProperty("documentIdSet").EnumerateArray()
            .Select(static item => item.GetString()!)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string[] luceneInputOrder = input.GetProperty("luceneInputOrderByPrice").EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        string[] fixtureDocumentIds = documents.Select(static item => item.ExternalId)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string[] sortedFromPrices = documents.OrderBy(static item => item.Price)
            .Select(static item => item.ExternalId)
            .ToArray();

        if (!string.Equals(input.GetProperty("storageBoundary").GetString(), "file-backed", StringComparison.Ordinal)
            || !string.Equals(expected.GetProperty("claimClass").GetString(), "Directional", StringComparison.Ordinal)
            || expected.GetProperty("competitiveRatioAllowed").GetBoolean()
            || documents.Length == 0
            || documents.Select(static item => item.ExternalId).Distinct(StringComparer.Ordinal).Count() != documents.Length
            || expectedPrices.Count != documents.Length
            || !expectedDocumentIds.SequenceEqual(fixtureDocumentIds, StringComparer.Ordinal)
            || !expectedSortedIds.SequenceEqual(sortedFromPrices, StringComparer.Ordinal)
            || !luceneInputOrder.SequenceEqual(expectedSortedIds, StringComparer.Ordinal)
            || documents.Any(item => !expectedPrices.TryGetValue(item.ExternalId, out double price)
                || Math.Abs(price - item.Price) > 1e-9))
        {
            throw new InvalidOperationException($"BVAL-06 fixture does not preserve the original ID/price map and declared directional order: {Bval06FixturePath}.");
        }

        string leanPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-06-fixture-lean");
        string lucenePath = BenchmarkHelpers.CreateTempDirectory("lc-bval-06-fixture-lucene");
        try
        {
            using var leanDirectory = new LeanMMapDirectory(leanPath);
            using (var writer = new LeanIndexWriter(leanDirectory, new LeanIndexWriterConfig
            {
                IndexSort = new IndexSort(LeanSortField.Numeric("price"))
            }))
            {
                foreach ((string externalId, string body, double price) in documents)
                {
                    var leanDocument = new LeanDocument();
                    leanDocument.Add(new LeanStringField("id", externalId));
                    leanDocument.Add(new LeanTextField("body", body));
                    leanDocument.Add(new NumericField("price", price));
                    writer.AddDocument(leanDocument);
                }
                writer.Commit();
            }

            using (var leanSearcher = new LeanIndexSearcher(leanDirectory))
            {
                var leanResults = leanSearcher.Search(
                    new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "orchard"), documents.Length,
                    LeanSortField.Numeric("price"));
                if (leanResults.TotalHits != documents.Length)
                    throw new InvalidOperationException($"BVAL-06 LeanCorpus fixture returned {leanResults.TotalHits} hits; expected {documents.Length}.");

                var leanDocuments = leanResults.ScoreDocs.Select(hit =>
                {
                    var stored = leanSearcher.GetStoredFields(hit.DocId);
                    return (
                        Id: stored["id"][0],
                        Price: double.Parse(stored["price"][0], CultureInfo.InvariantCulture));
                }).ToArray();
                AssertBval06Documents(leanDocuments, expectedPrices, expectedSortedIds, "LeanCorpus native index sort");
            }

            using var luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(lucenePath));
            using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var luceneInput = documents.OrderBy(static item => item.Price).ToArray();
            BuildLucenePreSortedIndex(luceneDirectory, analyser, luceneInput);
            using var luceneReader = LuceneDirectoryReader.Open(luceneDirectory);
            if (luceneReader.NumDocs != documents.Length || luceneReader.MaxDoc != documents.Length)
                throw new InvalidOperationException("BVAL-06 Lucene.NET fixture did not retain the exact live document count.");

            var luceneDocuments = Enumerable.Range(0, luceneReader.MaxDoc).Select(documentId =>
            {
                var stored = luceneReader.Document(documentId);
                return (
                    Id: stored.Get("id") ?? throw new InvalidOperationException($"BVAL-06 Lucene document {documentId} is missing its external ID."),
                    Price: double.Parse(stored.Get("price") ?? throw new InvalidOperationException($"BVAL-06 Lucene document {documentId} is missing its price."), CultureInfo.InvariantCulture));
            }).ToArray();
            AssertBval06Documents(luceneDocuments, expectedPrices, expectedSortedIds, "Lucene.NET pre-sorted ingestion");
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(leanPath);
            BenchmarkHelpers.DeleteDirectory(lucenePath);
        }
    }

    private static int BuildLucenePreSortedIndex(
        LuceneMMapDirectory directory,
        StandardAnalyzer analyser,
        (string ExternalId, string Body, double Price)[] documents)
    {
        using var writer = new LuceneIndexWriter(
            directory, new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < documents.Length; i++)
        {
            var item = documents[i];
            var luceneDocument = new LuceneDocument
            {
                new LuceneStringField("id",
                    item.ExternalId,
                    Lucene.Net.Documents.Field.Store.YES),
                new LuceneTextField("body", item.Body, Lucene.Net.Documents.Field.Store.NO),
                new LuceneDoubleField("price", item.Price, Lucene.Net.Documents.Field.Store.YES)
            };
            writer.AddDocument(luceneDocument);
        }
        writer.Commit();
        return documents.Length;
    }

    private static void AssertBval06Documents(
        (string Id, double Price)[] actual,
        IReadOnlyDictionary<string, double> expectedPrices,
        string[] expectedSortedIds,
        string stage)
    {
        string[] actualIds = actual.Select(static item => item.Id).ToArray();
        string[] expectedIds = expectedPrices.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] actualIdSet = actualIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (actual.Length != expectedPrices.Count
            || actualIds.Distinct(StringComparer.Ordinal).Count() != actualIds.Length
            || !actualIdSet.SequenceEqual(expectedIds, StringComparer.Ordinal)
            || !actualIds.SequenceEqual(expectedSortedIds, StringComparer.Ordinal)
            || actual.Any(item => !expectedPrices.TryGetValue(item.Id, out double expectedPrice)
                || Math.Abs(expectedPrice - item.Price) > 1e-9))
        {
            string actualMap = string.Join(", ", actual.Select(static item => $"{item.Id}:{item.Price.ToString(CultureInfo.InvariantCulture)}"));
            throw new InvalidOperationException(
                $"BVAL-06 {stage} did not preserve the exact external ID/price map and sorted order. " +
                $"Expected=[{string.Join(",", expectedSortedIds)}]; actual=[{actualMap}].");
        }
    }
}
