using System.Diagnostics;

namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// Shared <see cref="ActivitySource"/> for LeanLucene instrumentation.
/// Activities are only allocated when a listener is attached — zero overhead otherwise.
/// </summary>
internal static class LeanLuceneActivitySource
{
    internal static readonly ActivitySource Source = new("Rowles.LeanLucene");

    internal const string Search = "leanlucene.search";
    internal const string Commit = "leanlucene.index.commit";
    internal const string Flush = "leanlucene.index.flush";
    internal const string Merge = "leanlucene.index.merge";
}
