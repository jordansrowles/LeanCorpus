# ADR008: Streaming codec formats bypass the CodecKit envelope

- **Date:** 2026-07-09
- **Status:** Deprecated
- **Superseeded by**: ADR009

## Context

Several codec writers had to buffer the entire file body before writing because
the CodecKit envelope requires `[version:byte][VarInt64 bodyLen][body]`. The
body length can only be written after the body is complete, forcing
materialisation of the whole segment before the first byte hits disk.

- Stored fields (`.fdt`): `StoredFieldsWriter` and `StoredFieldsStreamWriter`
  buffered the entire body in an `ArrayBufferWriter<byte>`. For large segments
  this exhausted process memory (07d86970).
- Postings (`.pos`): `StreamingPostingsMerger` read the entire file with
  `File.ReadAllBytes` and materialised every term's positions into temporary
  `List<int>` allocations during merge (285c54f72).

## Decision

Introduce v2 formats for stored fields and postings that stream directly to
`IndexOutput` without the CodecKit `VersionEnvelope`. The v2 header is a single
version byte; the body follows immediately. Each format has its own
`*FileHeader` helper for reading the version byte and skipping any v1-specific
length prefix.

### Stored fields v2 (07d86970)

- `.fdt`: `[version=2:byte][blockSize:int32][compression:byte][blocks...]`
- `.fdx`: `[version=2:byte][blockSize:int32][docCount:int32][blockCount:int32][offsets:int64*]`

Each `.fdt` block is written as soon as it is full, keeping at most one
uncompressed block in RAM. The `.fdx` index buffers only block offsets.

### Postings v2 (285c54f72)

- `.pos`: `[version=2:byte][body]`

The postings body continues to use the existing skip-interval and delta-encoded
layout; only the envelope is removed. The reader skips the v1 VarInt64 length
prefix when the version byte is 1.

### Migration

CodecKit is still used for migration and compatibility:

- `CodecFormats` registers version steps for `fdt`, `fdx`, and `pos` in
  `CodecMigrationRegistry`.
- `IndexFormatInspector` special-cases `.fdt`/`.fdx`/`.pos` and reads the
  version byte through the format's `*FileHeader.ReadVersion`, then reports the
  version against the registry.
- `IndexCompatibility.Check` and `IndexCodecMigrator.Plan` use that inventory to
  flag v1 files for migration.
- `IndexCodecMigrator.ExecuteRewrite` rewrites v1 files by opening them with the
  v1-aware reader and calling the writer, which always emits v2.

## Rationale

The CodecKit envelope is `[version][bodyLen][body]`. The `bodyLen` prefix is
fundamentally incompatible with streaming a large segment: the length must be
known before the body bytes are emitted. A length-free `Versioned` codec exists
in CodecKit but it still encodes a complete in-memory value and does not help
the write path.

## Consequences

- `PostingsFileHeader` and `StoredFieldsFileHeader` are the single sources of
  truth for their respective header shapes and version constants.
- `StreamingPostingsMerger`, `StoredFieldsWriter`, `StoredFieldsStreamWriter`,
  and `StoredFieldsReader` bypass `CodecFileHeader`.
- `CodecFormats.StoredFields` was removed; `CodecFormats.Postings` is a legacy
  v1-only codec for tests and backward compatibility.
- `StoredFieldsFormat` remains as the legacy v1 body codec for tests.
- Existing v1 indexes remain readable; `IndexCodecMigrator` rewrites them to v2.
- Documentation in `docs/articles/05-codecs.md`,
  `docs/articles/07-feature-comparison.md`, and
  `docs/tutorials/codeckit/` was updated to distinguish streaming formats from
  the CodecKit envelope used by other codecs.
