# Storage formats

A LeanCorpus commit is a manifest plus immutable segment files. `CodecCatalog` is the authoritative inventory of persistent file roles, current body-format versions, access patterns, framing, checksums and migration policy.

## Segment inventory

The exact files depend on enabled fields and features.

| Family | Files | Access |
|---|---|---|
| Term dictionary | `.dic` | materialised FST metadata |
| Postings | `.pos` | streaming write, lazy/random read offsets |
| Norms and field lengths | `.nrm`, `.fln` | materialised |
| Stored fields | `.fdt`, `.fdx` | streaming data and random-access index |
| Term vectors | `.tvd`, `.tvx` | streaming data and random-access index |
| DocValues | `.dvn`, `.dvs`, `.dss`, `.dsn`, `.dvb`, `.dvnl`, `.dsnl` | sequential columns |
| Numeric structures | `.bkd`, `.bkdl`, `.num`, `.numl` | direct traversal and sparse sidecars |
| Vectors | `.vec`, `.vq` | retained random access |
| HNSW graph | `.hnsw` | retained random-access adjacency |
| Deletions and joins | `.del`, `.pbs` | bitmap payloads |
| Segment infrastructure | `.seg`, `.stats.json`, `.cfs` | JSON metadata or container |

`segments_N` identifies the segments in a commit and `stats_N.json` stores commit statistics. JSON and compound files are catalogue entries but are not mechanically wrapped in the binary frame.

## Canonical binary Frame v1

Current versioned binary files have a positive identity and a body checksum. All numeric fields are little-endian.

| Field | Size | Rule |
|---|---:|---|
| Magic | 4 | bytes `LCCF` |
| Frame version | 1 | `1` |
| Format ID length | 1 | 1 to 64 bytes |
| Body-format version | 4 | positive integer |
| Flags | 4 | zero in Frame v1 |
| Checksum algorithm | 1 | built-ins use xxHash64 |
| Reserved | 1 | zero |
| Format ID | variable | namespaced lowercase ASCII |
| Body | variable | format-specific bytes |
| Body length | 8 | exact physical body length |
| Checksum | 8 | checksum of the body only |

Frame version describes this outer structure. Body-format version describes the codec body. They change independently.

Normal opens validate framing and bounds but do not scan a large body checksum. `IndexValidator` deep validation performs that scan. Materialising reads are constrained separately from the maximum permitted file size.

## Vector data body versions

| Format ID | Extension | Readable bodies | Writable body | Older-body migration |
| --- | --- | --- | --- | --- |
| `leancorpus.vectors.float32` | `.vec` | v1, v2 | v2 | Rewrite |
| `leancorpus.vectors.quantised` | `.vq` | v1, v2, v3 | v3 Int8 | Int8 v1/v2 structural wrap into v3; BBQ unsupported |
| `leancorpus.vectors.hnsw` | `.hnsw` | v1 | v1 | Unchanged body |

The `.vq` migration rule is representation-specific. Int8 bodies may be
structurally wrapped into v3 without requantising their values. BBQ-tagged
bodies remain readable but are rejected for migration; a current BBQ-containing
index is read-only to the writer.

The `.vec` v2 and `.vq` v2/v3 bodies start with `docCount:int32`,
`dimension:int32`, and a format/quantisation byte. Bodies with persisted vector
presence then contain exactly `checked((docCount + 7) / 8)` presence bytes.
Each document has one bit in increasing document-ID order, least-significant
bit first. Unused high bits in the final byte must be zero. There is no
independent bitmap-length field or presence sidecar.

For `.vec`, the existing dense Float32 payload follows the bitmap; historical
Int8 data-format bodies retain their min/alpha parameters before dense bytes.
For `.vq` v1/v2, payload layout and legacy quantisation semantics remain
unchanged. `.vq` v3 adds a `storageKind` tag after presence so the reader can
distinguish Int8, historical sign-BBQ, and experimental OSQ with canonical
Int8. Current writers emit the Int8 representation only. The historical OSQ
body stores its format tag, segment centroid and squared norm, per-document
OSQ correction fields and packed one-bit codes, then per-document canonical
Int8 metadata and codes. Its readers remain for historical indexes; no current
writer, migration or merge emits that representation. Fixed-stride offsets
and body lengths are checked before access. The `.hnsw` scorer generation is
resolved from the associated `.vq` storage kind; `.hnsw` remains v1.

A set bit means the document explicitly supplied a vector, including an all-zero
vector. A clear bit means padding, which search and reranking skip before
accessing vector values. Supported Float32 and Int8 merges remap presence
alongside live documents and index sort order, then build HNSW only from present
destination IDs. An index containing any BBQ representation is rejected before
merge mutation.

## Supported historical framing

Supported 2.x bodies may use:

```text
[version: byte][zigzag VarInt64 body length][body]
```

or the ADR009 trailer:

```text
[version: byte][body][body length: int64]
```

Postings and stored fields also had declared custom streaming headers. Live-doc files had a headerless outer representation containing a framed Roaring bitmap. Legacy detection is permitted only when the catalogue descriptor declares that framing for the detected body version.

Current writers never emit these legacy outer frames.

## Loose and compound files

`ISegmentFileSource` exposes a logical file name and bounded `IndexInput` for both loose files and members stored in `.cfs`. A canonical footer is checked against the logical member boundary, not the end of the container. Inspector, compatibility and validator output therefore report both the logical member and its physical location.

## Random-access formats

Postings, vector, HNSW and BKD readers retain an input and perform direct offset arithmetic. Their offsets must stay inside the declared body range. Adding Frame v1 must not materialise these files or scan checksums during ordinary search opens. HNSW loads only the graph index needed to locate each node's neighbours; adjacency remains in the bounded body input. Deep validation scans its checksum explicitly.

Stored-field and term-vector pairs coordinate offsets across their data and index bodies. Their current and historical pair versions are validated together.

## External and independently framed files

`.seg`, `segments_N`, statistics JSON and `.cfs` retain their own serialisation or container rules. Minor binary sidecars such as `.num`, `.numl` and `.pbs` are explicit catalogue entries and use canonical Frame v1. This means recovery and tooling know their ownership and temporary-file patterns while applying the same framing and integrity policy as other codec files.

## Compatibility and migration

`IndexFormatInspector` inventories logical files. `IndexCompatibility` applies open policy. `IndexValidator` layers container, frame, body and cross-file checks. `IndexCodecMigrator` stages descriptor-led reframe, rewrite and coordinated-rewrite actions through normal current writers.

A 3.0-written canonical index cannot be opened by 2.x. Retain a verified backup before migration when application rollback is possible.

## Store boundary

`MMapDirectory`, `IndexInput` and `IndexOutput` own filesystem and mapped-file lifetimes. `IndexAtomicFileWriter`, `DirectoryFsync`, `DirtyFileTracker` and the platform filesystem implementations centralise publication, durability and precise retry behaviour. Durable commits synchronise only process-written files referenced by the commit; they do not enumerate or materialise codec files. Keep raw filesystem access behind this boundary.

## See also

- [CodecKit](codeckit/index.md)
- [Adding persistent formats](codeckit/02-adding-formats.md)
- [Codec migrations](codeckit/03-migrations.md)
- [Validation and recovery](../index-management/03-validation-recovery.md)

## Exact field lengths

`.fln` body v2 stores an Int32 field count, then each field's Int32 UTF-8 name
length, name bytes, Int32 document count and non-negative VarInt token counts.
The shared decoder checks names, uniqueness, encoded lengths, non-negative
values, exact body consumption and the canonical checksum. It validates encoded
values before allocating decoded arrays. Historical pooled-buffer writers could
record a longer array with a zero-only tail; the decoder checks every extra
VarInt is zero and returns only the owning segment's document range. Shorter
counts, non-zero tails and trailing bytes are rejected. The older v1 body uses
the same VarInt records and is rewritten to v2 by the catalogue migration path.
