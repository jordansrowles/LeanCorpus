# Block-join (nested documents)

Block-join lets a single parent document own a block of child documents, then query
parents by what their children match.

## Index a block

Children must be added immediately before their parent within the same call:

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
using Rowles.LeanCorpus.Search.Queries;

var childQ  = new TermQuery("comment", "battery");
var parentQ = new BlockJoinQuery(childQ);

var hits = searcher.Search(parentQ, topN: 10);
```

`BlockJoinQuery.Field` returns the child query's field.

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.BlockJoinQuery>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentBlock%2A>
