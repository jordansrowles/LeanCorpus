---
adr: ADR024
title: Compound segment files use memory-mapped slices
date: 2026-08-05
status: Accepted
version-added: 2.2.0
summary: Pack immutable segment codec files into one compound file and read them through bounded memory-mapped slices.
areas: [store, indexing, search, backup, operations]
---

# ADR024: Compound segment files use memory-mapped slices

- **Date:** 2026-08-05
- **Status:** Accepted

## Context

Small segments create many codec files and therefore many filesystem entries and
file-lifetime records. The earlier compound implementation packed files but
extracted them again before reading, which removed the operational benefit and
could temporarily double storage.

Compound files also need to coexist with the lazy segment readers, deferred file
deletion, incremental backups, and mutable live-document files. A reader must be
able to open a member without materialising it or keeping an `IndexWriter` lock.

## Decision

When `IndexWriterConfig.UseCompoundFile` is enabled, immutable codec files are
packed into `<segment>.cfs`. The small `<segment>.seg` metadata file remains
outside the container so the index can discover the compound flag. Deletion
files and `<segment>.stats.json` remain outside because they are replaced after
the segment is created. Existing non-compound segments remain readable.

The `.cfs` format has a fixed magic and version, a bounded directory containing
the member name, byte offset, and byte length, followed by the member bytes.
Readers validate every directory range against the container length and open a
member with an `IndexInput` slice. One owning input maps the physical `.cfs` file;
member inputs are lightweight bounded views that share its pointer and lifetime.
The owner cannot unmap until every member view has drained, so codecs retain their
existing zero-copy reads without creating one Windows mapping object and view per
logical member.

## Rationale

Memory-mapped slices preserve the current codec APIs and avoid both extraction
files and full-file byte arrays. Sharing the container mapping also bounds physical
handles and mapping objects by open compound readers rather than logical members.
Keeping mutable sidecars outside the compound file makes deletion updates atomic
and lets the existing lifetime registry defer cleanup of the single immutable
container.

## Consequences

- Compound segments have one `.cfs` plus `.seg` and mutable sidecars instead of
  one file per immutable codec component.
- Stored fields use a seekable `IndexInput` stream adapter; other codecs read
  directly from mapped slices.
- Member inputs have independent positions and bounds but retain their shared
  mapping owner until disposal.
- Backup, validation, format inspection, sizing, migration, and cleanup must
  recognise `.cfs` as the immutable segment payload.
- The feature is opt-in to avoid changing the file layout of existing writers.
