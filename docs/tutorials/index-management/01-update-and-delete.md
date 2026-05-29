# Update and delete

## Delete by query

`IndexWriter.DeleteDocuments` accepts a `TermQuery`. Use it to remove documents
matching an identifier:

```csharp
writer.DeleteDocuments(new TermQuery("id", "abc-123"));
writer.Commit();
```

Deletes are buffered and applied at commit time. Until commit, the deletes are not
visible to new searchers.

## Update

`UpdateDocument` is delete-then-add by an identifier term:

```csharp
var doc = new LeanDocument();
doc.Add(new StringField("id", "abc-123"));
doc.Add(new TextField("body", "Updated content"));

writer.UpdateDocument(new TermQuery("id", "abc-123"), doc);
writer.Commit();
```

The delete and add land in the same commit, so readers never see a window where
the document is missing.

## Soft deletes

Enable soft-deletion to mark documents deleted without immediately removing them
from the index:

```csharp
var config = new IndexWriterConfig
{
    SoftDeletesEnabled = true,
    SoftDeletesRetentionPeriod = TimeSpan.FromHours(24),
};
```

Soft-deleted documents are excluded from search results but retained in segments
until the retention period expires and a merge reclaims them. Delete by soft-delete
query:

```csharp
writer.SoftDeleteDocuments(new TermQuery("id", "abc-123"));
writer.Commit();
```

## Update by query

`UpdateDocuments` accepts an arbitrary `Query` and a replacement `LeanDocument`.
It atomically deletes all matching documents and adds the replacement:

```csharp
var replacement = new LeanDocument();
replacement.Add(new StringField("id", "abc-123"));
replacement.Add(new TextField("body", "Replacement content"));

writer.UpdateDocuments(new TermQuery("id", "abc-123"), replacement);
writer.Commit();
```

Unlike `UpdateDocument`, the query does not need to be a `TermQuery`.

## AddIndexes

`AddIndexes` merges segments from another index directory into the current index
without re-analysing or re-indexing individual documents:

```csharp
var sourceDir = new MMapDirectory("/path/to/other/index");
writer.AddIndexes(sourceDir);
writer.Commit();
```

This is useful for restoring archived segments, merging partitioned indexes,
or bootstrapping a new index from a snapshot.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.DeleteDocuments%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.UpdateDocument%2A>
