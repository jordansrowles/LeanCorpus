# Vector search

LeanCorpus indexes application-generated embeddings and returns the nearest
stored vectors. New 4.0 vector indexes use Float32 storage and exact cosine
scans. Exact search gives the correct ranking for the persisted values, with
work proportional to the number of vectors and their dimensions.

Historical Int8 indexes remain readable and are searched exactly over their
decoded stored values. This does not restore the original Float32 ranking.
Historical BBQ indexes retain their existing scorer and query route, but an
index containing BBQ data is read-only. New Int8, BBQ and HNSW graph writes are
retired for 4.0.

See the [vector search guide](../advanced/05-vector-search.md), [filtered
vector search](../advanced/08-filtered-vector-search.md), and
<xref:Rowles.LeanCorpus.Search.Queries.VectorQuery>.
