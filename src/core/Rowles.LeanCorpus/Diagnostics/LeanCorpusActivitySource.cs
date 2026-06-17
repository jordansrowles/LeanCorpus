using System.Diagnostics;

namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// Shared <see cref="ActivitySource"/> for LeanCorpus instrumentation.
/// Activities are only allocated when a listener is attached — zero overhead otherwise.
/// </summary>
internal static class LeanCorpusActivitySource
{
    internal static readonly ActivitySource Source = new("Rowles.LeanCorpus");

    internal const string Search = "leancorpus.search";
    internal const string Commit = "leancorpus.index.commit";
    internal const string Flush = "leancorpus.index.flush";
    internal const string Merge = "leancorpus.index.merge";
    internal const string FormatInspect = "leancorpus.index.format.inspect";
    internal const string CodecMigrationPlan = "leancorpus.index.codec_migration.plan";
    internal const string CodecMigrationMigrate = "leancorpus.index.codec_migration.migrate";
    internal const string BackupManifest = "leancorpus.index.backup.manifest";
    internal const string BackupCopy = "leancorpus.index.backup.copy";
    internal const string BackupValidate = "leancorpus.index.backup.validate";
    internal const string BackupRestore = "leancorpus.index.backup.restore";
    internal const string AddDocument = "leancorpus.index.add_document";
    internal const string Analyse = "leancorpus.index.analyse";
    internal const string DeleteQueue = "leancorpus.index.delete_queue";
    internal const string DeleteApply = "leancorpus.index.delete_apply";

    /// <summary>
    /// Records an exception that is being intentionally swallowed so it surfaces in
    /// diagnostic tooling. Writes to <see cref="Debug"/> unconditionally and emits
    /// an <see cref="ActivityEvent"/> when a listener is attached.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static void TraceSwallowed(Exception exception, string context)
    {
        Debug.WriteLine($"LeanCorpus swallowed {exception.GetType().Name} during {context}: {exception.Message}");
    }
}
