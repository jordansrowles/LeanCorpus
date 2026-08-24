---
adr: ADR025
title: Unified codec catalogue defines every persistent format
date: 2026-08-09
status: Accepted
version-added: 3.0.0
summary: Declare persistent formats once and use that immutable declaration throughout writing, reading, inspection, validation and migration.
areas: [codecs, store, indexing, migration, extensibility]
---

# ADR025: Unified codec catalogue defines every persistent format

- **Date:** 2026-08-09
- **Status:** Accepted

## Context

LeanCorpus 2.x has several independent descriptions of its persistent formats.
`CodecConstants`, `CodecFormats`, `CodecMigrationRegistry`, `CodecFormatTable`,
production readers and writers, migration extension lists, temporary-file
recognition and typed CodecKit formats can each describe a different part of the
same file.

These descriptions have drifted. Several version constants are newer than their
registered version cases, and `CodecFormats.Create` selects the first registered
case rather than enforcing its `currentVersion` argument. Normal indexing can
therefore write an older envelope generation after migration has written the
declared current trailer generation. Inspection also handles some headers through
`BinaryReader` and others through `IndexInput`, while compound files hide the
versions of their logical members.

The mutable process-wide `CodecMigrationRegistry.Default` is not a complete
extension point. It permits silent replacement by codec ID but does not register
file matching, inspection, validation, migration, compound-file handling or
temporary-file cleanup. This is unsuitable for plugins, AOT, deterministic tests
and processes hosting indexes with different format sets.

LeanCorpus 3.0 must continue to read supported indexes from 2.0 onwards,
but it must write one self-identifying current storage generation. Large postings,
vector, BKD and graph files must remain streaming or random access rather than
being forced through `byte[]`.

## Decision

### One immutable catalogue

Introduce `CodecCatalogBuilder` and immutable `CodecCatalog`. The default
catalogue is built statically from built-in declarations. A host can create a
different immutable catalogue by adding explicit third-party declarations before
calling `Build()`. Registration discovery must not use reflection.

`CodecMigrationRegistry`, `CodecFormatTable`, format-version constants,
extension-based migration switches and temporary-file lists cease to be
independent authorities. The mutable migration registry and disconnected typed
format specifications are removed. Fixed internal legacy envelopes remain only
where supported older files require them, and only catalogue declarations may
define current metadata.

Every persistent format is represented, including externally framed JSON and
container formats such as `.seg`, `segments_N`, statistics files and `.cfs`.
Those descriptors use an explicit external or container framing policy rather
than pretending to be canonical binary frames.

### Families and file roles

The catalogue has two descriptor levels:

- `CodecFamilyDescriptor` identifies a logical subsystem and any coordinated
  migration or cross-file validation. Stored fields, term vectors and vectors
  are examples of multi-file families.
- `CodecFileDescriptor` identifies one logical file role. It contains its stable
  format ID, family, display name, file matcher, current format version,
  supported versions, framing policy, access kind, checksum policy, validation,
  migration capability and temporary-file patterns.

A file matcher can recognise fixed extensions and generated names such as
per-field vector sidecars. An extension is an indexing aid, not the format's
identity. Multiple physical files may match one intentional generated role, but
two descriptors must not make overlapping claims.

Each supported body version records its integer version, diagnostic label, body
handler or specialist reader, legacy framing, read support and migration
behaviour. Materialised formats may use `ICodec<T>`. Sequential and random-access
formats retain specialist body implementations over bounded inputs. A typed
`CodecKit/Formats` definition must either be used by production persistence,
describe an authoritative structured subcomponent, or be removed.

`Build()` rejects:

- duplicate format IDs or family IDs;
- silent replacement of a built-in;
- duplicate or ambiguous physical claims;
- invalid or non-namespaced third-party identifiers;
- empty, duplicate or unordered version sets;
- a current version absent from the supported set;
- a current version that is not the newest writable version;
- missing migration, validation or temporary-file metadata required by the
  descriptor's declared behaviour.

The catalogue is the only editable source of the current format version. If
compile-time constants remain necessary, they are generated or derived from that
source.

### Frame and body versions are independent

Canonical binary files use the self-identifying frame specified by ADR026. The
frame version describes the physical wrapper. The format version describes the
body layout. Moving an unchanged Norms v3 body from a legacy trailer to Frame v1
does not create Norms v4.

Frame version and format version remain separate in APIs, diagnostics, inspector
output, compatibility decisions and migration plans. Current 3.0 writers never
emit a legacy envelope, trailer or custom header.

### One current storage lifecycle

Direct writes, flushes, merges and migration all resolve the same descriptor and
terminate at the same current writer. Call sites do not choose a current version
or frame themselves.

`CodecFileWriter` owns canonical framing. A write session exposes append-only
body operations and position, with `IBufferWriter<byte>` support where useful.
It does not expose arbitrary seeking. A specialist format that requires metadata
backpatching must precompute or bound that metadata, or use an explicitly reviewed
specialist path.

A caller must invoke `Complete()` to write a valid footer. Disposal without
completion leaves an incomplete frame. The atomic write helper owns recognised
temporary-file creation, completion, disposal, durable flushing when requested,
close-before-rename publication and failure cleanup. This preserves ADR010's
Windows lifetime requirement.

`CodecFileReader.Open` returns frame metadata and a bounded body `IndexInput`
view without materialising the body. Explicit `Read<T>`, `ReadBody` and `Validate`
operations cover structured decoding, bounded byte-array materialisation and
integrity validation respectively. Stream APIs adapt to this implementation and
must not contain another framing parser.

### Compatibility and legacy readers

LeanCorpus 3.0 reads the 2.x generations that are intentionally listed in
catalogue version history and writes only the canonical generation. Opening and
searching a supported old index remains allowed. Mutating an old-format index
continues to require migration unless a later decision explicitly relaxes that
policy.

Legacy support is read-only and descriptor-driven. Internal readers cover the
legacy CodecKit envelope, legacy trailer and the specialised stored-fields and
postings headers. Heuristic trailer detection remains confined to these legacy
readers. Once canonical magic is present, a malformed or unsupported canonical
frame is rejected and is never retried as legacy data.

Normal reads reject unknown future frame or body versions before semantic
decoding. Inspection may report their structural metadata without decoding.
Recovery tooling may request raw body access only through an explicit forensic
operation.

### Logical files and compound storage

Codec consumers operate on logical files through an abstraction such as
`ISegmentFileSource`. Loose storage opens a file directly. Compound storage opens
the existing bounded memory-mapped slice described by ADR024. Body readers do not
know which physical source supplied the logical input.

Inventory, inspection, compatibility, validation and migration enumerate the
members of `.cfs` as logical files. The container's own magic, version and
directory are validated separately. Compatibility decisions use member frame and
format versions rather than treating the container as proof that its members are
current.

Migration never patches a member in place. It opens the source container, stages
migrated and unchanged logical members as loose files, validates them, repacks
them with the normal compound writer, validates the new container, closes all
handles and publishes atomically.

### Descriptor-driven migration

Each file or family declares one migration capability:

- `None` for no migration work;
- `Reframe` when body bytes can be streamed unchanged into the canonical frame;
- `Rewrite` when an old semantic body must pass through the normal current writer;
- `CoordinatedRewrite` when a family such as `.fdt` and `.fdx` must be rewritten
  atomically;
- `Unsupported` when inspection is possible but rebuilding is required.

Migration planning is based on catalogue inventory and family actions rather
than extension switches. Reframing streams bytes without materialisation.
Rewriting invokes the same current writer as flush and merge. A migrated segment
must remain current after a later merge.

### Integrity, limits and validation

Canonical binary descriptors record a checksum unless a descriptor has a
documented reason to opt out. xxHash64 is the default. Fast open validates frame
structure, identity, versions, flags, footer consistency and ranges without
scanning a large body. Materialising reads verify while consuming the body, and
deep validation recomputes every available checksum.

Limits distinguish nested codec frames, explicitly materialised bodies, physical
codec files, sequences, strings, scratch buffers, decompression and nesting. A
multi-gigabyte random-access file can be valid while `ReadBody` correctly rejects
materialising it. Declared lengths are checked for sign, representation,
operation-specific limits and containment before allocation or slicing.

Validation is layered into storage or container checks, frame checks, semantic
body checks and cross-file family checks. Inspector, compatibility, validator,
migrator, recovery and CLI tooling consume the same catalogue-backed logical
inventory. They do not infer semantics independently from a filename.

### Configuration and public API

Normal users receive `CodecCatalog.Default`. Explicit catalogue configuration is
passed through writer, reader, inspection, compatibility and migration options as
required. Multiple indexes in one process may use different immutable catalogues.

`ICodec<T>` and the immutable checksum-provider `CodecRegistry` remain. The new
name `CodecCatalog` avoids conflating persistent-format declarations with
checksum-provider registration. Misleading 2.x format and migration APIs may be
removed in this major release rather than preserved indefinitely.

## Rationale

A single declaration makes version drift and incomplete third-party registration
structurally preventable. Immutability makes behaviour deterministic after index
configuration and avoids process-wide plugin races. Family descriptors model the
actual atomicity of paired files instead of deduplicating file actions later.

Owning only framing and metadata at the common layer preserves the performance
properties of sequential and random-access codecs. Requiring every body to be an
`ICodec<byte[]>` would reintroduce the buffering that ADR001 and ADR009 sought to
remove.

Treating compound members as logical files preserves ADR024's zero-copy slices
while removing a blind spot in inspection and migration. Keeping legacy framing
behind explicit read-only adapters preserves supported indexes without allowing
old writing paths to remain current architecture.

## Consequences

- 3.0 has one authoritative declaration and current writer for each persistent
  format.
- Canonical framing is defined separately by ADR026 and no longer forces body
  version bumps.
- Flush, merge, direct writes and migration cannot select different generations.
- Existing supported indexes remain readable, but old-format mutation requires
  migration and older LeanCorpus releases cannot read newly written 3.0 frames.
- Third-party formats register explicitly through an immutable builder and must
  declare their complete storage integration.
- Compound inspection and migration operate on member files and validate the
  container separately.
- Large files retain streaming and random-access readers. Whole-file allocation
  and checksum scans are explicit operations.
- The 2.x registries, duplicate tables, legacy current writers and extension
  switches are removed as consumers move to the catalogue.
- Golden frame tests, generated catalogue invariants, historical fixtures,
  corruption tests, migration monotonicity tests and Windows and Linux coverage
  are required before 3.0 release.
