---
adr: ADR034
title: Shape values use stable Packed BKD primitives and document-level relations
date: 2026-09-24
status: Accepted
version-added: 3.2.0
summary: Freeze the shape primitive bytes, value identity, field metadata, tessellation boundary and Geo/XY document relation semantics.
areas: [spatial, indexing, search, storage, compatibility]
---

# ADR034: Shape values use stable Packed BKD primitives and document-level relations

- **Date:** 2026-09-24
- **Status:** Accepted

## Context

Sprint 3 adds Geo and XY shape indexing and four document-level relations on the
existing Packed BKD v1 structure. The format already supports a value with seven
dimensions and four indexed dimensions, so shapes must use those bytes without
changing the `.pbkd` frame, version or descriptor. The primitive bytes and
logical field-value identity become compatibility contracts because flush,
reopen, query, compound-file reads and merge all consume them.

## Decision

### Field metadata and value identity

Each segment may contain additive `SpatialFieldInfo` entries. The persisted
kind is one of `GeoPoint`, `XYPoint`, `GeoShape` or `XYShape`, and a field name
has at most one kind per segment. Older segment metadata without this list
deserialises as an empty list. A query validates any recorded kind before
interpreting the field bytes. Merge rejects conflicting recorded kinds before
writing the merged segment. Point and shape data remain distinguishable without
changing the generic Packed BKD field directory.

Every occurrence of a shape field name in a document has a 0-based value
ordinal in document field order. It is stored in the upper 26 bits of the
primitive metadata word. The ordinal is document-local, remains unchanged when
merge remaps document IDs, and is shared by all primitives emitted from one
geometry collection. Ordinals above `0x03FF_FFFF` are rejected before the
Packed BKD buffer is modified.

### Primitive bytes

One shape primitive is exactly 28 bytes and uses
`PackedBkdConfig.Shape7D4Indexed(IndexWriterConfig.BKDMaxLeafSize)`:

| Dimension | Value | Encoding |
|---|---|---|
| D0 | minimum Y | four-byte sortable coordinate |
| D1 | minimum X | four-byte sortable coordinate |
| D2 | maximum Y | four-byte sortable coordinate |
| D3 | maximum X | four-byte sortable coordinate |
| D4 | reconstruction Y | four-byte sortable coordinate |
| D5 | reconstruction X | four-byte sortable coordinate |
| D6 | metadata | big-endian `UInt32`, not indexed |

Geo X is longitude and Geo Y is latitude, encoded with the existing 32-bit Geo
encoder. XY coordinates use the current sortable-float encoding. The decoder
reconstructs triangle vertices from D0-D5 and the low three bits of D6. The
reconstruction code table is fixed:

| Code | Reconstruction order |
|---|---|
| 0 | MINY_MINX_MAXY_MAXX_Y_X |
| 1 | MINY_MINX_Y_X_MAXY_MAXX |
| 2 | MAXY_MINX_Y_X_MINY_MAXX |
| 3 | MAXY_MINX_MINY_MAXX_Y_X |
| 4 | Y_MINX_MINY_X_MAXY_MAXX |
| 5 | Y_MINX_MINY_MAXX_MAXY_X |
| 6 | MAXY_MINX_MINY_X_Y_MAXX |
| 7 | MINY_MINX_Y_MAXX_MAXY_X |

D6 allocates bits 0-2 to the reconstruction code, bit 3 to AB source-edge
provenance, bit 4 to BC provenance, bit 5 to CA provenance, and bits 6-31 to
the value ordinal. No primitive-type or orientation bits exist. Equal A/B/C
coordinates mean point, two unique coordinates mean line, and three unique
coordinates mean a counter-clockwise triangle. Points and lines use canonical
source-edge flags. Invalid length, bounds, degeneracy, orientation, flags,
ordinal or coordinate encoding fails with `InvalidDataException`.

### Preparation and persistence

Geo coordinates are encoded and decoded before tessellation. One common
unwrapped longitude frame is used for shell and holes. XY uses canonical finite
float values and `double` intermediates. Polygon holes are bridged and rings
are bridged and ear-clipped deterministically; original shell and hole segments
define source-edge provenance. Ear clipping uses an active linked ring, scans
from its stable head, and removes the first admissible ear. Strictly convex
hole-free rings use a deterministic fan with the same boundary-edge rules.
Rings with at least 64 active vertices use a Morton-sorted z-order index to
limit point-in-ear candidates; the index only removes points outside a
conservative triangle bounding box and does not change ear order. Hole bridges
test candidate vertices by squared distance, then X, Y and ring ordinal, and
select the first visible candidate. Hole touching/intersection and invalid
topology are rejected, not repaired. Output order and tie-breaking use
canonical input order.

One ring or line has a 100,000-vertex limit. A polygon's shell and holes share
that limit. A flat geometry collection is limited to 100,000 combined
components and input vertices. Tessellation output is limited to 1,000,000
primitives per shape value. Ear clipping accepts no more than 200,000 merged
ring vertices after bridge duplication and stops after `n*n + n` candidate
checks. These bounds reject excessive input before Packed BKD mutation.

Geo triangles and lines are split at each crossed `180 + 360*k` seam. Clipped
pieces are translated to the normal longitude range; seam cut edges are
internal and retained source boundaries keep their provenance. No persisted
primitive may represent the long way around the globe. Rectangles use the same
polygon preparation path. Shape values are appended to the existing Packed BKD
buffer and transfer through the normal DWPT snapshot, flush, compound-file and
merge lifecycle. Merge validates source checksums, retains value ordinals and
rebuilds with the target leaf size. Sprint 3 relation execution remains
independent of Shape DocValues. Sprint 4 adds the optional `.dvg` operational
metadata representation under ADR035; it does not change primitive bytes or
relation semantics.

### Relations

Shape queries are constant-score filters. Boundaries are inclusive. For indexed
field values `G1...Gn` and query union `Q`, matching is defined as:

```text
Intersects: ANY Gi intersects Q
Within:     ALL Gi are within Q
Contains:   ANY ONE Gi contains the complete Q
Disjoint:   ALL Gi are disjoint from Q
```

A missing field matches none of the relations. Separate shape field values are
never combined to satisfy `Contains`. Components of a geometry collection
inside one field value may collectively contain the query. A query geometry
collection is one union query. Boundary equality yields Intersects, Within and
Contains true, and Disjoint false.

Intersects uses the Packed BKD indexed dimensions for conservative cell
pruning, then validates and tests primitive bytes. Within and Disjoint retain
per-document evidence across every indexed primitive, including failed values;
cells cannot be skipped when their document IDs are needed. Contains retains
state by `(docId, valueOrdinal)` and never uses value-less bulk visits for a
candidate. Geo circles are analytic queries using the existing Haversine
convention and Earth radius of 6,371,000 metres. Query geometry fingerprints
use built-in coordinate values in invariant round-trip form, so cache and
search-session identity does not depend on runtime object hashes.

## Consequences

- `.pbkd` v1 bytes, frame, version and descriptor remain unchanged.
- Shape fields have no Stored Fields representation in 3.2. Optional Shape
  DocValues and spatial aggregation semantics are defined by ADR035.
- Circle shapes remain query-only.
- Persisted spatial kind metadata prevents Geo/XY and point/shape reinterpretation.
- Stable primitive bytes and ordinals are covered by golden tests and remain
  compatible across segment merge and compound storage.
- Malformed primitive bytes fail rather than being treated as absent data.
