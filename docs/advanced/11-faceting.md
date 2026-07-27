# Faceting

Faceting counts values from selected fields across all documents matching a query while returning the normal top-N results.

Facet fields must have DocValues. Use a single-valued or multi-valued DocValues field according to the source data.

## Search and count

```csharp
var (results, facets) = searcher.SearchWithFacets(
    new TermQuery("body", "search"),
    topN: 20,
    "category",
    "author");

foreach (var facet in facets)
{
    Console.WriteLine(facet.FieldName);
    foreach (var bucket in facet.Buckets)
        Console.WriteLine($"  {bucket.Value}: {bucket.Count}");
}
```

`results` is the usual `TopDocs`. Each `FacetResult` has a `FieldName` and ordered `FacetBucket` values. A bucket contains the stored DocValues value and its matching-document count.

## Indexing facet values

```csharp
var document = new LeanDocument();
document.Add(new TextField("body", "A compact search engine"));
document.Add(new StringField(
    "category",
    "software",
    stored: false,
    boost: 1.0f,
    docValues: StringDocValues.SortedSet));
```

Use the multi-valued DocValues field type when one document belongs to several categories. A document contributes once to each of its distinct values.

## Counting model

Facet counts cover the complete matching set, not just the returned top-N page. The searcher uses a side collector where the query path supports it and a complete matching pass otherwise.

This makes faceting proportional to the number of matches and facet values. A broad query over high-cardinality fields can be expensive even when `topN` is small.

## Practical guidance

- facet on controlled values such as category, status, language, or tenant;
- avoid raw identifiers and free text;
- normalise display variants before indexing;
- cap or post-process the buckets presented by the application;
- measure broad fallback paths as well as selective term queries.

Faceting differs from [field collapsing](09-field-collapsing.md). Faceting counts groups while preserving the ordinary result list. Collapsing changes the result list so only a representative hit from each group is returned.

Numeric summaries such as minimum, maximum, sum, and average belong to [aggregations](01-aggregations.md).
