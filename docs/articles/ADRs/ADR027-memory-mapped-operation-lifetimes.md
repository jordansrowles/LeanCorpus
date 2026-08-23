---
adr: ADR027
title: Memory mappings drain active operations before reclamation
date: 2026-08-21
status: Accepted
version-added: 3.0.0
summary: Drain mmap-backed operations before releasing native views.
areas: [search, store, concurrency]
---

# ADR027: Memory mappings drain active operations before reclamation

- **Date:** 2026-08-21
- **Status:** Accepted

## Context

ADR011 introduced cache and file-snapshot leases for lazy segment readers. Those
leases prevent eviction and deletion while a reader needs a segment, but the
lifetime model stopped above the memory-mapped view itself. `IndexInput` checked
its disposed flag before a raw-pointer read while `Dispose()` could independently
release that pointer and unmap the view. A concurrent read could therefore become
an unmanaged access violation.

Permanently resident segment state had a related gap. It could be returned without
an active cache lease, allowing direct `IndexSearcher.Dispose()` to reclaim its
mapped inputs while a query or retained postings cursor still used them.

## Decision

LeanCorpus uses an internal operation drain built on its existing `LifetimeLease`
model. A drain rejects new operations after disposal begins and synchronous
disposal waits for active operations before reclaiming resources.

`MMapDirectory` drains segment and input operations before disposing tracked
inputs. Each `IndexInput` also drains direct operations before releasing its
acquired pointer, mapped view and file-lifetime callback. Input registration and
directory disposal are ordered by the same directory operation lifetime.

Resident segment state retains a detached cache lease until its reader has drained.
Unpinned facade calls acquire a reader operation lease, while nested calls reuse the
top-level query pin. Returned `PostingsEnum` instances retain segment-state and
input-mapping leases until their shared disposal guard drains. This lets searcher
retirement defer an idle cursor's state reclamation without treating the cursor's
whole lifetime as an executing reader operation.

Public `IndexInput.ReadSpan` methods return stable copied data. Internal codecs use
explicit borrowed spans only while a containing input, segment or query lifetime is
active, preserving zero-copy hot paths without exposing an unowned mapped span.
Primitive public reads retain their per-call drain. Internal decoding loops acquire
one scoped read session for a complete postings block or term-vector document and
perform their primitive reads under that containing operation.

## Rationale

This extends LeanCorpus's existing bounded-cache and cursor-lease design instead of
adopting Lucene.NET's reclamation architecture. It keeps the normal search path on
direct pointers, provides deterministic disposal, and makes ownership visible at
the same segment and cursor boundaries already used by the engine.

Acquiring and releasing the safe memory-mapped handle for every primitive read was
rejected because it would move native lifetime overhead into the hottest codec
loops. Finaliser-only reclamation was rejected because it would make file release
nondeterministic. A global reader/writer lock was rejected because unrelated mapped
files should not serialise their reads.

## Consequences

- Disposal may block until active operations or retained cursors finish.
- Active reads cannot observe an unmapped pointer.
- Resident state remains cache-pinned until its reader drains.
- Public `ReadSpan` calls allocate; internal borrowed spans remain zero-copy.
- Hot internal decoders amortise drain synchronisation over a bounded decoding
  operation rather than acquiring it for every primitive value.
- No index format or package dependency changes are required.
- ADR011 and ADR024 remain valid, with this decision defining their mmap boundary.
