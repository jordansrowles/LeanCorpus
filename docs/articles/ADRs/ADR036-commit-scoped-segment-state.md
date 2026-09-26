---
adr: ADR036
title: Commit-scoped state defines segment liveness at publication
date: 2026-09-26
status: Accepted
version-added: vNext
summary: Persist deletion generation and other mutable liveness state in each commit so commit publication is the only visibility boundary.
areas: [indexing, storage, recovery, snapshots, backup]
---

# ADR036: Commit-scoped state defines segment liveness at publication

- **Date:** 2026-09-26
- **Status:** Accepted

## Context

`segments_N` identifies a set of segment IDs, while `seg_N.seg` currently also
stores deletion-generation state and live-document counts. Applying pending
deletes writes a new `.del` file and mutates `seg_N.seg` before the next commit
marker is published. An already-published commit can therefore observe deletion
state from a later, unpublished operation. Retained commits and backups cannot
identify the exact deletion generation they originally published.

## Decision

Persist commit-mutable segment liveness in each commit record. Each segment
state is keyed by its segment ID and records its selected deletion generation,
live-document count, and earliest soft-delete timestamp. Immutable codec and
segment-structure metadata remains in `seg_N.seg`.

Applying pending deletes may write the generation-specific `.del` sidecar and
update the writer's private in-memory segment state, but it must not rewrite
`seg_N.seg` for an existing committed segment. The next `segments_N` file
publishes the new liveness state atomically. A reader, recovery operation,
snapshot, or backup resolves the exact segment state from the selected commit.

Older commit records without per-segment state remain readable by using the
legacy liveness fields in `seg_N.seg`. New commit records always include state
for every referenced segment. Validation rejects duplicate, missing, unknown,
or out-of-range state entries rather than guessing.

## Rationale

The commit marker is already the publication boundary for segment membership
and logical content identity. Keeping deletion pointers in that same immutable
record makes deletion visibility follow the same rule. It also makes retained
commit fallback and generation-specific backup deterministic without changing
the deletion-file format or requiring immutable copies of every segment's
codec metadata.

An immutable generation-scoped descriptor would also preserve point-in-time
identity, but it adds another persistent artifact and cleanup/lifetime rules.
The commit record already names each segment, so adding its mutable liveness
state there is the narrower ownership boundary.

## Consequences

- Publishing a new `segments_N` file is the sole visibility boundary for hard
  and soft deletions on existing segments.
- Retained commits, reader snapshots, recovery fallback, and backups can select
  their own deletion generation and live counts.
- The JSON commit schema gains an optional state collection for backward
  compatibility; the CRC-wrapped commit file format remains unchanged.
- Backup manifests and restores must preserve the selected commit's segment
  state, including when incremental chains reuse unchanged codec files.
- Generation-specific deletion sidecars remain governed by the existing
  segment-file lifecycle and snapshot-protection rules.
