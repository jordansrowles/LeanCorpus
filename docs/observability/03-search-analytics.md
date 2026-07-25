# Search analytics

`SearchAnalytics` is an in-memory ring buffer of recent search events.

```csharp
using Rowles.LeanCorpus.Diagnostics;

var analytics = new SearchAnalytics(capacity: 1000);
var config = new IndexSearcherConfig { SearchAnalytics = analytics };
```

## Read events

```csharp
var recent = analytics.GetRecentEvents(count: 50);
foreach (var e in recent)
    Console.WriteLine($"{e.Timestamp:O} {e.QueryType} {e.ElapsedMs}ms hits={e.TotalHits} cache={e.CacheHit}");
```

`GetRecentEvents` and `DrainEvents` consume entries; events read once are not returned again.

## Export

```csharp
using var writer = new StreamWriter("./events.json");
analytics.ExportJson(writer);
```

Writes a JSON array; drains the buffer.

## Event fields

`Timestamp`, `QueryType`, `Query`, `ElapsedMs`, `TotalHits`, `CacheHit`.

## See also

- <xref:Rowles.LeanCorpus.Diagnostics.SearchAnalytics>
