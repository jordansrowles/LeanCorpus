# Vector search

Use this page when your application already produces embeddings and needs local approximate nearest-neighbour search.

LeanCorpus stores and searches vectors. It does not create embeddings, choose a model or send document text to an external service. Generate compatible vectors in your application, keep one dimension per field, then index them as `VectorField` values.

HNSW retrieves an approximate candidate set. Float32 fields are rescored from their stored vectors; quantised fields use approximate scores from their stored representation. Apply an ordinary LeanCorpus filter when results must remain within a tenant, category or permission boundary. Measure recall and latency on your own corpus before enabling quantisation or changing HNSW budgets.

See also: [Vector search guide](../advanced/05-vector-search.md), [Filtered vector search](../advanced/08-filtered-vector-search.md), and <xref:Rowles.LeanCorpus.Search.Queries.VectorQuery>.
