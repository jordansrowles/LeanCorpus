# Update and delete

## Delete by query

```csharp
writer.DeleteDocuments(new TermQuery("id", "abc-123"));
writer.Commit();
```

Deletes are buffered and applied at commit time.

## Update (delete-then-add)

```csharp
var doc = new LeanDocument();
doc.Add(new StringField("id", "abc-123"));
doc.Add(new TextField("body", "Updated content"));

writer.UpdateDocument(new TermQuery("id", "abc-123"), doc);
writer.Commit();
```

The delete and add land in the same commit. Readers never see a window where the document is missing.

## Update by query

```csharp
var replacement = new LeanDocument();
replacement.Add(new StringField("id", "abc-123"));
replacement.Add(new TextField("body", "Replacement content"));

writer.UpdateDocuments(new TermQuery("id", "abc-123"), replacement);
writer.Commit();
```

`UpdateDocuments` accepts any `Query`, not just `TermQuery`.

## Sequence numbers

Track document versions for change-data-capture and replication:

```csharp
var config = new IndexWriterConfig
{
    TrackSequenceNumbers = true,
};

// Each commit gets a monotonically increasing sequence number
long seq = writer.NextSequenceNumber;
writer.Commit();
```

Sequence numbers are persisted through commits and merges. Use them to identify which documents changed between two points in time, or to resume a replication stream from a known position.

## Soft deletes

Mark documents deleted without immediately removing them:

```csharp
var config = new IndexWriterConfig
{
    SoftDeletesEnabled = true,
    SoftDeleteRetentionSeconds = TimeSpan.FromHours(24).TotalSeconds,
};

writer.SoftDeleteDocuments(new TermQuery("id", "abc-123"));
writer.Commit();
```

Soft-deleted documents are excluded from results but retained until the retention period expires and a merge reclaims them.

## AddIndexes

Merge segments from another index without re-analysing:

```csharp
var sourceDir = new MMapDirectory("/path/to/other/index");
writer.AddIndexes(sourceDir);
writer.Commit();
```


Useful for restoring archived segments, merging partitioned indexes, or bootstrapping from a snapshot.

## Two-phase commit

`PrepareCommit()` stages all pending changes without making them visible to readers:

```csharp
writer.AddDocument(doc1);
writer.AddDocument(doc2);
writer.PrepareCommit();

// Changes are staged but not visible yet.
// Rollback() would discard them.

writer.Commit(); // Makes the staged changes visible
```

Use `PrepareCommit()` when you need atomic visibility: add several documents, stage the commit, then either publish or roll back as a unit. Readers never see a partial batch.

## ForceMerge

Reduce the number of segments in the index:

```csharp
writer.ForceMerge(maxSegments: 1);
```

`ForceMerge` consolidates segments down to `maxSegments`. A single-segment merge eliminates all deleted documents and produces the smallest possible index. Merges are I/O-intensive — call during maintenance windows, not on every commit.


## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.DeleteDocuments%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.UpdateDocument%2A>
