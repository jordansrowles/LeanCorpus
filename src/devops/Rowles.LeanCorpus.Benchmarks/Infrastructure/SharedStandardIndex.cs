using System.Globalization;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Shared, lazily-built standard benchmark index for search-parity suites.
///
/// Suites that use the identical <c>id</c> + <c>body</c> schema (TermQuery,
/// BooleanQuery, PhraseQuery, PrefixQuery, FuzzyQuery, WildcardQuery, RegexpQuery,
/// DisjunctionMaxQuery, MultiPhraseQuery, SpanQuery) consume this singleton instead
/// of each building their own index in [GlobalSetup].
///
/// The Lucene.NET side still builds per-class (directory types vary), but the
/// document array is also cached here so it is generated only once.
/// </summary>
internal static class SharedStandardIndex
{
    private static readonly Lock Gate = new();
    private static bool _initialised;
    private static int _docCount;

    /// <summary>Cached document bodies, shared across all standard-search suites.</summary>
    private static string[] _documents = [];

    /// <summary>Path to the on-disk LeanCorpus MMapDirectory index.</summary>
    private static string _leanIndexPath = string.Empty;

    private static LeanMMapDirectory? _leanDirectory;
    private static LeanIndexSearcher? _leanSearcher;

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
            }
            catch
            {
                // Index build failed (e.g. disk full). Clean up the partial
                // temp directory so it doesn't waste space for later suites.
                _leanSearcher?.Dispose();
                _leanSearcher = null;
                _leanDirectory = null;
                BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
                _leanIndexPath = string.Empty;
                _documents = [];
                _docCount = 0;
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
    /// Release all shared resources and delete the temp index directory.
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
        _leanSearcher?.Dispose();
        _leanSearcher = null;
        _leanDirectory = null;

        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);

        _leanIndexPath = string.Empty;
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
