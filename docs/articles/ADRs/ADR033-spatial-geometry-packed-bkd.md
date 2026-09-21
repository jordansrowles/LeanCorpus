---
adr: ADR033
title: Spatial geometry uses canonical encoded coordinates and multidimensional Packed BKD
date: 2026-09-21
status: Accepted
version-added: 3.2.0
summary: Define the immutable Geo and XY geometry foundation and deterministic Packed BKD v1 format.
areas: [spatial, indexing, storage, codecs, performance]
---

# ADR033: Spatial geometry uses canonical encoded coordinates and multidimensional Packed BKD

- **Date:** 2026-09-21
- **Status:** Accepted

## Context

Later 3.2 spatial features need a common geometry contract and a multidimensional
point structure. The existing `.bkd` and `.bkdl` formats are established 1D
numeric structures and must remain byte-compatible. A new format must therefore
be independently versioned, bounded, deterministic and usable in loose or
compound segment storage without introducing a managed object per point or tree
node.

## Decision

Geo and XY geometry are separate public models. Small points, rectangles and
circles are immutable value types. Line strings, polygons and geometry
collections are sealed immutable reference types which copy caller-owned arrays
once. Geo APIs use `double` latitude/longitude and metre radii. XY APIs use
finite `float` coordinates and coordinate-unit radii. Public geometry contains no
indexing or tessellation state.

All constructors use shared validation and canonicalisation rules. Coordinates
reject non-finite values. Rings become explicitly closed, consecutive duplicate
vertices are removed, winding is normalised, and invalid or ambiguous topology
is rejected rather than repaired. Geo rectangles use `west > east` for a
dateline crossing. Lines and rings unwrap successive longitudes, split genuine
anti-meridian crossings and wrap canonical components back into the legal
longitude range. Poles are never wrapped.

Geo indexed coordinates use the existing 32-bit encoded latitude and longitude
ordering. XY coordinates use the IEEE `float` bits transformed to sortable
unsigned order. Packed BKD dimensions are always four opaque bytes whose
unsigned lexicographic order is logical coordinate order. Structural integers in
the codec remain explicit little-endian values.

Packed BKD is a native LeanCorpus block KD implementation derived from the
Lucene BKD algorithmic shape, not from Lucene's historical file format. It
supports one to sixteen total dimensions, one to eight indexed dimensions,
exactly four bytes per dimension in v1, and one to 4096 points per leaf. The
required proof configurations are 2D/2-indexed and 7D/4-indexed. Split choice
uses under-used indexed dimensions, then the largest unsigned encoded span;
MSD/radix partitioning and final leaf ordering use the selected dimension, the
remaining packed dimensions and document ID as deterministic tie-breakers.

Build input uses fixed-width records in pooled contiguous buffers. The default
additional build budget is 16 MiB and includes rented capacity and scratch
state. The in-memory and offline spill builders share the same tree shape and
ordering, and identical logical input produces byte-identical output. Spill
files are operation-scoped build artefacts and are deleted on success, failure
and cancellation.

The new `leancorpus.numeric-structures.packed-bkd` format is version 1, uses
the `.pbkd` extension, is random access, and is framed by the canonical LCCF
Frame v1 with xxHash64. One logical file per segment contains field sections
written in ordinal UTF-8 field order, followed by a tail directory and `PBKD`
footer. Field sections persist exact indexed bounds, implicit split arrays,
bounded leaf offsets, minimum-plus-delta document IDs and either raw or strictly
smaller per-dimension common-prefix values. There is no migration from `.bkd`
or `.bkdl` to `.pbkd`.

Readers retain a bounded logical body input and create no managed node graph.
Traversal uses bounded per-query state and validates all counts, dimensions,
offsets, bounds, split metadata and leaf encodings before deriving slices or
allocating. Existing mmap, compound-file, deletion and operation-drain
lifetimes remain authoritative.

## Rationale

Separate Geo and XY types keep coordinate semantics explicit and avoid a generic
geometry framework that would blur validation and precision rules. Canonical
encoded bytes make ordering independent of host endianness and give the BKD
builder one comparison contract for both families.

The packed format is additive, so established numeric indexes and their readers
are untouched. A tail directory permits append-only canonical frame writing
without backpatching. Exact leaf bounds improve crossing-cell pruning, while
bounded leaves keep the reader's query memory proportional to tree depth rather
than point count.

## Consequences

- New geometry APIs are public, immutable and independent of query execution.
- `GeoEncodingUtils` retains its existing public outputs while exposing the
  shared sortable-coordinate primitives needed by Packed BKD.
- `PackedBkdFieldBuffer`, writer, reader and traversal remain internal until a
  later sprint attaches public point fields and queries.
- `.bkd` and `.bkdl` constants, bytes and migration behaviour do not change.
- Flush and merge may carry packed field buffers through the existing detached
  snapshot boundary; failed work must dispose pooled state and remove spill
  artefacts.
- Canonical format tests must lock exact bytes, deterministic ordering, spill
  equivalence, corruption rejection, compound reads and Native AOT behaviour.

## References

- [ADR001: Span-based body encoding](ADR001-span-body-encoding.md)
- [ADR005: Each DWPT flushes its own segment](ADR005-dwpt-segment-flush.md)
- [ADR010: IndexOutput must be disposed before File.Move](ADR010-close-before-rename-migration.md)
- [ADR011: Lazy segment reader lifetimes](ADR011-lazy-segment-reader-lifetimes.md)
- [ADR024: Compound segment files](ADR024-memory-mapped-compound-segment-files.md)
- [ADR025: Unified codec catalogue](ADR025-unified-codec-catalogue.md)
- [ADR026: Canonical binary file frame](ADR026-canonical-binary-file-frame.md)
- [ADR027: Memory-mapped operation lifetimes](ADR027-memory-mapped-operation-lifetimes.md)
- Apache Lucene BKD reference reviewed at `15f6f15cfc9564cc41f84b3affaf03485cec18ca`.
