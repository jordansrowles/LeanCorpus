# ADR008: Stored fields v2 streams outside the CodecKit envelope

- **Date:** 2026-07-09
- **Status:** Accepted

## Context

`StoredFieldsWriter` and `StoredFieldsStreamWriter` buffered the entire `.fdt`
body in an `ArrayBufferWriter<byte>` before writing it. The reason was the
CodecKit file envelope: every codec file was expected to start with
`[version:byte][VarInt64 bodyLen][body]`. The body length can only be written
after the body is complete, so the writer had to accumulate the whole segment
before flushing. For large segments this exhausted process memory.

## Decision

Introduce a v2 stored-fields format that streams blocks directly to
`IndexOutput` and does not use the CodecKit `VersionEnvelope`. The v2 layouts
are:

- `.fdt`: `[version=2:byte][blockSize:int32][compression:byte][blocks...]`
- `.fdx`: `[version=2:byte][blockSize:int32][docCount:int32][blockCount:int32][offsets:int64*]`

Each `.fdt` block is written as soon as it is full, so at most one uncompressed
block sits in RAM. The `.fdx` index still buffers only block offsets, which are
written once after the last block is flushed.

`StoredFieldsReader` retains the ability to read v1 files (the old CodecKit
envelope) and dispatches on the leading version byte.

## Rationale

The CodecKit envelope is `[version][bodyLen][body]`. The `bodyLen` prefix is
fundamentally incompatible with streaming a large segment: the length must be
known before the body bytes are emitted. A length-free `Versioned` codec exists,
but it still encodes a complete in-memory value and does not help the write
path.

CodecKit is still used for migration and compatibility:

- `CodecFormats` registers `fdt` and `fdx` version steps in
  `CodecMigrationRegistry`.
- `IndexFormatInspector` special-cases `.fdt`/`.fdx` and reads the version byte
  through `StoredFieldsFileHeader.ReadVersion`, then reports the version against
  the registry.
- `IndexCompatibility.Check` and `IndexCodecMigrator.Plan` use that inventory to
  flag v1 files for migration.
- `IndexCodecMigrator.ExecuteRewrite` rewrites v1 stored fields by opening them
  with `StoredFieldsReader` and calling `StoredFieldsWriter`, which always emits
  v2.

This keeps the migration infrastructure intact while removing the buffering
constraint from the write hot path.

## Consequences

- `StoredFieldsFileHeader` is the single source of truth for the `.fdt`/`.fdx`
  header shape and version constants.
- `StoredFieldsWriter`, `StoredFieldsStreamWriter`, and `StoredFieldsReader`
  bypass `CodecFileHeader` for stored fields.
- `CodecFormats.StoredFields` was removed because nothing referenced it and it
  encoded the old v1 envelope.
- `StoredFieldsFormat` remains as the legacy v1 body codec for tests; its XML
  doc and `CodecConstants`/`CodecFormats` comments now describe it as legacy.
- Existing v1 indexes remain readable; `IndexCodecMigrator` rewrites them to v2.
- Documentation in `docs/articles/05-codecs.md`,
  `docs/articles/07-feature-comparison.md`, and
  `docs/tutorials/codeckit/` was updated to distinguish the streaming stored-
  fields format from the CodecKit envelope used by other codecs.
