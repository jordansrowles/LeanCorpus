using Rowles.LeanCorpus.Index;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Configuration for <see cref="SearcherManager"/>.
/// </summary>
public sealed class SearcherManagerConfig
{
    /// <summary>How often to poll for new commits. Default: 1 second.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Searcher configuration applied to each newly opened IndexSearcher.</summary>
    public IndexSearcherConfig SearcherConfig { get; set; } = new();

    /// <summary>
    /// Compatibility guardrail applied before refresh checks inspect commit metadata.
    /// Defaults to strict mode.
    /// </summary>
    public IndexOpenCompatibilityMode CompatibilityMode { get; set; } = IndexOpenCompatibilityMode.Strict;
}
