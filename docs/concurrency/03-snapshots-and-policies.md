# Snapshots and deletion policies

A snapshot pins a commit so its segment files survive deletion policy passes. Use for consistent backups while indexing continues.

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

The snapshot exposes the commit `Generation` and the list of segment IDs.

## Deletion policies

`IndexWriterConfig.DeletionPolicy` controls which old commits survive:

| Policy | Behaviour |
|---|---|
| `KeepLatestCommitPolicy` (default) | Only the newest commit |
| `KeepLastNCommitsPolicy(n)` | Last `n` generations |

```csharp
var config = new IndexWriterConfig
{
    DeletionPolicy = new KeepLastNCommitsPolicy(maxCommits: 5),
};
```

Active snapshots always protect their segments regardless of policy.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.CreateSnapshot%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.KeepLastNCommitsPolicy>
