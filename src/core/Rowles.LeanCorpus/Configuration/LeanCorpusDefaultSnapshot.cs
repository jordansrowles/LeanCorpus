using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus;

internal sealed record LeanCorpusDefaultSnapshot
{
    internal CodecDefaultsSnapshot Codecs { get; init; } = CodecDefaultsSnapshot.BuiltIn;
    internal IndexOpenDefaultsSnapshot IndexOpen { get; init; } = IndexOpenDefaultsSnapshot.BuiltIn;
    internal IndexWriterDefaultsSnapshot IndexWriter { get; init; } = IndexWriterDefaultsSnapshot.BuiltIn;
    internal IndexSearcherDefaultsSnapshot IndexSearcher { get; init; } = IndexSearcherDefaultsSnapshot.BuiltIn;
    internal SearcherManagerDefaultsSnapshot SearcherManager { get; init; } = SearcherManagerDefaultsSnapshot.BuiltIn;
    internal JsonMappingDefaultsSnapshot JsonMapping { get; init; } = JsonMappingDefaultsSnapshot.BuiltIn;
    internal ScoringDefaultsSnapshot Scoring { get; init; } = ScoringDefaultsSnapshot.BuiltIn;
    internal DiagnosticsDefaultsSnapshot Diagnostics { get; init; } = DiagnosticsDefaultsSnapshot.BuiltIn;
    internal SearchDefaultsSnapshot Search { get; init; } = SearchDefaultsSnapshot.BuiltIn;

    internal static LeanCorpusDefaultSnapshot BuiltIn { get; } = new();
}

internal sealed record CodecDefaultsSnapshot
{
    internal DefaultOverride<CodecCatalog> Catalog { get; init; } = DefaultOverride<CodecCatalog>.Unset;

    internal static CodecDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record IndexOpenDefaultsSnapshot
{
    internal DefaultOverride<IndexOpenCompatibilityMode> CompatibilityMode { get; init; } = DefaultOverride<IndexOpenCompatibilityMode>.Unset;

    internal static IndexOpenDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record IndexWriterDefaultsSnapshot
{
    internal DefaultOverride<double> RamBufferSizeMB { get; init; } = DefaultOverride<double>.Unset;
    internal DefaultOverride<double> RamPerThreadHardLimitMB { get; init; } = DefaultOverride<double>.Unset;
    internal DefaultOverride<int> MaxConcurrentFlushes { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> MaxBufferedDocs { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> MaxQueuedDocs { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<long> MaxQueuedBytes { get; init; } = DefaultOverride<long>.Unset;
    internal DefaultOverride<bool> StorePayloads { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<bool> StoreTermVectors { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<bool> UseCompoundFile { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<bool> DurableCommits { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<FieldCompressionPolicy> CompressionPolicy { get; init; } = DefaultOverride<FieldCompressionPolicy>.Unset;
    internal DefaultOverride<int> StoredFieldBlockSize { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> PostingsSkipInterval { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> BKDMaxLeafSize { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> AnalyserInternCacheSize { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> MaxTokensPerDocument { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<TokenBudgetPolicy> TokenBudgetPolicy { get; init; } = DefaultOverride<TokenBudgetPolicy>.Unset;
    internal DefaultOverride<int> MergeThreshold { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> MergeThrottleSegments { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> MaxConcurrentMerges { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<long> MaxPendingMergeBytes { get; init; } = DefaultOverride<long>.Unset;
    internal DefaultOverride<bool> NormaliseVectors { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<VectorQuantisation> VectorQuantisation { get; init; } = DefaultOverride<VectorQuantisation>.Unset;
    internal DefaultOverride<bool> BuildHnswOnFlush { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<long?> HnswSeed { get; init; } = DefaultOverride<long?>.Unset;
    internal DefaultOverride<bool> TrackSequenceNumbers { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<bool> SoftDeletesEnabled { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<double> SoftDeleteRetentionSeconds { get; init; } = DefaultOverride<double>.Unset;
    internal DefaultOverride<Func<IIndexDeletionPolicy>> DeletionPolicyFactory { get; init; } = DefaultOverride<Func<IIndexDeletionPolicy>>.Unset;
    internal DefaultOverride<Func<IMergePolicy>> MergePolicyFactory { get; init; } = DefaultOverride<Func<IMergePolicy>>.Unset;
    internal HnswBuildDefaultsSnapshot Hnsw { get; init; } = HnswBuildDefaultsSnapshot.BuiltIn;
    internal AnalysisDefaultsSnapshot Analysis { get; init; } = AnalysisDefaultsSnapshot.BuiltIn;

    internal static IndexWriterDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record HnswBuildDefaultsSnapshot
{
    internal DefaultOverride<int> M { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> EfConstruction { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> M0 { get; init; } = DefaultOverride<int>.Unset;

    internal static HnswBuildDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record AnalysisDefaultsSnapshot
{
    internal DefaultOverride<Func<IAnalyser>> DefaultAnalyserFactory { get; init; } = DefaultOverride<Func<IAnalyser>>.Unset;
    internal IReadOnlyDictionary<string, Func<IAnalyser>> FieldAnalyserFactories { get; init; } =
        new Dictionary<string, Func<IAnalyser>>(StringComparer.Ordinal);
    internal DefaultOverride<string[]> StopWords { get; init; } = DefaultOverride<string[]>.Unset;
    internal IReadOnlyList<Func<ICharFilter>> CharFilterFactories { get; init; } = Array.Empty<Func<ICharFilter>>();

    internal static AnalysisDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record IndexSearcherDefaultsSnapshot
{
    internal DefaultOverride<bool> ParallelSearch { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<int> MaxConcurrency { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<bool> EnableBlockMaxWand { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<int> MaxCachedSegmentReaders { get; init; } = DefaultOverride<int>.Unset;
    internal QueryCacheDefaultsSnapshot QueryCache { get; init; } = QueryCacheDefaultsSnapshot.BuiltIn;

    internal static IndexSearcherDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record QueryCacheDefaultsSnapshot
{
    internal DefaultOverride<bool> Enabled { get; init; } = DefaultOverride<bool>.Unset;
    internal DefaultOverride<int> MaxEntries { get; init; } = DefaultOverride<int>.Unset;

    internal static QueryCacheDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record SearcherManagerDefaultsSnapshot
{
    internal DefaultOverride<TimeSpan> RefreshInterval { get; init; } = DefaultOverride<TimeSpan>.Unset;

    internal static SearcherManagerDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record JsonMappingDefaultsSnapshot
{
    internal DefaultOverride<string> FieldNameSeparator { get; init; } = DefaultOverride<string>.Unset;
    internal DefaultOverride<int> MaxDepth { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> StringFieldMaxLength { get; init; } = DefaultOverride<int>.Unset;

    internal static JsonMappingDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record ScoringDefaultsSnapshot
{
    internal DefaultOverride<Func<ISimilarity>> SimilarityFactory { get; init; } = DefaultOverride<Func<ISimilarity>>.Unset;
    internal IReadOnlyDictionary<string, Func<ISimilarity>> PerFieldSimilarityFactories { get; init; } =
        new Dictionary<string, Func<ISimilarity>>(StringComparer.Ordinal);

    internal static ScoringDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record DiagnosticsDefaultsSnapshot
{
    internal DefaultOverride<Func<Diagnostics.IMetricsCollector>> MetricsCollectorFactory { get; init; } = DefaultOverride<Func<Diagnostics.IMetricsCollector>>.Unset;
    internal DefaultOverride<Func<Diagnostics.SlowQueryLog?>> SlowQueryLogFactory { get; init; } = DefaultOverride<Func<Diagnostics.SlowQueryLog?>>.Unset;
    internal DefaultOverride<Func<Diagnostics.SearchAnalytics?>> SearchAnalyticsFactory { get; init; } = DefaultOverride<Func<Diagnostics.SearchAnalytics?>>.Unset;

    internal static DiagnosticsDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record SearchDefaultsSnapshot
{
    internal DefaultOverride<long> MaxResultBytes { get; init; } = DefaultOverride<long>.Unset;
    internal DefaultOverride<TimeSpan?> Timeout { get; init; } = DefaultOverride<TimeSpan?>.Unset;
    internal HnswSearchDefaultsSnapshot Hnsw { get; init; } = HnswSearchDefaultsSnapshot.BuiltIn;

    internal static SearchDefaultsSnapshot BuiltIn { get; } = new();
}

internal sealed record HnswSearchDefaultsSnapshot
{
    internal DefaultOverride<int> Ef { get; init; } = DefaultOverride<int>.Unset;
    internal DefaultOverride<int> MaxPostFilterRetries { get; init; } = DefaultOverride<int>.Unset;

    internal static HnswSearchDefaultsSnapshot BuiltIn { get; } = new();
}
