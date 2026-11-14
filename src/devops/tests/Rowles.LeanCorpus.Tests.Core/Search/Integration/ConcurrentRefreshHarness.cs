using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Runs a writer that commits and refreshes in rounds while queries run, and proves the overlap
/// happened rather than relying on scheduling. Every reader has run a query before the writer's
/// first commit. A pinned reader holds a lease across each refresh: it queries, waits while the
/// writer commits and refreshes, checks that the manager now serves a different searcher, and
/// queries its retired snapshot again, which must return the same result.
/// </summary>
internal static class ConcurrentRefreshHarness
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(60);

    public static async Task<RefreshOverlap> RunAsync(
        SearcherManager manager,
        int rounds,
        int readers,
        Action<int> writeRound,
        Func<IndexSearcher, int> search,
        Action<int> validate,
        CancellationToken cancellationToken)
    {
        using var readersReady = new CountdownEvent(readers + 1);
        using var pinnedHolding = new SemaphoreSlim(0);
        using var refreshed = new SemaphoreSlim(0);
        int overlaps = 0;
        var queries = new int[readers];

        var writer = Task.Run(() =>
        {
            if (!readersReady.Wait(HandshakeTimeout, cancellationToken))
                throw new TimeoutException("Readers did not start before the writer.");
            for (int round = 0; round < rounds; round++)
            {
                if (!pinnedHolding.Wait(HandshakeTimeout, cancellationToken))
                    throw new TimeoutException($"The pinned reader did not hold a lease for round {round}.");
                writeRound(round);
                manager.MaybeRefresh();
                refreshed.Release();
            }
        }, cancellationToken);

        var pinned = Task.Run(() =>
        {
            readersReady.Signal();
            for (int round = 0; round < rounds; round++)
            {
                using var lease = manager.AcquireLease();
                int before = search(lease.Searcher);
                validate(before);
                pinnedHolding.Release();
                if (!refreshed.Wait(HandshakeTimeout, cancellationToken))
                    throw new TimeoutException($"The writer did not refresh in round {round}.");

                using (var current = manager.AcquireLease())
                {
                    if (ReferenceEquals(current.Searcher, lease.Searcher))
                        throw new InvalidOperationException($"Round {round} refreshed without publishing a new searcher.");
                }

                int after = search(lease.Searcher);
                if (after != before)
                    throw new InvalidOperationException($"Round {round}: the held snapshot returned {after} hits after refresh, {before} before.");
                Interlocked.Increment(ref overlaps);
            }
        }, cancellationToken);

        var workers = Enumerable.Range(0, readers).Select(index => Task.Run(() =>
        {
            void Query()
            {
                using var lease = manager.AcquireLease();
                validate(search(lease.Searcher));
                queries[index]++;
            }

            Query();
            readersReady.Signal();
            while (!writer.IsCompleted)
            {
                Query();
                // Leave the writer and pinned reader room to run their handshake on small hosts.
                Thread.Yield();
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll([writer, pinned, .. workers]);
        return new RefreshOverlap(overlaps, queries);
    }
}

/// <summary>How many refreshes a held lease spanned, and how many queries each reader ran.</summary>
internal sealed record RefreshOverlap(int HeldAcrossRefresh, int[] QueriesPerReader);
