using Rowles.LeanCorpus.Index;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Configuration for <see cref="SearcherManager"/>.
/// </summary>
public sealed class SearcherManagerConfig
{
    /// <summary>Initialises a configuration and captures one process-wide defaults snapshot for the graph.</summary>
    public SearcherManagerConfig()
        : this(LeanCorpusDefaults.GetSnapshot(), applyFactories: true)
    {
    }

    internal SearcherManagerConfig(LeanCorpusDefaultSnapshot snapshot, bool applyFactories)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RefreshInterval = Effective(snapshot.SearcherManager.RefreshInterval, TimeSpan.FromSeconds(1));
        CompatibilityMode = Effective(snapshot.IndexOpen.CompatibilityMode, IndexOpenCompatibilityMode.Strict);
        SearcherConfig = new IndexSearcherConfig(snapshot, applyFactories);
    }

    private static T Effective<T>(DefaultOverride<T> value, T builtIn)
        => value.IsSet ? value.Value : builtIn;

    internal void Validate()
    {
        if (RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshInterval), RefreshInterval,
                "RefreshInterval must be greater than zero.");
        SearcherConfig.Validate();
    }

    /// <summary>How often to poll for new commits. Default: 1 second.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Searcher configuration applied to each newly opened IndexSearcher.</summary>
    public IndexSearcherConfig SearcherConfig { get; set; }

    /// <summary>
    /// Compatibility guardrail applied before refresh checks inspect commit metadata.
    /// Defaults to strict mode.
    /// </summary>
    public IndexOpenCompatibilityMode CompatibilityMode { get; set; } = IndexOpenCompatibilityMode.Strict;
}
