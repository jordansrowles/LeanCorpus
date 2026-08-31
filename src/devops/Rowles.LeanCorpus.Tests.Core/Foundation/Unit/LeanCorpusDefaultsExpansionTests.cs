using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

/// <summary>Verifies the expanded global-default surface and its ownership rules.</summary>
[Collection(LeanCorpusDefaultsCollection.Name)]
[Category(TestCategory.Unit)]
[Area(TestArea.Foundation)]
public sealed class LeanCorpusDefaultsExpansionTests
{
    [Fact(DisplayName = "LeanCorpus Defaults: Reset Retains Every Built-In Configuration Value")]
    public void Reset_RetainsBuiltInConfigurationValues()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Reset();

            var writer = new IndexWriterConfig();
            Assert.Same(CodecCatalog.Default, writer.CodecCatalog);
            Assert.Equal(512, writer.RamBufferSizeMB);
            Assert.Equal(256, writer.RamPerThreadHardLimitMB);
            Assert.Equal(1, writer.MaxConcurrentFlushes);
            Assert.Equal(10_000, writer.MaxBufferedDocs);
            Assert.Equal(20_000, writer.MaxQueuedDocs);
            Assert.Equal(512L * 1024 * 1024, writer.MaxQueuedBytes);
            Assert.False(writer.StorePayloads);
            Assert.False(writer.StoreTermVectors);
            Assert.False(writer.UseCompoundFile);
            Assert.True(writer.DurableCommits);
            Assert.Equal(IndexOpenCompatibilityMode.Strict, writer.CompatibilityMode);
            Assert.Equal(FieldCompressionPolicy.Deflate, writer.CompressionPolicy);
            Assert.Equal(16, writer.StoredFieldBlockSize);
            Assert.Equal(128, writer.PostingsSkipInterval);
            Assert.Equal(10, writer.MergeThreshold);
            Assert.Equal(512, writer.BKDMaxLeafSize);
            Assert.Equal(4_096, writer.AnalyserInternCacheSize);
            Assert.Equal(0, writer.MaxTokensPerDocument);
            Assert.Equal(TokenBudgetPolicy.Truncate, writer.TokenBudgetPolicy);
            Assert.Equal(0, writer.MergeThrottleSegments);
            Assert.Equal(1, writer.MaxConcurrentMerges);
            Assert.Equal(4L * 1024 * 1024 * 1024, writer.MaxPendingMergeBytes);
            Assert.True(writer.NormaliseVectors);
            Assert.Equal(VectorQuantisation.None, writer.VectorQuantisation);
            Assert.True(writer.BuildHnswOnFlush);
            Assert.Null(writer.HnswSeed);
            Assert.False(writer.TrackSequenceNumbers);
            Assert.False(writer.SoftDeletesEnabled);
            Assert.Equal(86_400, writer.SoftDeleteRetentionSeconds);

            var hnsw = writer.HnswBuildConfig;
            Assert.Equal(16, hnsw.M);
            Assert.Equal(100, hnsw.EfConstruction);
            Assert.Equal(0, hnsw.M0);

            var searcher = new IndexSearcherConfig();
            Assert.Same(CodecCatalog.Default, searcher.CodecCatalog);
            Assert.Same(Bm25Similarity.Instance, searcher.Similarity);
            Assert.Null(searcher.PerFieldSimilarities);
            Assert.Equal(IndexOpenCompatibilityMode.Strict, searcher.CompatibilityMode);
            Assert.False(searcher.ParallelSearch);
            Assert.Equal(-1, searcher.MaxConcurrency);
            Assert.False(searcher.EnableQueryCache);
            Assert.Equal(1_024, searcher.QueryCacheMaxEntries);
            Assert.Equal(256, searcher.MaxCachedSegmentReaders);
            Assert.False(searcher.EnableBlockMaxWand);
            Assert.Null(searcher.SlowQueryLog);
            Assert.Null(searcher.SearchAnalytics);

            var manager = new SearcherManagerConfig();
            Assert.Equal(TimeSpan.FromSeconds(1), manager.RefreshInterval);
            Assert.Equal(IndexOpenCompatibilityMode.Strict, manager.CompatibilityMode);
            Assert.NotSame(manager.SearcherConfig, new IndexSearcherConfig());

            var mapping = new JsonMappingOptions();
            Assert.Equal(".", mapping.FieldNameSeparator);
            Assert.Equal(10, mapping.MaxDepth);
            Assert.Equal(int.MaxValue, mapping.StringFieldMaxLength);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Writer Scalar Defaults Apply Without Recalculating Queue Limits")]
    public void WriterScalars_ApplyIndependently()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(static options =>
            {
                options.IndexWriter.RamBufferSizeMB = 128;
                options.IndexWriter.RamPerThreadHardLimitMB = 64;
                options.IndexWriter.MaxConcurrentFlushes = 2;
                options.IndexWriter.MaxBufferedDocs = 100;
                options.IndexWriter.MaxQueuedDocs = 250;
                options.IndexWriter.MaxQueuedBytes = 123_456;
                options.IndexWriter.StorePayloads = true;
                options.IndexWriter.StoreTermVectors = true;
                options.IndexWriter.UseCompoundFile = true;
                options.IndexWriter.DurableCommits = false;
                options.IndexWriter.CompressionPolicy = FieldCompressionPolicy.None;
                options.IndexWriter.StoredFieldBlockSize = 32;
                options.IndexWriter.PostingsSkipInterval = 64;
                options.IndexWriter.BKDMaxLeafSize = 1_024;
                options.IndexWriter.AnalyserInternCacheSize = 128;
                options.IndexWriter.MaxTokensPerDocument = 10;
                options.IndexWriter.TokenBudgetPolicy = TokenBudgetPolicy.Reject;
                options.IndexWriter.MergeThreshold = 7;
                options.IndexWriter.MergeThrottleSegments = 3;
                options.IndexWriter.MaxConcurrentMerges = 2;
                options.IndexWriter.MaxPendingMergeBytes = 2_000_000;
                options.IndexWriter.NormaliseVectors = false;
                options.IndexWriter.VectorQuantisation = VectorQuantisation.Int8;
                options.IndexWriter.BuildHnswOnFlush = false;
                options.IndexWriter.HnswSeed = 42;
                options.IndexWriter.TrackSequenceNumbers = true;
                options.IndexWriter.SoftDeletesEnabled = true;
                options.IndexWriter.SoftDeleteRetentionSeconds = 60;
                options.IndexWriter.Hnsw.M = 24;
                options.IndexWriter.Hnsw.EfConstruction = 200;
                options.IndexWriter.Hnsw.M0 = 48;
            });

            var config = new IndexWriterConfig();

            Assert.Equal(128, config.RamBufferSizeMB);
            Assert.Equal(64, config.RamPerThreadHardLimitMB);
            Assert.Equal(2, config.MaxConcurrentFlushes);
            Assert.Equal(100, config.MaxBufferedDocs);
            Assert.Equal(250, config.MaxQueuedDocs);
            Assert.Equal(123_456, config.MaxQueuedBytes);
            Assert.True(config.StorePayloads);
            Assert.True(config.StoreTermVectors);
            Assert.True(config.UseCompoundFile);
            Assert.False(config.DurableCommits);
            Assert.Equal(FieldCompressionPolicy.None, config.CompressionPolicy);
            Assert.Equal(32, config.StoredFieldBlockSize);
            Assert.Equal(64, config.PostingsSkipInterval);
            Assert.Equal(1_024, config.BKDMaxLeafSize);
            Assert.Equal(128, config.AnalyserInternCacheSize);
            Assert.Equal(10, config.MaxTokensPerDocument);
            Assert.Equal(TokenBudgetPolicy.Reject, config.TokenBudgetPolicy);
            Assert.Equal(7, config.MergeThreshold);
            Assert.Equal(3, config.MergeThrottleSegments);
            Assert.Equal(2, config.MaxConcurrentMerges);
            Assert.Equal(2_000_000, config.MaxPendingMergeBytes);
            Assert.False(config.NormaliseVectors);
            Assert.Equal(VectorQuantisation.Int8, config.VectorQuantisation);
            Assert.False(config.BuildHnswOnFlush);
            Assert.Equal(42, config.HnswSeed);
            Assert.True(config.TrackSequenceNumbers);
            Assert.True(config.SoftDeletesEnabled);
            Assert.Equal(60, config.SoftDeleteRetentionSeconds);
            Assert.Equal(24, config.HnswBuildConfig.M);
            Assert.Equal(200, config.HnswBuildConfig.EfConstruction);
            Assert.Equal(48, config.HnswBuildConfig.M0);
            Assert.Equal(7, Assert.IsType<TieredMergePolicy>(config.MergePolicy).SegmentsPerTier);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Searcher And Manager Graph Capture One Snapshot")]
    public void SearcherManager_UsesOneCapturedSnapshot()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(static options =>
            {
                options.IndexOpen.CompatibilityMode = IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility;
                options.IndexSearcher.ParallelSearch = true;
                options.IndexSearcher.MaxConcurrency = 3;
                options.IndexSearcher.EnableBlockMaxWand = true;
                options.IndexSearcher.MaxCachedSegmentReaders = 12;
                options.IndexSearcher.QueryCache.Enabled = true;
                options.IndexSearcher.QueryCache.MaxEntries = 99;
                options.SearcherManager.RefreshInterval = TimeSpan.FromMilliseconds(250);
            });

            var config = new SearcherManagerConfig();
            Assert.Equal(IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility, config.CompatibilityMode);
            Assert.Equal(config.CompatibilityMode, config.SearcherConfig.CompatibilityMode);
            Assert.True(config.SearcherConfig.ParallelSearch);
            Assert.Equal(3, config.SearcherConfig.MaxConcurrency);
            Assert.True(config.SearcherConfig.EnableBlockMaxWand);
            Assert.Equal(12, config.SearcherConfig.MaxCachedSegmentReaders);
            Assert.True(config.SearcherConfig.EnableQueryCache);
            Assert.Equal(99, config.SearcherConfig.QueryCacheMaxEntries);
            Assert.Equal(TimeSpan.FromMilliseconds(250), config.RefreshInterval);

            var standalone = new IndexSearcherConfig();
            Assert.Equal(config.SearcherConfig.CompatibilityMode, standalone.CompatibilityMode);
            Assert.Equal(config.SearcherConfig.QueryCacheMaxEntries, standalone.QueryCacheMaxEntries);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Local Manager Compatibility Override Does Not Rewrite Nested Searcher")]
    public void SearcherManager_LocalCompatibilityOverridesRemainIndependent()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(static options =>
                options.IndexOpen.CompatibilityMode = IndexOpenCompatibilityMode.AllowSupportedOlderFormats);

            var config = new SearcherManagerConfig
            {
                CompatibilityMode = IndexOpenCompatibilityMode.Strict,
            };

            Assert.Equal(IndexOpenCompatibilityMode.Strict, config.CompatibilityMode);
            Assert.Equal(IndexOpenCompatibilityMode.AllowSupportedOlderFormats, config.SearcherConfig.CompatibilityMode);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Codec Catalogue Reaches Approved Targets Only")]
    public void CodecCatalogue_AppliesToApprovedTargets()
    {
        RunWithRestoredSnapshot(() =>
        {
            var catalog = new CodecCatalogBuilder().AddBuiltIns().Build();
            LeanCorpusDefaults.Configure(options => options.Codecs.Catalog = catalog);

            Assert.Same(catalog, new IndexWriterConfig().CodecCatalog);
            Assert.Same(catalog, new IndexSearcherConfig().CodecCatalog);
            Assert.Same(catalog, new IndexCheckOptions().Catalog);
            Assert.Same(catalog, new IndexCompatibilityOptions().Catalog);
            Assert.Same(catalog, new IndexFormatInspectionOptions().Catalog);
            Assert.Same(catalog, new IndexCodecMigrationOptions().Catalog);

            var local = new IndexSearcherConfig { CodecCatalog = CodecCatalog.Default };
            Assert.Same(CodecCatalog.Default, local.CodecCatalog);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: JSON Defaults Apply After Earlier Default Access")]
    public void JsonDefaults_AreNotFrozenByEarlierAccess()
    {
        RunWithRestoredSnapshot(() =>
        {
            _ = JsonDocumentMapper.FromJsonString("{\"before\":true}");
            LeanCorpusDefaults.Configure(static options =>
            {
                options.JsonMapping.FieldNameSeparator = "/";
                options.JsonMapping.StringFieldMaxLength = 2;
            });

            var document = JsonDocumentMapper.FromJsonString("{\"person\":{\"name\":\"long\"}}");
            Assert.IsType<TextField>(document.GetField("person/name"));
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Search Resource Defaults Remain Local When Explicitly Set")]
    public void SearchDefaults_ApplyToFutureOptionsOnly()
    {
        RunWithRestoredSnapshot(() =>
        {
            var before = SearchOptions.Default;
            LeanCorpusDefaults.Configure(static options =>
            {
                options.Search.MaxResultBytes = 512;
                options.Search.Timeout = TimeSpan.FromSeconds(2);
                options.Search.Hnsw.Ef = 80;
                options.Search.Hnsw.MaxPostFilterRetries = 5;
            });

            var after = SearchOptions.Default;
            var hnsw = new HnswSearchOptions();
            var vectorQuery = new VectorQuery("vector", [1f, 0f], topK: 5);
            var explicitVectorQuery = new VectorQuery("vector", [1f, 0f], topK: 5, efSearch: 7);
            Assert.Equal(long.MaxValue, before.MaxResultBytes);
            Assert.Equal(512, after.MaxResultBytes);
            Assert.Equal(TimeSpan.FromSeconds(2), after.Timeout);
            Assert.Equal(80, hnsw.Ef);
            Assert.Equal(5, hnsw.MaxPostFilterRetries);
            Assert.Equal(0, hnsw.TopK);
            Assert.Equal(80, vectorQuery.EfSearch);
            Assert.Equal(7, explicitVectorQuery.EfSearch);

            var local = new SearchOptions { MaxResultBytes = 1, Timeout = TimeSpan.FromMilliseconds(1) };
            var localHnsw = new HnswSearchOptions { Ef = 7, MaxPostFilterRetries = 1, TopK = 2 };
            Assert.Equal(1, local.MaxResultBytes);
            Assert.Equal(TimeSpan.FromMilliseconds(1), local.Timeout);
            Assert.Equal(7, localHnsw.Ef);
            Assert.Equal(1, localHnsw.MaxPostFilterRetries);
            Assert.Equal(2, localHnsw.TopK);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Analysis Collections And Factories Are Isolated")]
    public void AnalysisDefaults_CopyCollectionsAndCreateFreshInstances()
    {
        RunWithRestoredSnapshot(() =>
        {
            var analyserCalls = 0;
            var fieldCalls = 0;
            var filterCalls = 0;
            var stopWords = new List<string> { "the", "a" };
            LeanCorpusDefaults.Configure(options =>
            {
                options.IndexWriter.Analysis.DefaultAnalyserFactory = () =>
                {
                    Interlocked.Increment(ref analyserCalls);
                    return new KeywordAnalyser();
                };
                options.IndexWriter.Analysis.ForField("title", () =>
                {
                    Interlocked.Increment(ref fieldCalls);
                    return new WhitespaceAnalyser();
                });
                options.IndexWriter.Analysis.StopWords = stopWords;
                options.IndexWriter.Analysis.AddCharFilter(() =>
                {
                    Interlocked.Increment(ref filterCalls);
                    return new IdentityCharFilter();
                });
            });
            stopWords[0] = "changed-after-publication";

            var first = new IndexWriterConfig();
            var second = new IndexWriterConfig();

            Assert.Equal(2, analyserCalls);
            Assert.Equal(2, fieldCalls);
            Assert.Equal(2, filterCalls);
            Assert.NotSame(first.DefaultAnalyser, second.DefaultAnalyser);
            Assert.NotSame(first.FieldAnalysers["title"], second.FieldAnalysers["title"]);
            Assert.NotSame(first.CharFilters[0], second.CharFilters[0]);
            Assert.Equal("the", first.StopWords![0]);
            Assert.Equal("the", second.StopWords![0]);

            first.FieldAnalysers["other"] = new KeywordAnalyser();
            Assert.False(second.FieldAnalysers.ContainsKey("other"));
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Policy And Scoring Factories Preserve Local Precedence")]
    public void PolicyAndScoringFactories_UseFreshDefaultsAndLocalWins()
    {
        RunWithRestoredSnapshot(() =>
        {
            var deletionCalls = 0;
            var mergeCalls = 0;
            var scoringCalls = 0;
            LeanCorpusDefaults.Configure(options =>
            {
                options.IndexWriter.MergeThreshold = 7;
                options.IndexWriter.DeletionPolicyFactory = () =>
                {
                    Interlocked.Increment(ref deletionCalls);
                    return new KeepLatestCommitPolicy();
                };
                options.IndexWriter.MergePolicyFactory = () =>
                {
                    Interlocked.Increment(ref mergeCalls);
                    return new TieredMergePolicy(3);
                };
                options.Scoring.SimilarityFactory = () =>
                {
                    Interlocked.Increment(ref scoringCalls);
                    return TfIdfSimilarity.Instance;
                };
            });

            var first = new IndexWriterConfig();
            var second = new IndexWriterConfig();
            var searcher = new IndexSearcherConfig();
            var localPolicy = new KeepLatestCommitPolicy();
            var localMerge = NoMergePolicy.Instance;
            var local = new IndexWriterConfig
            {
                DeletionPolicy = localPolicy,
                MergePolicy = localMerge,
                Similarity = Bm25Similarity.Instance,
            };

            Assert.Equal(3, deletionCalls);
            Assert.Equal(3, mergeCalls);
            Assert.Equal(4, scoringCalls);
            Assert.NotSame(first.DeletionPolicy, second.DeletionPolicy);
            Assert.NotSame(first.MergePolicy, second.MergePolicy);
            Assert.Same(TfIdfSimilarity.Instance, first.Similarity);
            Assert.Same(TfIdfSimilarity.Instance, searcher.Similarity);
            Assert.Same(localPolicy, local.DeletionPolicy);
            Assert.Same(localMerge, local.MergePolicy);
            Assert.Same(Bm25Similarity.Instance, local.Similarity);
            Assert.Equal(3, Assert.IsType<TieredMergePolicy>(first.MergePolicy).SegmentsPerTier);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Diagnostic Factories Create Independent Configuration Resources")]
    public void DiagnosticFactories_CreateIndependentResources()
    {
        RunWithRestoredSnapshot(() =>
        {
            var metricsCalls = 0;
            var logCalls = 0;
            var analyticsCalls = 0;
            LeanCorpusDefaults.Configure(options =>
            {
                options.Diagnostics.MetricsCollectorFactory = () =>
                {
                    Interlocked.Increment(ref metricsCalls);
                    return NullMetricsCollector.Instance;
                };
                options.Diagnostics.SlowQueryLogFactory = () =>
                {
                    Interlocked.Increment(ref logCalls);
                    return new SlowQueryLog(1_000, TextWriter.Null);
                };
                options.Diagnostics.SearchAnalyticsFactory = () =>
                {
                    Interlocked.Increment(ref analyticsCalls);
                    return new SearchAnalytics(8);
                };
            });

            var writer = new IndexWriterConfig();
            var first = new IndexSearcherConfig();
            var second = new IndexSearcherConfig();

            Assert.Equal(3, metricsCalls);
            Assert.Equal(2, logCalls);
            Assert.Same(NullMetricsCollector.Instance, writer.Metrics);
            Assert.NotSame(first.SlowQueryLog, second.SlowQueryLog);
            Assert.NotSame(first.SearchAnalytics, second.SearchAnalytics);

            first.SlowQueryLog!.Dispose();
            second.SlowQueryLog!.Dispose();
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Manager Diagnostics Survive Refresh And Dispose With Manager")]
    public void DiagnosticFactories_ManagerRetainsAndDisposesOwnedLog()
    {
        RunWithRestoredSnapshot(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"lc-defaults-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            try
            {
                var output = new StringWriter();
                int logCalls = 0;
                LeanCorpusDefaults.Configure(options =>
                    options.Diagnostics.SlowQueryLogFactory = () =>
                    {
                        Interlocked.Increment(ref logCalls);
                        return new SlowQueryLog(0, output, ownsWriter: true);
                    });

                using var directory = new MMapDirectory(path);
                using var writer = new IndexWriter(directory, new IndexWriterConfig());
                writer.AddDocument(Document("first"));
                writer.Commit();

                var managerConfig = new SearcherManagerConfig
                {
                    RefreshInterval = TimeSpan.FromMinutes(5),
                };
                using var manager = new SearcherManager(directory, managerConfig);
                var log = managerConfig.SearcherConfig.SlowQueryLog;
                Assert.Equal(1, logCalls);

                writer.AddDocument(Document("second"));
                writer.Commit();
                Assert.True(manager.MaybeRefresh());

                Assert.Equal(1, logCalls);
                Assert.Same(log, managerConfig.SearcherConfig.SlowQueryLog);

                manager.Dispose();
                Assert.Throws<ObjectDisposedException>(() => output.Write("after dispose"));
            }
            finally
            {
                TestDirectoryFixture.TryDeleteDirectory(path);
            }
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Factory Failure Leaves Published Defaults Usable")]
    public void FactoryFailure_DoesNotCorruptPublishedDefaults()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(options =>
                options.Diagnostics.MetricsCollectorFactory = () =>
                    throw new InvalidOperationException("factory failure"));

            Assert.Throws<InvalidOperationException>(() => new IndexSearcherConfig());

            LeanCorpusDefaults.Configure(static options => options.Diagnostics.MetricsCollectorFactory = null);
            Assert.Same(NullMetricsCollector.Instance, new IndexSearcherConfig().Metrics);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Invalid Effective State Is Not Published")]
    public void InvalidCandidate_DoesNotReplacePreviousSnapshot()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.MaxConcurrentFlushes = 2);

            Assert.Throws<ArgumentException>(() =>
                LeanCorpusDefaults.Configure(static options => options.IndexWriter.MaxConcurrentFlushes = 0));

            Assert.Equal(2, new IndexWriterConfig().MaxConcurrentFlushes);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Cross-Property Validation Matches Writer Validation")]
    public void CandidateValidation_UsesWriterRules()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.MaxBufferedDocs = 10_000);
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.RamBufferSizeMB = 0);
            Assert.Equal(0, new IndexWriterConfig().RamBufferSizeMB);

            Assert.Throws<ArgumentException>(() => LeanCorpusDefaults.Configure(static options =>
            {
                options.IndexWriter.RamBufferSizeMB = 0;
                options.IndexWriter.MaxBufferedDocs = 0;
            }));
            Assert.Equal(0, new IndexWriterConfig().RamBufferSizeMB);
            Assert.Equal(10_000, new IndexWriterConfig().MaxBufferedDocs);

            Assert.Throws<ArgumentException>(() => LeanCorpusDefaults.Configure(static options =>
            {
                options.IndexWriter.SoftDeletesEnabled = true;
                options.IndexWriter.SoftDeleteRetentionSeconds = 0;
            }));
            Assert.False(new IndexWriterConfig().SoftDeletesEnabled);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Callback And Nested Configure Fail Without Publication")]
    public void FailedConfigure_DoesNotPublishOrCorruptSnapshot()
    {
        RunWithRestoredSnapshot(() =>
        {
            LeanCorpusDefaults.Configure(static options => options.IndexWriter.MaxBufferedDocs = 11);

            Assert.Throws<InvalidOperationException>(() => LeanCorpusDefaults.Configure(options =>
            {
                options.IndexWriter.MaxBufferedDocs = 12;
                LeanCorpusDefaults.Configure(static nested => nested.IndexWriter.MaxBufferedDocs = 13);
            }));
            Assert.Equal(11, new IndexWriterConfig().MaxBufferedDocs);

            Assert.Throws<InvalidOperationException>(() => LeanCorpusDefaults.Configure(static _ =>
                throw new InvalidOperationException("callback failure")));
            Assert.Equal(11, new IndexWriterConfig().MaxBufferedDocs);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Concurrent Disjoint Updates Are Additive")]
    public void ConcurrentConfigure_DisjointUpdatesSurvive()
    {
        RunWithRestoredSnapshot(() =>
        {
            var first = Task.Run(() => LeanCorpusDefaults.Configure(static options =>
                options.IndexWriter.RamBufferSizeMB = 123));
            var second = Task.Run(() => LeanCorpusDefaults.Configure(static options =>
                options.IndexWriter.MaxBufferedDocs = 456));
            Task.WaitAll(first, second);

            var config = new IndexWriterConfig();
            Assert.Equal(123, config.RamBufferSizeMB);
            Assert.Equal(456, config.MaxBufferedDocs);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Reset Serialises After An In-Flight Configure")]
    public void ConcurrentReset_UsesCompletePublicationOrder()
    {
        RunWithRestoredSnapshot(() =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var configure = Task.Run(() => LeanCorpusDefaults.Configure(options =>
            {
                options.IndexWriter.MaxBufferedDocs = 777;
                entered.Set();
                release.Wait();
            }));

            entered.Wait();
            var reset = Task.Run(LeanCorpusDefaults.Reset);
            release.Set();
            Task.WaitAll(configure, reset);

            Assert.Equal(10_000, new IndexWriterConfig().MaxBufferedDocs);
        });
    }

    [Fact(DisplayName = "LeanCorpus Defaults: Nullable Override Representation Distinguishes Unset From Null")]
    public void NullableOverrides_DistinguishUnsetAndConfiguredNull()
    {
        var unset = DefaultOverride<string?>.Unset;
        var configuredNull = DefaultOverride<string?>.Set(null);

        Assert.False(unset.IsSet);
        Assert.True(configuredNull.IsSet);
        Assert.Null(configuredNull.Value);
    }

    private static void RunWithRestoredSnapshot(Action action)
    {
        var snapshot = LeanCorpusDefaults.CaptureSnapshotForTests();
        try
        {
            action();
        }
        finally
        {
            LeanCorpusDefaults.RestoreSnapshotForTests(snapshot);
        }
    }

    private static LeanDocument Document(string value)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", value));
        return document;
    }

    private sealed class IdentityCharFilter : ICharFilter
    {
        public string Filter(ReadOnlySpan<char> input) => input.ToString();
    }
}
