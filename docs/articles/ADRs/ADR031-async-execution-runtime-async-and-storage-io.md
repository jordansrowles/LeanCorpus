---
adr: ADR031
title: "Async execution, Runtime Async and storage I/O"
date: 2026-09-16
status: Accepted
version-added: vNext
summary: "Keep Runtime Async off by default and reject a general async segment-storage rewrite."
areas: [indexing, storage, performance]
---

# ADR031: Async execution, Runtime Async and storage I/O

- **Date:** 2026-09-16
- **Status:** Accepted

## Context

LEAN-9 established bounded DWPT producers, writer-owned detached flush batches,
bounded `FlushCoordinator` execution, ordered publication, background merges and
separate search parallelism. LEAN-16 investigated the separate question of
genuine asynchronous execution.

Asynchronous admission, asynchronous waiting, background synchronous execution,
CPU parallelism and true asynchronous I/O are different properties. LeanCorpus
already uses asynchronous admission and waiting where callers need backpressure
without blocking. They do not imply asynchronous codecs, scoring or durable
storage.

## Decision

Runtime Async remains off by default. Repeated .NET 11 Linux real-corpus
OFF/ON comparisons did not show a consistent material benefit across the async
workload set: individual operations improved, while others were flat or
regressed. The evidence does not establish that Runtime Async is universally
slower or unsuitable; it does not justify making it the LeanCorpus default.

General async segment writing is rejected. Storage profiling found the material
waiting concentrated at durability barriers such as filesystem synchronisation,
not a broad asynchronous-friendly segment-write phase. `Task.Run` around a
synchronous durability barrier is not true asynchronous storage.

LEAN-9 remains authoritative. This decision does not change DWPT or
`DwptFlushBatch` ownership, `FlushCoordinator`, ordered publication,
accepted-write ownership, durability semantics, search execution, codecs or
on-disk formats. In particular, `IndexOutput`, `SegmentFlusher`, codec and
postings construction, stored fields, search, HNSW and FST construction do not
become async for API symmetry.

## Rationale

The tested workloads did not provide a stable throughput or latency case for a
default runtime feature change. A general async storage API would broaden the
codec and storage contract while leaving the dominant durability wait
synchronous on supported platforms.

Async remains appropriate at boundaries that genuinely release workers while
waiting, including network and server I/O, external connectors, streaming
ingestion, remote backup or replication, and other external services.

The full Windows Runtime Async and NativeAOT matrix was intentionally not
completed. Linux supplied the stronger repeated signal, and additional Windows
spike work was unlikely to change the architectural decision enough to justify
its cost. This does not reduce the normal LeanCorpus Windows CI and product
validation commitment.

## Consequences

Keep asynchronous APIs as admission, streaming and fairness boundaries around
synchronous engine execution unless a component has a genuine asynchronous
completion primitive. Do not add a general async storage abstraction without
new evidence.

Reconsider this decision only when one or more of these conditions changes:

- A future .NET Runtime Async implementation materially changes the measured
  allocation or continuation costs.
- New NativeAOT or runtime behaviour materially changes those costs.
- A genuine cross-platform asynchronous durable-file primitive becomes
  available.
- `io_uring` or an equivalent runtime integration materially changes Linux
  file-I/O behaviour.
- A LeanCorpus workload demonstrates meaningful worker starvation from storage
  waits.
- Server or cluster workloads demonstrate meaningful mixed search/index
  tail-latency gains from genuine asynchronous I/O.

## References

- YouTrack LEAN-16.
- LEAN-9.
- Archival branch: `lean-16`.
- LEAN-9 baseline: `bc727ab9acbb1d50a3fd179e300e2eb10ca822ee`.
- LEAN-16 final state: `e22a5203d40616b4637451b8f603f88dcdfe3f73`.
