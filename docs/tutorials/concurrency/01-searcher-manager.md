# Searcher manager

`SearcherManager` keeps a current `IndexSearcher` open and swaps in a fresh one
when a new commit is detected. Use it to share a searcher across many concurrent
queries.

## Set up

```csharp
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

using var dir = new MMapDirectory("./index");
using var manager = new SearcherManager(dir, new SearcherManagerConfig
{
    RefreshInterval = TimeSpan.FromSeconds(1),
    SearcherConfig  = new IndexSearcherConfig { EnableQueryCache = true },
});
```

A background loop polls for new commits at `RefreshInterval`.

## Acquire and release

Prefer `AcquireLease` for new code:

```csharp
using var lease = manager.AcquireLease();
var hits = lease.Searcher.Search(query, 10);
```

The older acquire/release pair is still available:

```csharp
var searcher = manager.Acquire();
try
{
    var hits = searcher.Search(query, 10);
}
finally
{
    manager.Release(searcher);
}
```

Or use the convenience method:

```csharp
var hits = manager.UsingSearcher(s => s.Search(query, 10));
```

## Forcing a refresh

```csharp
bool refreshed = manager.MaybeRefresh();          // sync
bool refreshedAsync = await manager.MaybeRefreshAsync();
```

Returns `true` when a newer commit was loaded.

## Refresh failures

Refresh errors are captured instead of crashing the background loop:

```csharp
manager.RefreshFailed += (_, e) =>
    logger.LogWarning(e.Error, "Searcher refresh failed {Count} time(s)", e.ConsecutiveFailures);

if (manager.LastRefreshError is not null)
    Console.Error.WriteLine(manager.LastRefreshError.Message);
```

## See also

- [Refresh failures](04-refresh-failures.md)
- <xref:Rowles.LeanCorpus.Search.Searcher.SearcherManager>
- <xref:Rowles.LeanCorpus.Search.Searcher.SearcherManagerConfig>
