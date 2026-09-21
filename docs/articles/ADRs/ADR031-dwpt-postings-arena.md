---
adr: ADR031
title: DWPT postings use a pooled block arena
date: 2026-09-21
status: Accepted
version-added: 3.2.0
summary: Keep mutable postings in one pooled DWPT store and stream immutable snapshots directly to segment files.
areas: [indexing, storage, performance]
---

# ADR031: DWPT postings use a pooled block arena

- **Date:** 2026-09-21
- **Status:** Accepted

## Context

Indexing previously kept a separate accumulator and managed arrays for each
term, with additional block-pool abstractions used by parts of the postings
path. Detached flushing then had to transfer, sort and interpret those
per-term structures before writing the segment. This made retained memory,
ownership and index-sort remapping harder to reason about, while duplicate
term and posting representations increased allocation pressure.

The replacement must preserve the existing public indexing API, codec IDs,
postings file format, term-vector format and compatibility with existing
segments. It must also support mixed field index options, pending final
documents, payloads, offsets, concurrent DWPT ownership and deterministic
failure cleanup.

## Decision

Each `DocumentsWriterPerThread` owns one `PostingsStore`. The store contains:

- a `BytesRefHash` for qualified UTF-8 field and term bytes;
- a pooled, reference-free `PostingTermState` array for per-term metadata;
- a `PostingsByteArena` made from rented 32 KiB blocks and forwarding slices;
- stack-only document and proximity readers used by the flush path.

Arena addresses are one-based, with zero representing an absent stream.
Document, position, payload and offset data are encoded as compact primitive
records. A term's final document remains pending in its state until a reader
opens the frozen snapshot. The effective term flags are the union of all field
index options seen for that term; a richer later occurrence does not discard
earlier documents.

Snapshot capture freezes and transfers the store to `DwptFlushSnapshot`, then
installs a fresh store in the live DWPT. Ordinary flush sorts compact term IDs
against the owned UTF-8 pool and streams document and proximity data directly
to the existing segment writers. The FST receives term spans directly from the
same pool.

Index sorting never mutates the captured store. It retains the inverse
permutation and materialises at most one term into pooled
`IndexSortPostingScratch`, including remapped document IDs, positions, payloads
and offsets, before writing the sorted postings and term vectors.

All stores, arenas, term pools and scratch arrays have idempotent disposal.
RAM accounting reports the owned rented capacities rather than a heuristic per
term estimate. The old accumulator and production block-pool types are not
retained as compatibility layers.

## Rationale

- One store gives each DWPT a single ownership and disposal boundary.
- Sliced byte streams remove per-term managed posting arrays while retaining
  bounded growth and zero-copy reads during flush.
- UTF-8 term spans avoid decoding and re-encoding qualified terms in ordinary
  flushes.
- A one-term sort scratch keeps index-sort memory proportional to the largest
  term instead of the complete snapshot.
- The decision changes only in-memory construction and ownership. Existing
  readers and on-disk formats remain authoritative.

## Consequences

The flush path must finish pending final documents and consume proximity
payloads in their encoded order. Corruption checks are performed by the arena
readers before bytes are copied to segment outputs. A failed or abandoned
snapshot must dispose its store exactly once, including when writer shutdown
and flush failure paths overlap.

Field ordinals are an internal optimisation and are not persisted as a public
contract. Term vectors continue to use their existing materialised entry model
because their output is document-oriented. Index sort pays the bounded
one-term materialisation cost when enabled.

## References

- [ADR005: Each DWPT flushes its own segment](ADR005-dwpt-segment-flush.md).
- [ADR025: Unified codec catalogue](ADR025-unified-codec-catalogue.md).
- [ADR026: Canonical binary file frame](ADR026-canonical-binary-file-frame.md).
- [ADR027: Memory-mapped operation lifetimes](ADR027-memory-mapped-operation-lifetimes.md).
- [ADR029: Platform filesystem durability](ADR029-platform-filesystem-durability.md).
