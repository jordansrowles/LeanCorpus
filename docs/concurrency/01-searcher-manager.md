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

```mermaid-latest
sequenceDiagram
    participant Timer
    participant Manager as SearcherManager
    participant Store as Index directory
    participant Caller

    Timer->>Manager: Refresh poll
    Manager->>Store: Read latest commit generation
    alt No newer commit
        Manager->>Manager: Keep current searcher
    else Newer commit
        Manager->>Store: Open replacement searcher
        Manager->>Manager: Swap current searcher
        Caller->>Manager: AcquireLease()
        Manager-->>Caller: Lease on current searcher
        Caller->>Manager: Dispose lease
        Manager->>Manager: Dispose old searcher after final lease
    end
```

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

The manager opens and validates the replacement before publication. A failed refresh leaves the previous healthy searcher available.

## Query cache across refresh

When query caching is enabled, the manager owns one shared `QueryCache`. Refresh invalidates its generation so old document IDs cannot be returned, while hit and miss counters continue across replacement searchers.

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
