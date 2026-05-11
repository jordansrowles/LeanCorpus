# Refresh failures

`SearcherManager` polls for new commits in the background. If a commit file is
temporarily unreadable or invalid, the manager keeps the current searcher alive
and records the failure.

```csharp
manager.RefreshFailed += (_, e) =>
{
    logger.LogWarning(
        e.Error,
        "Refresh failed {ConsecutiveFailures} time(s)",
        e.ConsecutiveFailures);
};
```

You can also poll the last error:

```csharp
if (manager.LastRefreshError is { } error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(manager.LastRefreshErrorAt);
}
```

`ConsecutiveRefreshFailures` resets after a successful refresh.

## See also

- [Searcher manager](01-searcher-manager.md)
- <xref:Rowles.LeanCorpus.Search.Searcher.RefreshFailedEventArgs>
- <xref:Rowles.LeanCorpus.Search.Searcher.SearcherLease>
