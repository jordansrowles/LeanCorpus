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

## Tombstones

Deletes are written as a per-segment `.del` bitset. They are merged out when the
segment is rewritten by a background merge.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.DeleteDocuments%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.UpdateDocument%2A>
