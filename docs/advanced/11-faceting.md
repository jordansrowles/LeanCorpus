# Faceting

Faceting counts values from selected fields across all documents matching a query while returning the normal top-N results.

Facet fields must have DocValues. Use a single-valued or multi-valued DocValues field according to the source data.
Facet buckets count matching documents, so a document contributes at most once to
each distinct bucket even when the same value is indexed repeatedly. A document
with several distinct values contributes once to every one of those buckets.

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

`results` is the usual `TopDocs`. Each `FacetResult` has a `Name`, `FieldName`, and ordered `FacetBucket` values. `Name` defaults to the field and can distinguish independent requests over the same source field. A bucket contains the stored DocValues value and its matching-document count.

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

Use the multi-valued DocValues field type when one document belongs to several categories. A document contributes once to each of its distinct values, not once per field occurrence. Missing values are excluded unless an advanced `FacetRequest` opts into the missing bucket.

## Counting model

Facet counts cover the complete matching set, not just the returned top-N page. The searcher exposes every live match to the facet collector during the same query traversal; it does not replay a query merely to populate facet buckets.

Flat facets require sorted or sorted-set DocValues. Stored fields and binary DocValues are not faceting fallbacks. Exact faceting has a searcher-level `MaxExactFacetBuckets` guard (100,000 by default): a request exceeding it fails rather than returning a truncated or approximate result. Paging retains only the requested count-ordered candidates unless the caller explicitly requests every bucket.

## Federated facets and global ordinals

When searching several directories, `MultiReader.SearchWithFacets()` merges
sorted and sorted-set DocValues through one immutable `OrdinalMap`. The map gives
equal terms the same global ordinal even when their local segment ordinals differ:

```csharp
using var reader = new MultiReader([firstDirectory, secondDirectory]);
var ordinals = reader.GetOrdinalMap("category", sortedSet: true);
int globalOrdinal = ordinals.GetGlobalOrdinal(sourceIndex: 1, localOrdinal: 0);
```

`IndexSearcher.GetOrdinalMap()` exposes the equivalent map across one searcher's
segments. The source index follows the captured segment or component order. Taxonomy,
join, and grouping APIs are not inferred from this map because LeanCorpus does not
currently expose those index structures.

## Practical guidance

- facet on controlled values such as category, status, language, or tenant;
- avoid raw identifiers and free text;
- normalise display variants before indexing;
- cap or post-process the buckets presented by the application;
- measure broad matching populations as well as selective term queries.

Faceting differs from [field collapsing](09-field-collapsing.md). Faceting counts groups while preserving the ordinary result list. Collapsing changes the result list so only a representative hit from each group is returned.

Numeric summaries such as minimum, maximum, sum, and average belong to [aggregations](01-aggregations.md).

## Date histograms

Dates are UTC Unix milliseconds in an `Int64Field`. `DateHistogramFacetRequest`
supports fixed elapsed intervals such as `DateHistogramInterval.Hour` and UTC
calendar day, ISO Monday-start week, month, quarter and year intervals. Buckets
are `[start, end)`, expose typed `DateTimeOffset` boundaries, and count a
multi-valued document once per logical bucket.

## Hierarchical paths

Use `FacetPath` for a dimension with levels such as `Technology / Programming / C#`.
`FacetPathIndexer.AddToDocument` writes every path prefix through existing
queryable `StringField` postings and sorted-set DocValues:

```csharp
FacetPathIndexer.AddToDocument(
    document,
    "category",
    new FacetPath("Technology", "Programming", "C#"));

var (_, facets) = searcher.SearchWithFacetRequests(
    new MatchAllDocsQuery(),
    topN: 20,
    [new HierarchicalFacetRequest(
        "category",
        parentPath: new FacetPath("Technology"))]);
```

The request returns immediate children of the root or parent path. Components
are length-prefixed internally, so `/` and `:` in a component remain data and
do not change the path identity. A path is indexed with postings as well as
DocValues, which allows the same representation to be used by drill-down.
Hierarchy depth is limited to 32 components to bound prefix expansion.

## Drill-down

`DrillDownQuery` wraps any base query. Selections in different dimensions are
ANDed, while multiple selections in one dimension are ORed:

```csharp
var query = new DrillDownQuery(
    new TermQuery("body", "phone"),
    new DrillDownSelection("category", "books"),
    new DrillDownSelection("category", "magazines"),
    new DrillDownSelection("language", "en"));

var results = searcher.Search(query, topN: 20);
```

The example means `body` contains `phone`, category is either `books` or
`magazines`, and language is `en`. Use a `FacetPath` selection for an exact
hierarchical path. Drill-down relies on searchable terms, not DocValues alone;
`StringField` and `FacetPathIndexer` provide both representations.
