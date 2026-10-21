---
adr: ADR039
title: "Dense vector data persists explicit sparse presence"
date: 2026-10-04
status: Accepted
version-added: 4.0.0
summary: "Persist per-document presence in vector body v2 and reconstruct legacy presence only from persisted HNSW membership."
areas: [codecs, indexing, search, migration]
relates-to: [ADR025, ADR026]
---

# ADR039: Dense vector data persists explicit sparse presence

- **Date:** 2026-10-04
- **Status:** Accepted

## Context

Vector data v1 stores dense rows addressed by segment document ID. Missing values
are padded, so the data body cannot distinguish an explicitly supplied zero
vector from an absent vector. Graph membership retains that distinction when a
persisted HNSW graph exists. Flat search and merge must retain the same logical
presence independently of vector contents and destination encoding.

## Decision

Advance `leancorpus.vectors.float32` and `leancorpus.vectors.quantised` to body v2.
Retain readable v1 bodies and write only v2, through the ADR025 catalogue and
ADR026 canonical frame. HNSW remains body v1.

Both data bodies contain `docCount:int32`, `dimension:int32`, a format or
quantisation byte, then one presence bit per segment document. The bitmap length
is implicit, `checked((docCount + 7) / 8)`. Bits follow increasing document IDs,
least-significant bit first; unused high bits must be zero. Existing parameters,
corrections and dense payload follow the bitmap without changing their semantics.
There is no separate sidecar and no value-based absence sentinel.

Flush and merge supply authoritative present IDs. Search, reranking and merge
consult presence before accessing or scoring vectors. Merge remaps only live,
present documents through destination sort order and retains the destination
quantisation policy. Explicit zero vectors remain present.

Migration is an executable structural rewrite when owning segment metadata
identifies a persisted HNSW graph. Validate unique, in-range graph membership and
use exactly the base-level document-ID set. Insert the bitmap and preserve every
remaining v1 vector byte, including Int8/BBQ parameters, corrections and packed
payload. Reassociate the unchanged HNSW body. Compound migration materialises,
rewrites and repacks logical members through existing staged publication.

Without persisted graph membership, v1 presence is not reconstructable. Planning
reports a non-executable `UnsupportedMigrationPath`; the index must be rebuilt
from original documents. Dense non-zero values cannot prove presence either.
Presence-dependent segment reads also reject ambiguous v1 fields, while the
historical dense body readers remain available for inspection and migration.

## Rationale

Keeping dense payload addressing preserves existing readers' random-access and
quantised-distance contracts. An embedded bitmap records logical field state
independently of representation and graph availability. Persisted graph node IDs
are the only authoritative presence source in v1; inferring from values loses
explicit zeros. Copying legacy payload bytes avoids requantisation drift.

## Consequences

- Vector v2 adds one bit per document to each vector data field.
- Corruption checks cover bitmap padding, body bounds, vector metadata and graph
  document-ID membership before exposing vector state.
- v1 to v2 is a one-way 4.0 persisted-format boundary; 3.1.1 cannot read rewritten
  vector files.
- GitLab issue 8 release fixtures require sparse vectors and persisted HNSW;
  focused issue 26 tests cover both quantised variants and publication retry.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-10-04 | Accepted | Original decision accepted. | `2228d0d52` |
