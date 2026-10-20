using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Exposes Server search observations without transferring physical ownership.</summary>
internal sealed class LocalSearcherAccess(PhysicalIndexRuntime physical)
{
    internal SearcherLease AcquireLease() => physical.AcquireLease();
    internal ReaderManagerDiagnostics GetReaderDiagnostics() => physical.GetReaderDiagnostics();
    internal long ConsecutiveRefreshFailures => physical.ConsecutiveRefreshFailures;
    internal Exception? LastRefreshError => physical.LastRefreshError;
    internal bool Refresh() => physical.Refresh();
    internal bool MaybeRefresh() => physical.Refresh();
}
