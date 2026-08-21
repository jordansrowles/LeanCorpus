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

## Supported historical framing

Supported 1.x and 2.x bodies may use:

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
