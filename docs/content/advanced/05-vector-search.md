# Vector search

LeanCorpus stores and searches application-generated embeddings. It does not
choose an embedding model or send document text to an external service.

## Supported mode in 4.0

New vector indexes store Float32 values in `.vec` v2 files and, by default,
search them with an exact cosine scan. The scan scores every live, present
vector in each matching segment. Its work grows with the number of vectors and
dimensions, so measure the cost on the corpus you expect to run. An optional
[vector graph](#optional-vector-graph) can select candidates instead.

```csharp
var document = new LeanDocument();
document.Add(new StringField("id", "v1"));
document.Add(new VectorField("embedding", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }));
writer.AddDocument(document);

var query = new VectorQuery("embedding", queryVector, topK: 10);
var hits = searcher.Search(query, topN: 10);
```

## TopK and result counts

`topK` selects the query's nearest neighbours across the whole searcher
snapshot, and across every component of a `MultiReader`. It is independent of
the search's `topN`: a search returns at most `min(topN, topK)` hits, and
`TotalHits` and `Count` report the size of the selected set rather than every
vector scanned. Ties are broken by the lower document ID. `topK` must be
positive.

```csharp
var nearest = new VectorQuery("embedding", queryVector, topK: 5);
TopDocs page = searcher.Search(nearest, topN: 100); // at most 5 hits
int selected = searcher.Count(nearest);             // at most 5
```

Facets, sorting, `SearchAfter`, streaming and asynchronous search all observe
the same selected set. `Explain(VectorQuery, docId)` reports a zero-score
non-match, naming the cap, for a document outside it. Each `Explain` call
selects the whole set to decide membership, so it costs about as much as
running the query.

All vectors in one field must have the same dimension. Vectors are normalised
at index time by default, so scores use cosine similarity over the stored
Float32 values. Set `NormaliseVectors` consistently with the embeddings and
query vectors used by the application.

An existing `.hnsw` v1 graph does not change Float32 query routing: the stored
Float32 values are scanned exactly and the graph is reported as ignored.
`Explain(VectorQuery, docId)` describes this as an exact Float32 scan.

## Optional vector graph

Set `BuildHnswOnFlush = true` to build a Float32 vector graph (`.vgr`) for each
unquantised vector field. Flush builds it and every merge rebuilds it from the
merged vectors. The option defaults to `false`.

```csharp
var config = new IndexWriterConfig
{
    BuildHnswOnFlush = true,
    HnswBuildConfig = new HnswBuildConfig { M = 16, EfConstruction = 100 },
    HnswSeed = 42, // optional; a fixed default seed is used otherwise
};
```

A query traverses each segment's graph to choose candidates, then rescores them
with the exact cosine scorer over the stored Float32 values. Scores are the
same as exact search; only the set of candidates can differ, as with any
approximate search. `efSearch` sets the traversal breadth: higher values
improve recall and cost more. The same seed, vectors and parameters always
produce the same graph.

The search falls back to an exact scan of the segment, and reports why, when:

- the filter leaves few documents (fewer than `max(64, 0.5% of MaxDoc)`, or no
  more than the shortlist, `topK * oversamplingFactor`), because scanning them
  is cheaper;
- the traversal exhausts its work budget, about `max(4 * efSearch, eligible
  documents / 2)` distance evaluations, so it never costs more than the scan it
  replaces;
- the traversal returns fewer candidates than requested;
- the graph file cannot be opened or fails validation;
- the score is not monotonic in cosine: per-document field boosts, or a zero
  or negative query boost.

A zero query vector has no direction, so it returns no hits on any field and
records no route. A filter that matches nothing records `ExactSelectiveFilter`.

Graph distances use the same cosine as the exact scorer for every finite
vector, including very large, very small and subnormal magnitudes on fields
with `NormaliseVectors = false`. Every published graph reaches all of its
vectors; if construction cannot guarantee that, the segment is written without
a graph and searched exactly.

`IMetricsCollector.RecordVectorSearchRoute` receives one `VectorSearchRoute`
per segment, and `Explain(VectorQuery, docId)` names the route. A fallback is
always reported as an exact scan.

Costs and limits:

- Building takes time at flush and on every merge; at level zero the graph
  stores about `(1 + M0) * 4` bytes per vector (`M0` defaults to `2 * M`).
- The graph helps most on embeddings with real neighbourhood structure.
  Uniform random high-dimensional data has none, so any graph visits most of
  the index there and gains little over the exact scan.
- An index with `.vgr` files needs LeanCorpus 4.0 or later. No migration is
  needed: an index without graphs is unchanged, and graphs are added as
  segments are flushed or merged with the option enabled.
- A corrupt graph is never used. Index checks report it as LLIDX054, and
  queries scan that segment exactly until a merge rebuilds it.

The option is implemented and tested, but its release qualification against
real embedding corpora and a reference implementation is still open; see
[ADR052](../articles/ADRs/ADR052-float32-vector-graph.md).

## Historical Int8 and BBQ indexes

New Int8 writes are also retired. Setting
`VectorQuantisation = VectorQuantisation.Int8` is rejected before index files
are created. Existing Int8 `.vq` v1/v2 and v3/tag-1 vectors remain readable.
Their queries scan the decoded stored values exactly, including when an old
HNSW graph is present. This is exact for the stored Int8 representation; it
does not recover the original Float32 embeddings or their ranking.

Structural migration of historical Int8 v1/v2 data to v3/tag 1 copies the
stored codes and calibration parameters without requantising them. The physical
format remains `.vq` v3. A segment merge reads historical Int8 vectors as their
decoded Float32 values and writes `.vec` v2, so it does not emit new Int8 data
or change the exact decoded-vector scores. The merged segment has no HNSW graph.

Historical BBQ representations remain readable with their existing scorer and
query routing. Any committed BBQ field makes its entire index read-only to
writers, including mixed indexes. New BBQ writes and BBQ migrations remain
unsupported. Build a separate Float32 index from the original documents and
embeddings when new vector indexing is required; decoded BBQ values cannot
recover the original components.

## Filtering and hybrid queries

`VectorQuery.Filter` restricts candidates *before* the top `topK` are chosen.
A sibling Boolean clause restricts the selected set *afterwards* and does not
refill it, so it can return fewer than `topK` hits:

```csharp
// The 10 nearest published documents.
var prefiltered = new VectorQuery("embedding", queryVector, topK: 10,
    filter: new TermQuery("status", "published"));

// Of the 10 nearest documents overall, those that are published.
var postfiltered = new BooleanQuery.Builder()
    .Add(new VectorQuery("embedding", queryVector, topK: 10), Occur.Must)
    .Add(new TermQuery("status", "published"), Occur.Must)
    .Build();
```

Float32 and historical Int8 queries apply the filter first, then scan only the
matching live documents with vectors. With a vector graph, a broad filter
restricts the graph's results while traversal still routes through
non-matching nodes, and a selective one scans exactly. Historical BBQ keeps its
existing graph-based search behaviour. See [Filtered vector search](08-filtered-vector-search.md)
for the strategy details.

Vector queries can be combined with text queries through reciprocal rank
fusion:

```csharp
var rrf = new RrfQuery()
    .Add(new TermQuery("body", "machine learning"))
    .Add(new VectorQuery("embedding", queryVector, topK: 50));
```

`efSearch` and `oversamplingFactor` govern candidate discovery for vector
graphs and historical BBQ graph queries, not the final `topK` cap. They do not
affect exact Float32 or decoded-Int8 scans.

See [ADR049](../articles/ADRs/ADR049-query-composition-and-vector-result-bounds.md)
for the composition and result-bound rules.

See [Score explanations](../searching/11-score-explanations.md), [storage
formats](../contributors/storage-formats.md), and
[ADR042](../articles/ADRs/ADR042-bbq-v3-osq-canonical-int8.md) and
[ADR052](../articles/ADRs/ADR052-float32-vector-graph.md).
