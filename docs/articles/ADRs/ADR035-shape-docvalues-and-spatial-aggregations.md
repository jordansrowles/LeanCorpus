---
adr: ADR035
title: Shape DocValues preserve encoded primitives and spatial metadata
date: 2026-09-24
status: Accepted
version-added: 3.2.0
summary: Define the Shape DocValues file, owned primitive reuse, metadata semantics and single-pass spatial aggregations.
areas: [spatial, indexing, storage, codecs, search, aggregations]
---

# ADR035: Shape DocValues preserve encoded primitives and spatial metadata

- **Date:** 2026-09-24
- **Status:** Accepted

## Context

Sprint 3 established the stable 28-byte Geo/XY shape primitive and
`SpatialFieldInfo` metadata, and executes the four shape relations through
Packed BKD. Sprint 4 adds operational per-document shape metadata and spatial
aggregations without changing those bytes or making relations depend on
DocValues. The persisted data must reuse the exact quantised and seam-split
primitive stream already prepared for indexing, remain bounded and lazily
readable, and use the existing CodecKit catalogue and canonical frame.

## Decision

### Format descriptor

Shape DocValues is one additional file role in the existing
`leancorpus.doc-values` family:

| Property | Value |
|---|---|
| Family ID | `leancorpus.doc-values` |
| Format ID | `leancorpus.doc-values.shape` |
| Display name | Shape DocValues |
| Extension | `.dvg` |
| Format version | 1 |
| Access kind | RandomAccess |
| Framing | Canonical LCCF v1 |
| Checksum | xxHash64 |
| Migration | None |
| Legacy framing | None |

`CodecConstants.ShapeDocValuesVersion` is 1. The descriptor uses the existing
catalogue family. No existing codec version or family changes, and there is no
legacy `.dvg` format to migrate.

### One owned prepared primitive stream

Index-time preparation tessellates each shape value once and encodes each
result once into the frozen 28-byte Sprint 3 representation. An owned,
disposable prepared spatial document retains pooled or chunked encoded bytes
and per-value metadata (field index/name/kind, value ordinal and DocValues
choice). It reports its actual owned buffer capacity. Preparation returns that
memory exactly once on success, validation or indexing failure, and
cancellation. It creates no managed object per primitive and retains the
existing one-million-output-primitive limit.

The exact encoded bytes are the only source for both Packed BKD appends and
Shape DocValues tree leaves. Shape indexing does not retessellate or independently
re-encode a second stream. While preparation is owned, its actual capacity is
transient DWPT memory; transferred PBKD and `.dvg` bytes are accounted as
retained DWPT state. Synthetic `primitiveCount * 96` temporary accounting is
not used.

For each enabled shape field in a document, field occurrences are combined
into one record in field order. Their primitive ordinals remain the existing
document-local Sprint 3 value ordinals. Records contain no segment document
ID. A segment `.dvg` contains the field sections for its enabled Geo/XY shape
DocValues fields.

### Top-level `.dvg` body

The canonical LCCF body is zero or more field sections, a top-level field
directory, and a fixed 16-byte footer. Integers in this body are little-endian.

The footer is:

| Offset | Size | Meaning |
|---:|---:|---|
| 0 | 4 | ASCII `SHDV` |
| 4 | 4 | field count, UInt32 |
| 8 | 8 | directory offset relative to body start, UInt64 |

The directory starts with its UInt32 field count. Each entry contains a
bounded strict UTF-8 field name, a UInt64 section offset relative to body start,
and a UInt64 section length. Entries are strictly sorted by Unicode-scalar
ordinal field-name order, matching Packed BKD ordering. Prefix and supplementary
Unicode names are covered by golden tests. Writing is append-only and does not
backpatch offsets.

### Field section and records

Each field section contains its header, record bytes in ascending document ID
order, a fixed record directory and a 16-byte footer.

The 16-byte header is `SHF1`, one coordinate-system byte (`0` Geo, `1` XY),
zero flags and reserved bytes, UInt32 `maxDoc`, and UInt32 `recordCount`.
Each 16-byte directory entry contains UInt32 document ID, UInt64 record offset
relative to the section start, and UInt32 record length. The footer contains
ASCII `SDFT`, UInt32 record count and UInt64 directory offset relative to the
section start.

The reader validates coordinate system, flags and reserved bytes, counts,
directory bounds, strict document ordering, document IDs below `maxDoc`,
non-overlapping in-range records, and matching header/footer counts. Directory
lookup is a binary search through the fixed mapped directory; opening a field
does not allocate an array proportional to its record count.

Each record starts with a fixed 72-byte metadata header:

| Offset | Size | Meaning |
|---:|---:|---|
| 0 | 4 | value count, UInt32 |
| 4 | 4 | primitive count, UInt32 |
| 8 | 1 | highest dimension: point 0, line 1, area 2 |
| 9 | 1 | flags; bit 0 means a Geo wrapped longitude bound crosses the Date Line |
| 10 | 2 | reserved zero |
| 12 | 24 | six raw sortable-coordinate bound values |
| 36 | 32 | four little-endian IEEE754 binary64 centroid accumulators |
| 68 | 4 | tree length, UInt32 |
| 72 | variable | component tree |

Value and primitive counts are positive. Every primitive ordinal is below the
value count. Record length is exactly 72 plus tree length. Dimension and flags
are defined, reserved bytes are zero, all accumulators are finite, and weight
is positive. Any corrupt value fails with `InvalidDataException`.

Geo bounds store bottom/top latitude, wrapped west/east longitude, then
canonical non-wrapped minimum/maximum longitude. The wrapped flag is set when
west exceeds east. These are the minimal circular longitude envelope of the
persisted primitive intervals. XY bounds store minimum/maximum X, minimum/maximum
Y, then two zero slots; XY flags must be zero.

Geo centroid accumulators are latitude numerator, weighted sine longitude,
weighted cosine longitude and weight. XY accumulators are X numerator, Y
numerator, zero and weight. Coordinates divide by weight; Geo longitude is
`atan2(sine, cosine)`. If the circular vector magnitude is at most
`1e-15 * weight`, longitude is deterministically `0.0`.

### Component tree

The tree stores exact 28-byte Sprint 3 primitive encodings. Leaf capacity is
16. At depth 0 partition by X bounding-box centre, depth 1 by Y centre, and
alternate. The unsigned 64-bit centre key is `minCode + maxCode`. Ties use
full primitive byte lexicographic order. Split counts are `count / 2` and
`count - count / 2`. Leaf primitives are lexicographically sorted.

Every node has a 28-byte header: UInt32 total node length; one-byte kind
(`0` internal, `1` leaf); zero flags and reserved bytes; 16 raw D0-D3 subtree
bounds. An internal node then stores UInt32 left length and the left and right
nodes. A leaf stores UInt16 primitive count (1..16), UInt16 zero reserved, then
the primitive bytes. The root length equals record tree length.

Deep validation recomputes leaf bounds from primitive D0-D3 and internal bounds
from child bounds; checks total primitive count and maximum depth 64; decodes
every primitive under the field coordinate system; and validates ordinal range.
Traversal uses bounded slices and node lengths, never a managed node graph.

### Reader, compound storage and merge

Normal open validates the canonical frame and top directory only. Field access
validates its bounded header/footer/directory; record metadata access binary
searches the document directory and reads the fixed header. Tree traversal
occurs only when requested. Whole-file checksum and semantic scans are explicit
deep validation operations, not repeated on metadata reads. Compound access
uses bounded `IndexInput` slices.

Before merge copies records from a source `.dvg`, it validates that source
checksum once. It copies validated record bytes for live remapped documents;
record payloads remain document-local and need no semantic recomputation.

Shape DocValues is optional at indexing. `LatLonShapeField` and `XYShapeField`
default `storeDocValues` to true, with an explicit false option. The PBKD shape
value is written either way. Shape relations continue to use Packed BKD and do
not require `.dvg`.

Before a spatial shape aggregation begins matching-document traversal, every
segment containing the target `GeoShape` field must have compatible ShapeDV
kind/coordinate metadata and
`ShapeDV.RecordCount == PackedBkdFieldMetadata.DocumentCount`. Missing or
incomplete coverage raises a clear `InvalidOperationException`, even if the
current query would not match a document lacking a record. Point aggregations
do not require Shape DocValues.

### Spatial dimension and centroid

The public `SpatialDimension : byte` values are Point 0, Line 1 and Area 2.
Highest dimension wins in a shape record and across aggregation state:
Area > Line > Point. Accumulators are derived from final quantised,
seam-split primitives.

Each point has weight one. Each persisted line segment contributes decoded
coordinate-space Euclidean length and its midpoint. Each non-zero persisted
triangle contributes absolute decoded coordinate-space area and its centroid.
Geo longitude is combined circularly. Lines and areas use the unwrapped and
seam-split indexing coordinates before midpoint/centroid longitude is
normalised. Holes are absent from triangle primitives, so they need no negative
weight. Lower dimensions are ignored once a higher dimension exists. The
record and aggregate use the same weighting. A result's contributing document
count resets when a higher dimension replaces the previous aggregate. Empty
results have null centroid and dimension and count zero. This is an
index-coordinate centroid, not an ellipsoidal GIS centroid.

### Aggregation integration

Existing `AggregationRequest`, `AggregationResult`, numeric overloads and
`NumericAggregator` remain source compatible. Heterogeneous requests and
results use `ISearchAggregationRequest` and `ISearchAggregationResult`; request
order is result order and names are unique, non-empty and non-null. Numeric and
spatial states consume the existing matching-document side collector in one
traversal.

Geo distance aggregation supports `GeoPoint` only and returns metres. Lower
range bounds are inclusive, upper bounds exclusive; null means open-ended;
provided values are finite and non-negative and any pair is strictly
increasing. Overlap and request order are preserved. Each matched document is
counted once per bucket. All exact new Geo point DocValues are consumed, with
legacy `_lat`/`_lon` fallback; Haversine is computed once per point. Missing
fields contribute nothing.

Geo centroid and bounds support `GeoPoint` and `GeoShape`. Point centroid uses
unit-weight points; point bounds contributes point longitude intervals. Both
retain the legacy point fallback. Shape centroid consumes the record
accumulators. Shape bounds traverses persisted leaf primitive longitude
intervals, not only each record's wrapped bound, then applies exact circular
interval union. It merges overlapping/touching intervals, finds the largest
circular uncovered gap and returns its complement. Equal largest gaps choose
the result with numerically smallest normalised west longitude. No positive
gap yields full longitude `[-180,180]`. `wrapLongitude=false` returns
canonical non-crossing minimum and maximum longitude. Missing fields contribute
nothing; empty results have null bounds.

## Rationale

Keeping operational metadata beside, but independent from, Packed BKD preserves
the established relation path while enabling metadata-only aggregation reads.
The shared encoded stream prevents primitive drift between search and
aggregation and makes per-document ordinals stable across persistence and
merge. One side traversal avoids repeated query work for heterogeneous
aggregations.

## Consequences

- `.dvg` is additive and does not change `.pbkd` or any existing codec version.
- Shape DocValues stores quantised operational metadata and primitives, never
  source WKT or the original unquantised geometry.
- Ordinary shape relations remain available when Shape DocValues are disabled
  or absent; only aggregations that require complete shape metadata fail.
- Fast metadata access does not checksum-scan or reconstruct the tree.
- WKT and simplification are explicit public utilities and are not implicit
  indexing transformations.
- Corrupt counts, offsets, frame checksums, record metadata, nodes or primitives
  fail validation rather than being treated as missing data.
