# Vector search

LeanCorpus stores and searches application-generated embeddings. It does not
choose an embedding model or send document text to an external service.

## Supported mode in 4.0

New vector indexes store Float32 values in `.vec` v2 files and search them with
an exact cosine scan. The scan scores every live, present vector in each
matching segment. Its work grows with the number of vectors and dimensions, so
measure the cost on the corpus you expect to run. This is the supported mode
for new 4.0 indexes.

```csharp
var document = new LeanDocument();
document.Add(new StringField("id", "v1"));
document.Add(new VectorField("embedding", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }));
writer.AddDocument(document);

var query = new VectorQuery("embedding", queryVector, topK: 10);
var hits = searcher.Search(query, topN: 10);
```

All vectors in one field must have the same dimension. Vectors are normalised
at index time by default, so scores use cosine similarity over the stored
Float32 values. Set `NormaliseVectors` consistently with the embeddings and
query vectors used by the application.

An existing `.hnsw` v1 graph does not change Float32 query routing: the stored
Float32 values are scanned exactly and the graph is reported as ignored.
`Explain(VectorQuery, docId)` describes this as an exact Float32 scan.

## Retired graph creation

New HNSW graph writes are retired for 4.0. `BuildHnswOnFlush` now defaults to
`false`; setting it to `true` is rejected before the writer creates index
files. `HnswBuildConfig` and `HnswSeed` remain on `IndexWriterConfig` for source
compatibility, but they do not create graphs. Historical `.hnsw` v1 data
remains readable.

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

Float32 and historical Int8 queries apply the filter first, then scan only the
matching live documents with vectors. Historical BBQ keeps its existing
graph-based search behaviour. See [Filtered vector search](08-filtered-vector-search.md)
for the strategy details.

Vector queries can be combined with text queries through reciprocal rank
fusion:

```csharp
var rrf = new RrfQuery()
    .Add(new TermQuery("body", "machine learning"))
    .Add(new VectorQuery("embedding", queryVector, topK: 50));
```

`efSearch` and `oversamplingFactor` remain available for compatibility with
historical BBQ graph queries. They do not affect exact Float32 or decoded-Int8
scans.

See [Score explanations](../searching/11-score-explanations.md), [storage
formats](../contributors/storage-formats.md), and
[ADR042](../articles/ADRs/ADR042-bbq-v3-osq-canonical-int8.md).
