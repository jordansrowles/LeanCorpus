using System.Globalization;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Shared, lazily-built standard benchmark index for search-parity suites.
///
/// Suites that use the identical <c>id</c> + <c>body</c> schema (TermQuery,
/// BooleanQuery, PhraseQuery, PrefixQuery, FuzzyQuery, WildcardQuery, RegexpQuery,
/// DisjunctionMaxQuery, MultiPhraseQuery, SpanQuery) consume this singleton instead
/// of each building their own index in [GlobalSetup].
///
/// Both LeanCorpus and Lucene.NET indices are built once and reused across all
/// standard-search suites.
/// </summary>
internal static class SharedStandardIndex
{
    private static readonly Lock Gate = new();
    private static bool _initialised;
    private static int _docCount;

    /// <summary>Cached document bodies, shared across all standard-search suites.</summary>
    private static string[] _documents = [];

    // ── LeanCorpus ──

    /// <summary>Path to the on-disk LeanCorpus MMapDirectory index.</summary>
    private static string _leanIndexPath = string.Empty;

    private static LeanMMapDirectory? _leanDirectory;
    private static LeanIndexSearcher? _leanSearcher;

    // ── Lucene.NET ──

    private static string _luceneIndexPath = string.Empty;
    private static LuceneMMapDirectory? _luceneDirectory;
    private static StandardAnalyzer? _luceneAnalyzer;
    private static DirectoryReader? _luceneReader;
    private static LuceneIndexSearcher? _luceneSearcher;

    // ── Public API ──

    /// <summary>Document count currently loaded.</summary>
    public static int DocCount => _docCount;

    /// <summary>True once initialised (any thread may read without a lock).</summary>
    public static bool IsInitialised => _initialised;

    /// <summary>
    /// Ensure the shared standard index is built for <paramref name="docCount"/> documents.
    /// Safe to call from multiple [GlobalSetup] methods — only the first call does work.
    /// </summary>
    public static void EnsureInitialised(int docCount)
    {
        if (_initialised && _docCount == docCount)
            return;

        lock (Gate)
        {
            if (_initialised && _docCount == docCount)
                return;

            // If already initialised with a different doc count, tear down first
            if (_initialised)
                Teardown();

            _docCount = docCount;
            _documents = BenchmarkData.BuildDocuments(docCount);

            // ── LeanCorpus index ──
            _leanIndexPath = Path.Combine(
                BenchmarkHelpers.TempRoot,
                $"leancorpus-shared-stdidx-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_leanIndexPath);

            try
            {
                _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
                using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                    _leanDirectory,
                    new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig
                    {
                        MaxBufferedDocs = 10_000,
                        RamBufferSizeMB = 256
                    }))
                {
                    for (int i = 0; i < _documents.Length; i++)
                    {
                        var doc = new LeanDocument();
                        doc.Add(new LeanStringField("id",
                            i.ToString(CultureInfo.InvariantCulture)));
                        doc.Add(new LeanTextField("body", _documents[i]));
                        writer.AddDocument(doc);
                    }

                    writer.Commit();
                }

                _leanSearcher = new LeanIndexSearcher(_leanDirectory);

                // ── Lucene.NET index ──
                _luceneIndexPath = Path.Combine(
                    BenchmarkHelpers.TempRoot,
                    $"lucenenet-shared-stdidx-{Guid.NewGuid():N}");
                IODirectory.CreateDirectory(_luceneIndexPath);

                _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
                _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

                using (var luceneWriter = new Lucene.Net.Index.IndexWriter(
                    _luceneDirectory,
                    new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer)))
                {
                    for (int i = 0; i < _documents.Length; i++)
                    {
                        var doc = new Lucene.Net.Documents.Document
                        {
                            new LuceneStringField("id",
                                i.ToString(CultureInfo.InvariantCulture),
                                Lucene.Net.Documents.Field.Store.NO),
                            new LuceneTextField("body", _documents[i],
                                Lucene.Net.Documents.Field.Store.NO)
                        };
                        luceneWriter.AddDocument(doc);
                    }

                    luceneWriter.Commit();
                }

                _luceneReader = DirectoryReader.Open(_luceneDirectory);
                _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
            }
            catch
            {
                // Index build failed (e.g. disk full). Clean up partial
                // directories so they don't waste space for later suites.
                Teardown();
                throw;
            }

            _initialised = true;
        }
    }

    /// <summary>Documents used to build the shared index.</summary>
    public static string[] Documents
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _documents;
        }
    }

    /// <summary>Path to the shared LeanCorpus index directory.</summary>
    public static string LeanIndexPath
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _leanIndexPath;
        }
    }

    /// <summary>Pre-built IndexSearcher for the shared LeanCorpus index.</summary>
    public static LeanIndexSearcher LeanSearcher
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _leanSearcher!;
        }
    }

    /// <summary>Pre-built IndexSearcher for the shared Lucene.NET index.</summary>
    public static LuceneIndexSearcher LuceneSearcher
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _luceneSearcher!;
        }
    }

    /// <summary>
    /// Creates a fresh <see cref="LeanIndexSearcher"/> on the shared directory.
    /// Use when the suite needs its own searcher lifecycle (e.g. SearcherManager
    /// or QueryCache suites).
    /// </summary>
    public static LeanIndexSearcher CreateSearcher()
    {
        EnsureInitialised(ResolveDocCount());
        return new LeanIndexSearcher(new LeanMMapDirectory(_leanIndexPath));
    }

    /// <summary>
    /// Creates a fresh <see cref="LeanMMapDirectory"/> pointing at the shared
    /// index path. Callers own the instance and must dispose it.
    /// </summary>
    public static LeanMMapDirectory CreateDirectory()
    {
        EnsureInitialised(ResolveDocCount());
        return new LeanMMapDirectory(_leanIndexPath);
    }

    /// <summary>
    /// Release all shared resources and delete temp index directories.
    /// Called at the end of the benchmark run.
    /// </summary>
    public static void Cleanup()
    {
        lock (Gate)
        {
            Teardown();
        }
    }

    private static void Teardown()
    {
        // LeanCorpus
        _leanSearcher?.Dispose();
        _leanSearcher = null;
        _leanDirectory = null;
        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        _leanIndexPath = string.Empty;

        // Lucene.NET
        _luceneSearcher = null;
        _luceneReader?.Dispose();
        _luceneReader = null;
        _luceneAnalyzer?.Dispose();
        _luceneAnalyzer = null;
        _luceneDirectory?.Dispose();
        _luceneDirectory = null;
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
        _luceneIndexPath = string.Empty;

        _documents = [];
        _docCount = 0;
        _initialised = false;
    }

    private static int ResolveDocCount()
    {
        var env = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var n) && n > 0)
            return n;
        return BenchmarkData.DefaultDocCount;
    }
}
