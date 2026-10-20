# Physical index lifetimes

Community Server and the Orchard provider use one internal Core runtime to own a
physical index's directory, optional writer and searcher manager. The runtime is
an implementation boundary, rather than a public embedded-engine API. Direct
Core callers still own the engine objects they construct.

## Lease and shutdown contract

A reader-manager lease retains its immutable reader generation. Disposing the
manager retires its generations and stops refresh scheduling; existing leases
remain valid until released. Dispose every lease, including leases over older
generations, before expecting the underlying files to be released. Copies of a
reader lease share one release guard.

The composed runtime additionally tracks complete admitted operations and its
issued searcher leases. Close first rejects new work, drains admitted operations,
stops refreshes, drains search leases, closes writer work, disposes the
directory and settles unleased deferred deletions. One worker performs synchronous
engine teardown while callers await the shared completion; this does not make
filesystem durability barriers asynchronous I/O. Concurrent close callers join one completion. Cancelling a caller's
wait leaves admission closed and cleanup running.

An admitted operation carries an owner-bound lease through all of its physical
stages. A commit or refresh inside that operation uses the existing lease even
if close has begun; independent work cannot borrow it or enter after quiescing.
Top-level commit and refresh calls acquire their own lease. Server commit
receipts are published under the same writer lock as the physical commit, and
waiters observe a monotonic durable sequence. Observer callbacks run outside the
writer and publication locks. Their invocation and completion order is not a
durability guarantee; consumers use the receipt sequence, and observer failures
are reported separately from physical commit failures.

Successful terminal close releases that runtime's physical ownership. Immediate
writer reopen, directory rename and recursive deletion are then possible,
subject to independent external owners and filesystem permissions. A close
failure remains a failed close, with the original failure available in the
snapshot; it never transitions to `Closed`. A writer that has not finished
shutting down retains its directory.

## Failure and visibility

| Condition | Runtime behaviour |
| --- | --- |
| Rejected request before engine mutation | Does not by itself poison the writer |
| Commit fails before publication | Reports degraded state; a successful retry can recover |
| Writer reports fatal indexing failure | Rejects new write operations; requires cleanup and a new runtime |
| Refresh fails | Retains the previous readable generation and reports degraded state |
| Close fails | Reports `Faulted`, retaining the close failure and ownership state |

A fatal writer can still have a sound older searcher. Read availability does not
mean that further writes or refresh are safe. Health derives from the writer's
failure state and the searcher manager's refresh result. Commit durability and
search visibility are separate milestones; each host retains its own policy.

## Host ownership

Server keeps logical registration, schema, sequencing, durability receipts and
readiness above the physical runtime. A commit snapshot pins its source runtime
until the snapshot lease and its opened file streams or active backup operations
are released. Disposing the parent snapshot rejects new file opens while
existing streams retain the physical pin until their own disposal. Mode transitions and snapshot installation
therefore wait for retained snapshots before replacing the physical owner.

Orchard shares physical runtimes across overlapping tenant shell generations.
Normal final-cache release closes a tenant's runtime. Application shutdown also
invalidates idle historical owners, rejects queued work and waits for close,
even when an old released shell scope remains retained. Tenant paths, task
cursors and the required refresh after indexing remain Orchard policy.

Orchard sharing is scoped to one root-host lifetime identity and canonical
tenant path. Shells from that root may overlap during replacement. A second
active root host in the same process cannot attach to the same writable path;
it receives an ownership error. A new root can open the path after the previous
owner has closed successfully. Core reclaims an idle directory registry state
after the last `MMapDirectory` owner, file/snapshot lease, deferred delete and
queued finaliser-delete callback has settled. A failed deferred delete keeps
its state available for recovery.

## Diagnostics

Internal snapshots include the physical runtime ID, lifecycle and health state,
admitted operation and search-lease counts, writer/directory ownership, allowed
operations, last failure and close timing. Resource snapshots also expose current and retired
generations, writer-lock ownership, active input/view and merge counts, and
pending deferred deletion. File-lease and pending-delete counts are directory-wide
and can include independent embedded readers; input/view counts belong to the
runtime directory. Unleased pending deletions must settle before terminal close
succeeds. Orchard adds cache, shell and shared
state IDs, root-host, shell and cache correlation, current owner IDs, admission
and close state. Server coordinator snapshots include the latest durable
sequence and visibility, waiter and active-attempt counts, and separate physical
and observer failures. File-lifetime diagnostics expose registered state,
owner, lease and pending-delete counts without exporting paths. Activities use
the existing `Rowles.LeanCorpus` ActivitySource and correlate close to these
owners.

See [ADR040](ADRs/ADR040-physical-index-runtime-lifecycle.md) for the ownership
decision and [the Orchard provider guide](../server/orchard-core-search-provider.md)
for tenant deployment and shutdown behaviour.
