# Index size and statistics

LeanCorpus exposes an on-disk size report and persists collection statistics used by scoring. They answer different questions.

## Size report

```csharp
using var searcher = new IndexSearcher(directory);
var size = searcher.GetIndexSize();

Console.WriteLine(
    $"{size.TotalSizeFormatted} across {size.SegmentCount} segments");

foreach (var segment in size.Segments)
    Console.WriteLine($"{segment.SegmentName}: {segment.TotalSizeBytes} bytes");
```

`IndexSizeReport` includes:

- total bytes in the directory;
- commit-manifest bytes;
- `stats_N.json` bytes;
- per-segment file and total sizes;
- segment count and total segment-data bytes.

Track size after representative indexing, deletion, and merge cycles. Immediately after a large merge, old files may remain while searchers, snapshots, or retained commits still hold them.

## Collection statistics

`IndexStats` contains:

| Value | Meaning |
|---|---|
| `TotalDocCount` | Documents including deleted entries |
| `LiveDocCount` | Searchable documents |
| `GetAvgFieldLength(field)` | Average indexed token count for a field |
| `GetFieldDocCount(field)` | Documents containing a field |
| `GetFieldLengthSum(field)` | Total indexed tokens for a field |

These values make BM25 scoring comparable across segment boundaries.

## Persistence

Statistics for commit generation `N` are stored as `stats_N.json`. The canonical path is:

```csharp
var path = IndexStats.GetStatsPath(indexPath, generation);
```

`WriteTo` publishes the JSON through a temporary file. `TryLoadFrom` returns `null` when the file is missing or corrupt:

```csharp
var stats = IndexStats.TryLoadFrom(path);
if (stats is null)
{
    // The searcher can recompute statistics from segment data.
}
```

The statistics sidecar is an opening optimisation, not the source of truth. Search remains recoverable without it, although opening may take longer.

Do not copy a statistics file independently of its matching commit. [Backup and restore](../index-management/08-backup-and-restore.md) handles this relationship.
