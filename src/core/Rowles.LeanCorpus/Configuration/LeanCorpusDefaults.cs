using System.Threading;

namespace Rowles.LeanCorpus;

/// <summary>
/// Configures process-wide defaults used when new LeanCorpus configuration objects are created.
/// Changes affect only configurations created after publication; existing configurations and
/// active writers retain their captured values.
/// </summary>
public static class LeanCorpusDefaults
{
    private static LeanCorpusDefaultSnapshot s_current = LeanCorpusDefaultSnapshot.BuiltIn;

    /// <summary>Publishes a complete, immutable snapshot of the supplied defaults.</summary>
    /// <param name="configure">Configures a private options builder.</param>
    public static void Configure(Action<LeanCorpusDefaultOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        LeanCorpusDefaultSnapshot current = Volatile.Read(ref s_current);
        var options = new LeanCorpusDefaultOptions(current);
        configure(options);
        Interlocked.Exchange(ref s_current, options.ToSnapshot());
    }

    /// <summary>Restores the built-in defaults for subsequently created configurations.</summary>
    public static void Reset() => Interlocked.Exchange(ref s_current, LeanCorpusDefaultSnapshot.BuiltIn);

    internal static LeanCorpusDefaultSnapshot GetSnapshot() => Volatile.Read(ref s_current);
}

/// <summary>Mutable builder used only while publishing a <see cref="LeanCorpusDefaults"/> snapshot.</summary>
public sealed class LeanCorpusDefaultOptions
{
    internal LeanCorpusDefaultOptions(LeanCorpusDefaultSnapshot snapshot) =>
        IndexWriter = new IndexWriterDefaultOptions { DurableCommits = snapshot.IndexWriterDurableCommits };

    /// <summary>Gets the defaults applied to newly created index-writer configurations.</summary>
    public IndexWriterDefaultOptions IndexWriter { get; }

    internal LeanCorpusDefaultSnapshot ToSnapshot() => new(IndexWriter.DurableCommits);
}

/// <summary>Optional defaults applied to newly created <see cref="Index.Indexer.IndexWriterConfig"/> instances.</summary>
public sealed class IndexWriterDefaultOptions
{
    /// <summary>
    /// Gets or sets the default durable-commit setting. <see langword="null"/> retains the
    /// built-in production default.
    /// </summary>
    public bool? DurableCommits { get; set; }
}

internal sealed record LeanCorpusDefaultSnapshot(bool? IndexWriterDurableCommits)
{
    internal static LeanCorpusDefaultSnapshot BuiltIn { get; } = new((bool?)null);
}
