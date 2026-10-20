---
adr: ADR040
title: Physical indexes have one composed runtime owner
date: 2026-10-17
status: Accepted
version-added: 4.0.0
summary: Compose engine resources under one policy-neutral owner with a terminal close contract.
areas: [indexing, search, store, concurrency, server]
---

# ADR040: Physical indexes have one composed runtime owner

- **Date:** 2026-10-17
- **Status:** Accepted

## Context

Community Server and the Orchard provider independently compose directories,
writers and searcher generations. Orchard replacement shells can overlap and
share a physical writer. Disposing the current shell's cache does not close that
writer while a historical shell still retains a cache reference.

A functional regression holding an actual Orchard shell scope across reload
demonstrated that host stop and disposal could return before exclusive access
to `write.lock` was available. The replacement shell could query successfully;
the failure occurred at the immediate post-stop lock probe.

Reader-manager disposal has a deliberately weaker meaning than physical index
close: acquired leases keep retired readers alive. A composed owner must account
for those leases before disposing their writer and directory.

## Decision

One `PhysicalIndexRuntime` owns each first-party physical index's directory,
optional writer, searcher manager and lifecycle coordination. Keep this component
in the existing Core assembly. Introduce it internally while its first-party
consumers and failure contract are qualified; do not create a package solely to
name this boundary.

The runtime admits complete physical operations and issues disposable searcher
leases. Closing admission rejects new work. Terminal close drains admitted work,
stops refreshes, drains current and retired searcher leases, closes the writer
including its merge and asynchronous work, and finally disposes the directory.
Successful close means the runtime's physical ownership has ended and immediate
writer reopen, directory rename and deletion are possible on supported local
filesystems, subject to independent external owners.

Concurrent close callers join the same completion. Cancelling a caller's wait
does not reopen admission or abandon cleanup. Failure remains observable and
must never be reported as `Closed`. A writer whose shutdown has not completed
retains its directory; cleanup cannot reclaim it underneath active writer work.
Normal work cannot resume on an owner that has begun closing.

Core retains ownership of writer locking, commit publication, merges, mappings
and file lifetimes. Runtime health must use definitive engine failure signals,
rather than host guesses based on exception types. A failed refresh may retain
an older readable generation; it must not be represented as successful current
visibility. A fatal writer or directory failure requires a new runtime after
terminal cleanup and any required durable recovery.

Server retains logical registration, sequencing, durability acknowledgements,
visibility policy, readiness, scheduling, transport and distributed concerns.
Its admitted snapshot operation retains the physical owner until both the
snapshot and all child read streams or backup operations have ended; disposing
a parent snapshot closes admission rather than invalidating an active child.
Orchard retains tenant paths, schema integration, visibility policy and shell
handoff. Its handoff layer shares one physical runtime across shell generations
and must bind terminal physical shutdown to the application lifetime, including
historical shells. An idle old shell reference cannot silently retain a writer
past terminal host shutdown.

## Rationale

This reuses `SearcherManager`, `ReaderManager` and the Store lifetime machinery
defined by ADR011 and ADR027. It removes duplicated physical ownership without
adding ASP.NET Core, Orchard, acknowledgement or distributed dependencies to
Core. ADR007 still permits background merges independently of commit; terminal
writer shutdown remains responsible for their completion. ADR029 and ADR032
continue to govern storage durability and asynchronous waiting.

The runtime has no process-wide path registry. Explicit host sharing accounts
for logical owners, and the existing writer lock remains the exclusion backstop
for independent owners and processes. Low-level embedded callers can continue
to compose engine primitives directly and own their complete lifetimes.

## Consequences

- First-party hosts adopt the common owner instead of constructing raw writers
  and managing searcher generations independently.
- Shell release, host quiescence, manager disposal and terminal physical close
  have distinct meanings and distinct evidence.
- Lifecycle snapshots and the existing ActivitySource identify remaining
  operations, leases, physical ownership and close failure.
- Tests must cover retained generations, whole-operation admission, failed open
  and close, independent processes, shell reload and Server restart on the
  supported platform and framework matrix.
- No persisted index format, analysis source-inclusion or new dependency is
  required by this boundary. Public runtime API stabilisation is a separate
  compatibility decision after first-party qualification.
