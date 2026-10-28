# Index lifecycle

Use this page when deciding when data becomes visible, durable or reclaimable.

`IndexWriter` accepts documents and flushes immutable segments. `Commit` records a durable generation referencing those segments. An `IndexSearcher` reads a committed view, while `SearcherManager` provides near-real-time refreshed views. Merges rewrite smaller segments in the background and obsolete files remain until no reader or snapshot retains them.

This model means a searcher is a read-only view, not a live window into every writer mutation. Dispose readers, leases and snapshots promptly so files can be reclaimed.

For federated search, `MultiReader` captures one committed view per directory and
assigns document-ID ranges in the order supplied by the caller. It does not refresh
component readers independently, so create a new composition when a consistent set of
generations is required. `ReaderManager<TReader>` provides the same immutable swap and
lease lifecycle for other disposable near-real-time reader types.

See also: [Concurrent indexing](../concurrency/02-concurrent-indexing.md), [Snapshots and deletion policies](../concurrency/03-snapshots-and-policies.md), and <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter>.
