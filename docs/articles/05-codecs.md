# Codecs

LeanCorpus stores each segment as a small set of purpose-built codec files. The
files are sidecars that share the segment ID prefix, for example `seg_0.dic` and
`seg_0.pos`. Every binary codec starts with the LeanCorpus magic header and a
format version from `CodecConstants`.

| Extension | Codec | Used for |
|---|---|---|
| `.seg` | Segment metadata | JSON metadata for document counts, field names, index sort metadata, delete generation, and vector field descriptors. |
| `segments_N` | Commit file | Atomic commit manifest listing live segment IDs, commit generation, content token, and CRC32 trailer. |
| `.dic` | Term dictionary | Maps sorted `field\0term` keys to postings offsets for term, phrase, prefix, wildcard, fuzzy, and regexp queries. |
| `.pos` | Postings | Block-packed document IDs, frequencies, positions, and optional payloads for inverted-index queries. |
| `.nrm` | Norms | Per-document field-length norms used by scoring. |
| `.fln` | Field lengths | Exact per-field token counts used by BM25 and segment statistics. |
| `.fdt` | Stored fields data | Stored field payload blocks, optionally compressed. |
| `.fdx` | Stored fields index | Stored field block offsets and compression metadata for random document lookup. |
| `.num` | Sparse numeric index | Per-field numeric values keyed by document ID for range queries and compatibility fallback. |
| `.bkd` | BKD tree | Point index for fast numeric range queries. |
| `.dvn` | Numeric DocValues | Single-valued numeric column data for sorting and aggregations. |
| `.dvs` | Sorted DocValues | Single-valued string ordinal columns for sorting, faceting, and collapse. |
| `.dss` | Sorted-set DocValues | Multi-valued string ordinal columns for repeated `StringField` values, used by facets and deterministic sort/collapse fallback. |
| `.dsn` | Sorted-numeric DocValues | Multi-valued numeric columns for repeated `NumericField` values, used by aggregations and deterministic numeric sort fallback. |
| `.dvb` | Binary DocValues | Multi-valued UTF-8 byte columns derived from stored-field payloads, used before stored-field scans for string facets and grouping fallback. |
| `.vec` | Vectors | Per-field dense vector payloads used by vector search. |
| `.hnsw` | HNSW graph | Approximate nearest-neighbour graph for vector search. |
| `.tvd` | Term vectors data | Optional per-document term vector payloads. |
| `.tvx` | Term vectors index | Term vector document offsets. |
| `.pbs` | Parent bitset | Parent-document markers for block-join queries. |
| `.del` / `_gen_N.del` | Live docs | Deleted-document bitsets, either legacy or generation-specific. |
| `.stats.json` | Segment stats | Per-segment field-length totals and document counts. |
| `stats_N.json` | Index stats | Commit-level corpus statistics used by searchers. |

## Optional and generated files

`write.lock` prevents multiple writers from mutating an index at the same time.
Temporary `*.tmp` files can appear during atomic writes and are cleaned up during
writer-side recovery.

Stored field compression is configured through `FieldCompressionPolicy`. The
codec records the chosen policy in `.fdx`, while compression implementations can
publish native sidecar binaries when an application is published as Native AOT.

`IndexValidator.Check` and `leancorpus-cli.exe check` validate the codec headers
for the segment files above, including DocValues, vector, HNSW, term-vector, and
live-doc sidecars when they are present. Deep validation opens the reader paths
for postings, stored fields, DocValues, vectors, HNSW graphs, and live docs.

See [Reliable commits](04-reliable-commits.md) for commit and recovery details.
