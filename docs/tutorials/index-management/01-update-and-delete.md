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

## Soft deletes

Mark documents deleted without immediately removing them:

```csharp
var config = new IndexWriterConfig
{
    SoftDeletesEnabled = true,
    SoftDeletesRetentionPeriod = TimeSpan.FromHours(24),
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

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.DeleteDocuments%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.UpdateDocument%2A>
