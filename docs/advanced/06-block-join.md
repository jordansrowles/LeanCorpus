# Block-join (nested documents)

A parent document owns a block of child documents. Query parents by what their children match.

## Index a block

Children must be added immediately before their parent:

```csharp
var c1 = new LeanDocument();
c1.Add(new TextField("comment", "Great phone"));

var c2 = new LeanDocument();
c2.Add(new TextField("comment", "Battery life is poor"));

var parent = new LeanDocument();
parent.Add(new StringField("type", "review"));
parent.Add(new TextField("title", "Acme X1"));

writer.AddDocumentBlock(new[] { c1, c2, parent });
```

The last document in the block is the parent.

## Query parents by child matches

```csharp
var childQ  = new TermQuery("comment", "battery");
var parentQ = new BlockJoinQuery(childQ);

var hits = searcher.Search(parentQ, topN: 10);
```

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.BlockJoinQuery>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentBlock%2A>
