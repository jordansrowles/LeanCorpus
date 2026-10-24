# Search sessions and cursor pagination

`SearchSessionManager` retains a committed `IndexSearcher` view while a client pages through results. Later commits and refreshes do not change that session.

```csharp
using var searchers = new SearcherManager(directory);
using var sessions = new SearchSessionManager(searchers);
using var session = sessions.OpenSession();

var page = session.Search(
    new TermQuery("body", "coffee"),
    pageSize: 20,
    sorts: [SortField.Numeric("price"), SortField.String("name")]);

while (page.NextCursor is not null)
{
    page = session.Search(
        new TermQuery("body", "coffee"),
        pageSize: 20,
        cursor: page.NextCursor,
        sorts: [SortField.Numeric("price"), SortField.String("name")]);
}
```

Dispose sessions promptly. Abandoned sessions expire automatically.

## Limits

The defaults allow 256 sessions, eight retained generations, 4 GiB of uniquely retained snapshot files, and a 15-minute lifetime. New sessions are rejected when a limit is reached. Set `LimitPolicy` to `EvictOldest` when replacing the oldest session is preferable.

`GetDiagnostics()` reports active sessions, age, generations, retained bytes and files, files awaiting deletion, and lifecycle failures.

## Cursors

Cursors are opaque, versioned and limited to 4 KiB by default. They contain only internal continuation state. They are bound to the session, index, commit generation, query, sort definition and optional ranking identity.

Set `CursorIntegrityKey` to at least 16 bytes to protect tokens with HMAC-SHA256. Keep the key outside logs and configuration files that may be exposed.

Every sort ends with document ID as its final tie-breaker. Numeric and integer fields missing a sort value use `0`; missing strings use an empty value. Non-finite score and numeric boundaries are rejected.

## Ranking compatibility

Pass `rankingIdentity` when application-owned ranking affects compatibility. Basic identity-only `RankingProfile` requests are supported directly. Pipelines, rules, rescorers, fusion, pinning and diversification are rejected until they supply complete continuation state; LeanCorpus does not silently rerank each page independently.

Sessions currently retain committed `SearcherManager` views. Near-real-time sessions are not supported.
