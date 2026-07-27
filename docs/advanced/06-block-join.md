# Block join (nested documents)

Block join stores one or more child documents immediately before their parent in a segment. `BlockJoinQuery` returns parent documents whose children match.

This is a single-level relationship. It is not a general nested-document graph and does not support parent-to-grandparent joins.

## Index a block

```csharp
var firstComment = new LeanDocument();
firstComment.Add(new StringField("docType", "comment", stored: false));
firstComment.Add(new TextField("comment", "Great phone"));

var secondComment = new LeanDocument();
secondComment.Add(new StringField("docType", "comment", stored: false));
secondComment.Add(new TextField("comment", "Battery life is poor"));

var review = new LeanDocument();
review.Add(new StringField("docType", "review"));
review.Add(new TextField("title", "Acme X1"));

writer.AddDocumentBlock([firstComment, secondComment, review]);
```

The last document is marked as the parent. At least one child and one parent are required. The complete block is kept together through flush and merge.

An asynchronous overload preserves the same atomic block shape:

```csharp
await writer.AddDocumentBlockAsync(
    [firstComment, secondComment, review],
    cancellationToken);
```

When backpressure is enabled, one block cannot contain more documents than `MaxQueuedDocs`.

## Query parents by child matches

```csharp
var childQuery = new TermQuery("comment", "battery");
var parentQuery = new BlockJoinQuery(childQuery)
{
    Boost = 2.0f,
};

var hits = searcher.Search(parentQuery, topN: 10);
```

Execution finds matching child document IDs, skips any match that is itself a parent, and maps each child to the next parent marker in its block. A parent is returned once even when several children match.

## Parent filtering

`BlockJoinQuery` has only a child query. To apply a parent-level condition, combine the join with another clause:

```csharp
var query = new BooleanQueryBuilder()
    .Must(new BlockJoinQuery(
        new TermQuery("comment", "battery")))
    .Must(new TermQuery("docType", "review"))
    .Must(new TermQuery("status", "published"))
    .Build();
```

Use a field that is present on parents and absent from children. This also guards against accidentally treating a child as an ordinary top-level result in other queries.

## Scoring

Child scores are not aggregated. A matching parent receives the `BlockJoinQuery.Boost`, plus normal composition effects from an outer query. The number or strength of matching children does not increase the parent score.

If child relevance must affect ordering, compute an application field on the parent at indexing time or use a second-stage application rescore.

## Updates and deletion

Treat a block as one logical indexing unit. Replacing only a child can break the intended relationship between current source data and the immutable block. A common pattern is:

1. delete the old parent block through a stable identifier;
2. add the complete replacement block;
3. commit both changes together.

Test deletion and merge behaviour with multiple neighbouring blocks. The parent marker file is part of the segment and is remapped during merges.

## Limitations

- one parent level only;
- children must immediately precede their parent;
- no child-score aggregation modes;
- no query that directly returns children with their parent attached;
- block membership cannot be reconstructed from stored fields alone.

Use denormalised parent fields when the nested relationship is small and queried frequently. Block join is most useful when child text must remain independently indexed but results are parent entities.
