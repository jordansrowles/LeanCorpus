# Vector search

LeanCorpus stores dense float vectors per segment and builds an HNSW graph at
flush time by default. Searches use HNSW when a graph is present, then rerank the
shortlist with exact cosine similarity.

## Index a vector

```csharp
using Rowles.LeanCorpus.Document.Fields;

var doc = new LeanDocument();
doc.Add(new StringField("id", "v1"));
doc.Add(new VectorField("embedding", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }));
writer.AddDocument(doc);
```

All vectors written to the same field must have the same dimensionality.

Vector fields are normalised at index time by default. This keeps cosine search
cheap and consistent.

## Query

```csharp
using Rowles.LeanCorpus.Search.Queries;

var query = new VectorQuery(
    "embedding",
    queryVector,
    topK: 10,
    efSearch: 128,
    oversamplingFactor: 2);

var hits = searcher.Search(query, topN: 10);
```

The score is cosine similarity, range `[-1, 1]` (typically `[0, 1]` for normalised
vectors).

## Build settings

```csharp
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Index.Indexer;

var config = new IndexWriterConfig
{
    NormaliseVectors = true,
    BuildHnswOnFlush = true,
    HnswBuildConfig = new HnswBuildConfig
    {
        M = 16,
        EfConstruction = 100,
    },
};
```

Set `HnswSeed` for reproducible graph builds.

## Hybrid retrieval

Combine a vector query with a text query through [RRF](04-rrf.md), or add a
filter directly to `VectorQuery`:

```csharp
var filter = new TermQuery("category", "docs");
var query = new VectorQuery("embedding", queryVector, topK: 10, filter: filter);
```

## Implementation note

If no HNSW graph exists, LeanCorpus falls back to a flat SIMD scan. Vector
readers are opened lazily, so non-vector searches do not pay the mmap cost.

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.VectorQuery>
- [Filtered vector search](08-filtered-vector-search.md)
