---
adr: ADR037
title: Block joins preserve parent boundaries across deletion and merge
date: 2026-09-26
status: Accepted
version-added: vNext
summary: Treat each parent and its preceding children as one merge unit when the parent will not be retained.
areas: [indexing, search, merging]
---

# ADR037: Block joins preserve parent boundaries across deletion and merge

- **Date:** 2026-09-26
- **Status:** Accepted

## Context

Block indexing writes each parent's children contiguously before the parent and
stores parent document IDs in the segment's `.pbs` bitset. Block-join search
maps matching child documents to the next parent marker. A document-level merge
can remove a hard-deleted parent while retaining its live children. Without the
parent marker, those children can then map to the next block's parent. Search
also reads postings that remain physically present for deleted children and
does not check that a mapped parent is live.

## Decision

Block-join search considers only live children and returns only live parents.
This rule applies to the term fast path, Boolean child-query path and general
child-query path.

During merge, each parent and the documents since the previous parent marker
form one block. If the parent is live or is a soft-deleted document retained by
the configured retention window, preserve its structural parent marker. The
retained soft-deleted parent remains hidden from search. If a hard-deleted
parent or an expired soft-deleted parent will be omitted, omit its entire block,
including otherwise-live children. Documents outside a marked block continue
to follow normal document-level merge retention.

The existing `.pbs` representation remains unchanged; no structural tombstone
or additional persisted format is introduced.

## Rationale

Dropping the whole block when its parent is omitted preserves the meaning of the
existing next-parent mapping without retaining hard-deleted document payloads or
introducing another kind of parent marker. Keeping a soft-deleted parent while
it remains inside the established retention window preserves the structural
boundary already required by soft-delete recovery. Filtering both children and
parents at search time keeps logical visibility consistent with `SegmentReader`
while leaving the postings and parent bitset formats intact.

## Consequences

- A deleted child cannot produce a parent result through stale postings.
- A deleted parent cannot be returned as a block-join result.
- Merges cannot attach children from an omitted block to a later parent.
- Soft-deleted retained parents preserve boundaries but remain invisible to
  search results.
- Merges may discard live children when their parent is no longer retained.
- The `.pbs` and commit formats do not change.
