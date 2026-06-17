# Codecs

Each segment is stored as a small set of codec files sharing the segment ID prefix (e.g. `seg_0.dic`, `seg_0.pos`). Every binary codec starts with the LeanCorpus magic header and a format version from `CodecConstants`.

## File inventory

| Extension | Codec | Purpose |
|---|---|---|
| `.seg` | Segment metadata | JSON: doc counts, field names, index sort, delete generation, vector descriptors |
| `segments_N` | Commit file | Atomic manifest: live segment IDs, generation, content token, CRC32 trailer |
| `.dic` | Term dictionary | Sorted `field\0term` → postings offset for term, phrase, prefix, wildcard, fuzzy, and regexp queries |
| `.pos` | Postings | Block-packed doc IDs, frequencies, positions, optional payloads |
| `.nrm` | Norms | Per-document field-length norms for scoring |
| `.fln` | Field lengths | Per-field token counts for BM25 and segment stats |
| `.fdt` | Stored fields data | Stored field payload blocks, optionally compressed |
| `.fdx` | Stored fields index | Block offsets and compression metadata for random lookup |
| `.num` | Sparse numeric index | Per-field numeric values keyed by doc ID |
| `.bkd` | BKD tree | Point index for fast numeric range queries |
| `.dvn` | Numeric DocValues | Single-valued numeric columns for sorting and aggregations |
| `.dvs` | Sorted DocValues | Single-valued string ordinal columns for sorting, faceting, collapse |
| `.dss` | Sorted-set DocValues | Multi-valued string ordinals for repeated `StringField` values |
| `.dsn` | Sorted-numeric DocValues | Multi-valued numeric columns for repeated `NumericField` values |
| `.dvb` | Binary DocValues | Multi-valued UTF-8 byte columns from stored-field payloads |
| `.vec` | Vectors | Per-field dense float vectors for vector search |
| `.hnsw` | HNSW graph | Approximate nearest-neighbour graph |
| `.tvd` | Term vectors data | Per-document term vector payloads |
| `.tvx` | Term vectors index | Term vector document offsets |
| `.pbs` | Parent bitset | Parent-document markers for block-join |
| `.del` / `_gen_N.del` | Live docs | Deleted-document bitsets |
| `.stats.json` | Segment stats | Per-segment field-length totals and doc counts |
| `stats_N.json` | Index stats | Commit-level corpus statistics |

## Housekeeping

`write.lock` prevents multiple writers. Temporary `*.tmp` files appear during atomic writes and are cleaned up during writer recovery.

Stored field compression is configured through `FieldCompressionPolicy` and recorded in `.fdx`.

`IndexValidator.Check` and `leancorpus-cli.exe check` validate codec headers for all the above. Deep validation opens the reader paths for postings, stored fields, DocValues, vectors, HNSW graphs, and live docs.

## See also

- [Reliable commits](04-reliable-commits.md)
- [Validation and recovery](../tutorials/index-management/03-validation-recovery.md)
