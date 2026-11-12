using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanRegexpQuery = Rowles.LeanCorpus.Search.Queries.RegexpQuery;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneRegexpQuery = Lucene.Net.Search.RegexpQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="RegexpQuery"/> throughput against Lucene.NET <c>RegexpQuery</c>.
/// As-shipped rows use the shared standard index; single-segment rows use a force-merged copy
/// of the same documents on both engines (ADR045).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class RegexpQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("gov.*ment", "mark.*", ".*nation.*")]
    public string Pattern { get; set; } = "gov.*ment";

    private static readonly System.Threading.Lock s_gate = new();
    private static int s_singleSegmentDocCount;
    private static string s_singleSegmentLeanPath = string.Empty;
    private static string s_singleSegmentLucenePath = string.Empty;
    private static LeanMMapDirectory? s_singleSegmentLeanDirectory;
    private static LeanIndexSearcher? s_singleSegmentLeanSearcher;
    private static LuceneMMapDirectory? s_singleSegmentLuceneDirectory;
    private static DirectoryReader? s_singleSegmentLuceneReader;
    private static LuceneIndexSearcher? s_singleSegmentLuceneSearcher;

    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        EnsureSingleSegmentIndexes(DocumentCount);

        string caseName = string.Concat("Pattern=", Pattern);
        BenchmarkFixtureEvidence.RequireWork("regexp", string.Concat(caseName, " as-shipped"), BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_RegexpQuery());
        BenchmarkFixtureEvidence.RequireWork("regexp", string.Concat(caseName, " as-shipped"), BenchmarkFixtureEvidence.LuceneNetEngine, LuceneNet_RegexpQuery());
        BenchmarkFixtureEvidence.RequireWork("regexp", string.Concat(caseName, " single-segment"), BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_RegexpQuery_SingleSegment());
        BenchmarkFixtureEvidence.RequireWork("regexp", string.Concat(caseName, " single-segment"), BenchmarkFixtureEvidence.LuceneNetEngine, LuceneNet_RegexpQuery_SingleSegment());
        ValidateWholeTermParity();
    }

    /// <summary>
    /// Both engines match whole terms (ADR048), so every route must return the same document count;
    /// <c>gov.*ment</c> on the 100,000-document corpus is the reviewed reference value.
    /// </summary>
    private void ValidateWholeTermParity()
    {
        int[] counts = [LeanCorpus_RegexpQuery(), LuceneNet_RegexpQuery(), LeanCorpus_RegexpQuery_SingleSegment(), LuceneNet_RegexpQuery_SingleSegment()];
        if (counts.Distinct().Count() != 1)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Regexp '{Pattern}' parity failed: LeanCorpus {counts[0]}, Lucene.NET {counts[1]}, single-segment LeanCorpus {counts[2]}, Lucene.NET {counts[3]}."));
        if (Pattern == "gov.*ment" && DocumentCount == 100_000 && counts[0] != 18_946)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Regexp 'gov.*ment' matched {counts[0]} documents on both engines; the reviewed value is 18,946."));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // The as-shipped index is owned by SharedStandardIndex; the single-segment
        // indexes are static and persist for the lifetime of the benchmark class.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_RegexpQuery()
        => _leanSearcher!.Search(new LeanRegexpQuery("body", Pattern), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_RegexpQuery()
    {
        var q = new LuceneRegexpQuery(new Term("body", Pattern));
        return SharedStandardIndex.LuceneSearcher.Search(q, TopN).TotalHits;
    }

    [Benchmark(Description = "LeanCorpus regexp single segment")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_RegexpQuery_SingleSegment()
        => s_singleSegmentLeanSearcher!.Search(new LeanRegexpQuery("body", Pattern), TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET regexp single segment")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_RegexpQuery_SingleSegment()
    {
        var q = new LuceneRegexpQuery(new Term("body", Pattern));
        return s_singleSegmentLuceneSearcher!.Search(q, TopN).TotalHits;
    }

    private static void EnsureSingleSegmentIndexes(int documentCount)
    {
        if (s_singleSegmentDocCount == documentCount && s_singleSegmentLeanSearcher is not null)
            return;

        lock (s_gate)
        {
            if (s_singleSegmentDocCount == documentCount && s_singleSegmentLeanSearcher is not null)
                return;

            ReleaseSingleSegmentIndexes();
            var documents = BenchmarkData.BuildDocuments(documentCount);

            s_singleSegmentLeanPath = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-regexp-ss");
            s_singleSegmentLeanDirectory = new LeanMMapDirectory(s_singleSegmentLeanPath);
            using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                s_singleSegmentLeanDirectory,
                new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 }))
            {
                for (int i = 0; i < documents.Length; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    doc.Add(new LeanTextField("body", documents[i]));
                    writer.AddDocument(doc);
                }
                writer.Commit();
                writer.ForceMerge(1);
                writer.Commit();
            }
            s_singleSegmentLeanSearcher = new LeanIndexSearcher(s_singleSegmentLeanDirectory);

            s_singleSegmentLucenePath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-regexp-ss");
            s_singleSegmentLuceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(s_singleSegmentLucenePath));
            var similarity = SharedStandardIndex.CreateBval05Bm25Similarity();
            using (var analyser = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48))
            using (var writer = new Lucene.Net.Index.IndexWriter(
                s_singleSegmentLuceneDirectory,
                new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyser) { Similarity = similarity }))
            {
                for (int i = 0; i < documents.Length; i++)
                {
                    writer.AddDocument(new Lucene.Net.Documents.Document
                    {
                        new Lucene.Net.Documents.StringField("id",
                            i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Lucene.Net.Documents.Field.Store.NO),
                        new Lucene.Net.Documents.TextField("body", documents[i], Lucene.Net.Documents.Field.Store.NO)
                    });
                }
                writer.ForceMerge(1);
                writer.Commit();
            }
            s_singleSegmentLuceneReader = DirectoryReader.Open(s_singleSegmentLuceneDirectory);
            s_singleSegmentLuceneSearcher = new LuceneIndexSearcher(s_singleSegmentLuceneReader) { Similarity = similarity };

            string fixture = string.Concat("regexp-single-segment-", documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var lean = BenchmarkFixtureEvidence.RecordLeanCorpus(fixture, s_singleSegmentLeanDirectory, s_singleSegmentLeanSearcher);
            var lucene = BenchmarkFixtureEvidence.RecordLucene(fixture, s_singleSegmentLuceneDirectory, s_singleSegmentLuceneReader);
            if (lean.SegmentCount != 1 || lucene.SegmentCount != 1)
                throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Single-segment regexp fixture has {lean.SegmentCount} LeanCorpus and {lucene.SegmentCount} Lucene.NET segments."));
            s_singleSegmentDocCount = documentCount;
        }
    }

    private static void ReleaseSingleSegmentIndexes()
    {
        s_singleSegmentLeanSearcher?.Dispose();
        s_singleSegmentLeanSearcher = null;
        s_singleSegmentLuceneReader?.Dispose();
        s_singleSegmentLuceneReader = null;
        s_singleSegmentLuceneDirectory?.Dispose();
        s_singleSegmentLuceneDirectory = null;
        BenchmarkHelpers.DeleteDirectory(s_singleSegmentLeanPath);
        BenchmarkHelpers.DeleteDirectory(s_singleSegmentLucenePath);
        s_singleSegmentDocCount = 0;
    }
}
