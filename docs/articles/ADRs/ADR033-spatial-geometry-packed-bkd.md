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
indexing or tessellation state. The marker interfaces are `IGeoGeometry` and
`IXYGeometry`; they describe the built-in geometry models but do not register
custom indexable geometries. LeanCorpus indexing supports the documented
built-in Geo and XY geometry types in 3.2. `GeoEncodingUtils` exposes
`NormaliseLongitude`. The internal proof configurations are
`PackedBkdConfig.Point2D()` and `PackedBkdConfig.Shape7D4Indexed()`.

All constructors use shared validation and canonicalisation rules. Coordinates
reject non-finite values. Rings become explicitly closed, consecutive duplicate
vertices are removed, winding is normalised, and invalid or ambiguous topology
is rejected rather than repaired. Geo polygon shells and holes are validated in
one common unwrapped world before seam expansion, including complete shell,
hole and cross-ring relationships. Complete or multiple world wraps are
rejected. The complete polygon is then quantised onto the encoded grid and the
same topology is validated again. Geo rectangles use `west > east` for a
dateline crossing. Lines and rings unwrap successive longitudes, split genuine
anti-meridian crossings and wrap canonical components back into the legal
longitude range. Poles are never wrapped. XY topology calculations use
`double`, and signed zero has one canonical encoded representation.

Geo indexed coordinates use the existing 32-bit encoded latitude and longitude
ordering. XY coordinates use the IEEE `float` bits transformed to sortable
unsigned order. Packed BKD dimensions are always four opaque bytes whose
unsigned lexicographic order is logical coordinate order. Structural integers in
the codec remain explicit little-endian values.

Packed BKD is a native LeanCorpus block KD implementation derived from the
Lucene BKD algorithmic shape, not from Lucene's historical file format. It
supports one to sixteen total dimensions, one to eight indexed dimensions,
exactly four bytes per dimension in v1, and one to 4096 points per leaf. The
existing public `IndexWriterConfig.BKDMaxLeafSize` remains at least two because
it also controls the established 1D BKD writer. Internal Packed BKD
configuration and the `.pbkd` v1 reader may use a leaf size of one; Sprint 1
adds no separate public Packed BKD leaf-size setting. The
required proof configurations are 2D/2-indexed and 7D/4-indexed. Split choice
uses under-used indexed dimensions, then the largest unsigned encoded span.
Internal nodes use deterministic MSD/radix selection and partitioning at the
required rank, not recursive full subtree sorting. Full deterministic ordering
is performed only for final leaves, using the selected dimension, the remaining
packed dimensions and document ID as tie-breakers. Root bounds are exact, child
bounds inherit from their parent, and fields with more than two indexed
dimensions refresh exact slice bounds at every fourth split depth.

Build input uses fixed-width records in pooled contiguous buffers. The default
additional build budget is 16 MiB and is a hard limit for all additional
Packed BKD-owned managed build state. It includes actual rented capacity and
scratch state, including the ordering vector, histograms, bounds, leaf metadata,
leaf scratch, document counting and retained leaf data. The complete source
buffer is not cloned. The in-memory and offline spill builders share the same
tree shape, selection key and ordering, and identical logical input produces
byte-identical output. Spill files are operation-scoped build artefacts and are
deleted on success, failure and cancellation. Cancellation is checked during
active selection, partitioning, bound refresh and encoding.

The new `leancorpus.numeric-structures.packed-bkd` format is version 1, uses
the `.pbkd` extension, is random access, and is framed by the canonical LCCF
Frame v1 with xxHash64. One logical file per segment contains field sections
written in ordinal UTF-8 field order, followed by a tail directory and `PBKD`
footer. Field sections persist exact indexed bounds, implicit split arrays,
bounded leaf offsets, minimum-plus-delta document IDs and either raw or strictly
smaller per-dimension common-prefix values. There is no migration from `.bkd`
or `.bkdl` to `.pbkd`.

Readers retain a bounded logical body input and create no managed node graph.
Normal open validates the LCCF frame and Packed BKD tail directory only. Field
metadata is compact and stores offsets without allocating split or leaf arrays,
walking every node or decoding every payload. Each query opens its own bounded
field cursor, validates only the splits and leaves it touches, compares exact
leaf bounds before renting reusable DocID scratch, and keeps the complete
operation under the segment read lease. Full checksum and whole-tree semantic
validation is an explicit `DeepValidate()` operation. Bounds are compared
independently for every indexed dimension. A present corrupt `.pbkd` propagates
an error rather than becoming an absent field. Existing mmap, compound-file,
deletion and operation-drain lifetimes remain authoritative.

Packed BKD build and traversal activities reuse the existing LeanCorpus activity
source. They record useful build, spill, output, pruning and decode counters only
when an observer is active.

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
- Packed BKD remains internal to the LeanCorpus assembly. Its subsystem
  contracts live in `Codecs.PackedBkd`, while low-level implementation helpers
  live in `Codecs.PackedBkd.Internal`.
- Implementation-only geometry helpers live under `Search.Geo.Internal` and
  `Search.XY.Internal`; shared coordinate encoding lives under
  `Search.Internal`.
- Public geometry marker interfaces are closed in supported behaviour for 3.2
  and do not register custom geometries for indexing.
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
