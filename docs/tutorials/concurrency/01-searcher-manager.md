# Searcher manager

`SearcherManager` keeps a current `IndexSearcher` open and swaps in a fresh one when a new commit lands. Share one searcher across many concurrent queries.

## Setup

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

A background loop polls at `RefreshInterval`.

## Acquire and release

```csharp
using var lease = manager.AcquireLease();
var hits = lease.Searcher.Search(query, 10);
```

Or the convenience method:

```csharp
var hits = manager.UsingSearcher(s => s.Search(query, 10));
```

## Force refresh

```csharp
bool refreshed = manager.MaybeRefresh();
bool refreshedAsync = await manager.MaybeRefreshAsync();
```

Returns `true` when a newer commit was loaded.

## Refresh failures

Errors are captured instead of crashing the background loop:

```csharp
manager.RefreshFailed += (_, e) =>
    logger.LogWarning(e.Error, "Searcher refresh failed {Count} time(s)", e.ConsecutiveFailures);

if (manager.LastRefreshError is not null)
    Console.Error.WriteLine(manager.LastRefreshError.Message);
```

## See also

- [Refresh failures](04-refresh-failures.md)
- <xref:Rowles.LeanCorpus.Search.Searcher.SearcherManager>
