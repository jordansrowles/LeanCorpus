# Snapshots and deletion policies

A snapshot pins a specific commit so its segment files survive future deletion
policy passes. Use it to take consistent backups while indexing continues.

## Take a snapshot

```csharp
var snapshot = writer.CreateSnapshot();
try
{
    BackupSegmentFiles(snapshot.SegmentIds, snapshot.Generation);
}
finally
{
    writer.ReleaseSnapshot(snapshot);
}
```

The snapshot exposes the commit `Generation` and the list of segment IDs the
commit references.

## Deletion policies

`IndexWriterConfig.DeletionPolicy` controls which old commits and segments survive
after each commit:

| Policy | Behaviour |
|---|---|
| `KeepLatestCommitPolicy` (default) | Retains only the newest commit. |
| `KeepLastNCommitsPolicy(n)` | Retains the last `n` generations. |

```csharp
var config = new IndexWriterConfig
{
    DeletionPolicy = new KeepLastNCommitsPolicy(maxCommits: 5),
};
```

Active snapshots always protect their segments, regardless of policy.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.CreateSnapshot%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.KeepLastNCommitsPolicy>
- <xref:Rowles.LeanCorpus.Index.Indexer.KeepLatestCommitPolicy>
