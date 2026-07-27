# Production deployment

Production behaviour is dominated by the index working set, commit and refresh policy, merge headroom, and file lifetime. Establish limits and recovery procedures before increasing throughput.

## Storage and page cache

Use a local filesystem with reliable memory mapping, atomic rename, and durability semantics. Size the host for the hot index working set plus managed heap, thread stacks, native buffers, and operating-system headroom.

Memory-mapped pages appear as resident memory but are reclaimable by the operating system. Do not treat resident set size as managed allocation or set a container limit equal to the managed heap target.

Keep enough free disk for:

- the current committed segments;
- flushed segments waiting to merge;
- merge output before source files can be removed;
- snapshots and retained commits;
- one local restore or deployment switch if that is part of the procedure.

Large merges can temporarily require space comparable to their inputs.

## File descriptors and mappings

Segment-heavy indexes may open many files. Set the process file-descriptor limit above the expected segment-file count with headroom for logs, sockets, and merge activity.

`MaxCachedSegmentReaders` bounds lazily opened readers, but active searches and lifecycle transitions still need descriptors and mappings. Alert on descriptor exhaustion and unusual segment growth.

## One writer

Use one `IndexWriter` per index directory. The write lock prevents competing writers, but deployment orchestration should make ownership explicit.

Keep `DurableCommits = true` when commit acknowledgement must survive host or power loss. If the application chooses non-durable commits, document the accepted recovery-point loss.

## Refresh and searchers

Use `SearcherManager` for a long-running service:

```csharp
using var manager = new SearcherManager(
    indexPath,
    new SearcherManagerConfig
    {
        RefreshInterval = TimeSpan.FromSeconds(1),
    });

using var lease = manager.AcquireLease();
var results = lease.Searcher.Search(query, topN: 20);
```

Always dispose the lease. Monitor refresh failures and consecutive-failure count. A failed refresh keeps the previous healthy searcher available, so availability can remain green while data freshness degrades.

## Backpressure and merges

Leave document and byte queue bounds enabled. Tune flush and merge concurrency against measured storage bandwidth and memory use. Increasing both at once can turn a throughput problem into I/O saturation and long tail latency.

Monitor segment count, pending merge bytes, flush latency, merge latency, commit latency, indexing queue pressure, search latency, and partial or cancelled searches.

## Backup and recovery

Take commit-aware backups, retain independent generations, validate them after transport, and practise restoring into a fresh directory. Define:

- recovery point objective and backup cadence;
- recovery time objective;
- who selects and switches the restored directory;
- how the restored commit is validated;
- how stale or partial upstream indexing work is replayed.

See [Backup and restore](../index-management/08-backup-and-restore.md) and [Validation and recovery](../index-management/03-validation-recovery.md).

## Telemetry

Configure metrics, tracing, and the slow-query log before an incident. Useful alerts include:

- refresh failures or growing refresh age;
- disk free-space threshold;
- file-descriptor usage;
- segment count and merge backlog;
- indexing queue saturation;
- commit or fsync latency;
- search timeout, cancellation, and partial-result rate;
- backup age and validation failure.

The [Aspire dashboard](../observability/05-aspire-dashboard.md) is useful for local and single-environment inspection. Export telemetry to durable monitoring for production retention and alerting.

## Native AOT

Publish and smoke-test the exact Native AOT target used in production. Register analysers, compression providers, generated mappings, and telemetry components through statically reachable paths. Include index open, one write and commit, refresh, and representative searches in the deployment smoke test.

## Deployment checklist

- Validate index compatibility before switching traffic.
- Keep the previous index directory until the new generation is serving correctly.
- Confirm free space and descriptor limits.
- Confirm writer ownership and commit durability.
- Confirm searcher refresh health.
- Confirm telemetry export and alerts.
- Confirm a recent validated backup.
