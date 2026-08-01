# Index lifecycle

Use this page when deciding when data becomes visible, durable or reclaimable.

`IndexWriter` accepts documents and flushes immutable segments. `Commit` records a durable generation referencing those segments. An `IndexSearcher` reads a committed view, while `SearcherManager` provides near-real-time refreshed views. Merges rewrite smaller segments in the background and obsolete files remain until no reader or snapshot retains them.

This model means a searcher is a read-only view, not a live window into every writer mutation. Dispose readers, leases and snapshots promptly so files can be reclaimed.

See also: [Concurrent indexing](../concurrency/02-concurrent-indexing.md), [Snapshots and deletion policies](../concurrency/03-snapshots-and-policies.md), and <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter>.
