---
adr: ADR029
title: Platform-specific durability stays behind the Store boundary
date: 2026-08-21
status: Accepted
version-added: 3.0.0
summary: Isolate filesystem durability and transient-error handling without splitting index semantics.
areas: [store, reliability, performance]
---

# ADR029: Platform-specific durability stays behind the Store boundary

- **Date:** 2026-08-21
- **Status:** Accepted

## Context

LeanCorpus previously expressed file synchronisation through `FileStream` and
implemented directory synchronisation only for POSIX. Windows-specific retry
behaviour was spread across the general file facade and treated broad I/O and
access-denied failures as transient. Durable commits also enumerated and inspected
every file in the index to discover which segment files had changed.

That design mixed platform policy with shared index semantics and made the cost of
a durable commit grow with the total index file count. Broad retries could also
hide permanent permission and path errors for one second before returning them.

## Decision

The Store layer owns a small platform filesystem seam for file synchronisation,
directory synchronisation and transient-error classification. Windows uses
`CreateFileW` with read, write and delete sharing for synchronisation handles and
`FlushFileBuffers`. POSIX uses `open` and `fsync`. Native calls are source-generated
for Native AOT compatibility, and POSIX path buffers come from `ArrayPool<byte>`.

Windows retries only sharing violations, lock violations and delete-pending
failures. Permission, missing-path and other I/O errors fail immediately. Normal
`IndexOutput` handles remain exclusive under ADR010. Immutable input handles allow
read, write and delete sharing so the short-lived synchronisation handle can coexist
with mapped readers; this does not let another exclusive `IndexOutput` open while a
reader remains active.

Successful output close, copy and atomic publication update a process-wide,
versioned dirty-file tracker. Durable commit snapshots only dirty files referenced
by the commit being published, synchronises them, then clears the exact versions
it observed. A write racing an older synchronisation receives a newer version and
cannot be cleared by that operation. Already-durable atomic files are cleared
after their file and directory synchronisation completes.

Production defaults remain unchanged: durable commits are enabled and compound
files remain opt-in. A dedicated four-way benchmark covers durable and non-durable
commits with loose and compound segment files, and reports synchronisation time,
bytes, file count, retries, retry delay and physical files created.

## Rationale

This isolates only behaviour that genuinely differs by operating system. Commit
publication, memory mapping, leases, deletion policy and segment ownership remain
shared, so the platform seam cannot develop a second indexing lifecycle.

Versioned dirty tracking removes repeated directory enumeration and metadata
queries from commit without scanning file contents or materialising codec data.
It preserves the close-before-rename invariant and the mmap-backed compound-file
design. Keeping the tracker process-wide also matches the process-wide lifetime
registry used for files shared by several directory and reader instances.

## Windows evidence

The four-way benchmark was run on 21 August 2026 with Defender real-time
protection enabled on Windows Server 2025, two virtual CPUs, 8 GiB RAM and .NET
10.0.11. It indexed 1,000 synthetic documents per operation using BenchmarkDotNet
ShortRun with three measured iterations. The repository corpus was not present on
the VM, so these results compare the four configurations on one controlled host;
they are not cross-host throughput figures.

| Durable commits | Compound files | Median operation | Median sync time | Sync operations | Changed files | Files created | Final files |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| No | No | 27.54 ms | 0 ms | 0 | 0 | 18 | 17 |
| No | Yes | 72.94 ms | 0 ms | 0 | 0 | 19 | 5 |
| Yes | No | 620.27 ms | 564.89 ms | 19 | 16 | 18 | 17 |
| Yes | Yes | 308.04 ms | 198.63 ms | 7 | 4 | 19 | 5 |

No transient retry or retry sleep occurred in any measured iteration. On this
host the durable slowdown therefore came from synchronisation frequency rather
than the retry policy. Compound files reduced durable median time by about half,
but made non-durable indexing slower for this small workload, so neither
production default changes globally. Applications with durable, file-heavy
Windows workloads should benchmark opting into compound files.

## Consequences

- Durable commit work is proportional to files written since the prior commit,
  rather than every file currently belonging to the index.
- Windows synchronisation can coexist with readers and deletion leases without
  weakening exclusive output ownership.
- Permanent filesystem failures are surfaced without retry delay.
- Files written outside the Store facade must explicitly update dirty state.
- Files created by an earlier process are assumed to have crossed that process's
  commit durability boundary and are not re-synchronised on writer open.
- Filesystem counters are process-wide diagnostics and benchmark evidence, not
  per-writer correctness state.
- No codec, compound container or commit format changes are introduced, so no
  index migration is required.

## Related decisions

- ADR007 keeps background merge completion outside commit publication.
- ADR010 requires outputs to close before atomic rename.
- ADR011 defines shared reader and deletion lifetimes.
- ADR024 keeps compound members mmap-backed rather than materialised.
- ADR027 defines operation lifetimes around those mappings.
