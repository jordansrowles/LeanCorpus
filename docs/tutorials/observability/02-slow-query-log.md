# Slow query log

`SlowQueryLog` writes one JSON line per query that exceeds a latency threshold.

## Wire it up

```csharp
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Search.Searcher;

using var slowLog = SlowQueryLog.ToFile(
    thresholdMs: 50.0,
    filePath: "./slow-queries.jsonl");

var config = new IndexSearcherConfig { SlowQueryLog = slowLog };
using var searcher = new IndexSearcher(dir, config);
```

Or write to any `TextWriter`:

```csharp
var writer = new StringWriter();
var log = new SlowQueryLog(thresholdMs: 25.0, writer);
```

## Log entry

Each line contains: `Timestamp` (UTC), `QueryType`, `Query` (string form),
`ElapsedMs`, `TotalHits`.

## Reading entries

The file is JSON Lines. Parse each line with the schema above.

## See also

- <xref:Rowles.LeanCorpus.Diagnostics.SlowQueryLog>
