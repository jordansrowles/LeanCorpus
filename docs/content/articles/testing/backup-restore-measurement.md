# Backup and restore state-machine measurements

Run the fixed-seed filesystem investigation through DevOps:

```bash
./devops test backup-restore-matrix -Framework net10.0 -NoRestore
./devops test backup-restore-matrix -Framework net11.0 -NoRestore
```

Each command runs three independent test processes per configuration: the
current loose/durable source, compound/durable source, loose/non-durable source,
compound/non-durable source and loose/durable source in an alternate temporary
location. Backup and restore remain durable in every configuration. The model,
30-operation trace limit, size range and correctness assertions are unchanged.
The normal workload uses `ChaosConfig.FsCheckIterations`, defaulting to 100;
`CHAOS_ITERATIONS` or an explicit `-Iterations` changes only the requested run.
Use `-Count 1 -Iterations 1` for an instrumentation smoke, not a baseline.

`-TempRoot` selects the alternate location. The other four configurations use the
OS temporary directory. Each report records source, backup and restore paths,
volume roots and filesystem formats; a different path on the same volume does
not establish an independent-volume effect. Use disposable Windows guest state
for repeated measurements. Record the guest/hypervisor/storage topology and
Defender state alongside the reports. VM IDE timings are diagnostic evidence.

Reports and a verified source manifest are retained under
`artifacts/test/runs/<run-id>/`. An exported Windows checkout can use
`-SourceManifest <source-manifest.json>` from the matching Linux source. Every
listed file is verified before execution. The matrix checks the final source
fingerprint and the seed, gamma, effective iterations, operation count and
operation-sequence fingerprint across all cells. Compare those fields between
Windows and Linux before comparing timings. Runtime versions and assembly hashes
are recorded separately, since platform builds need not have identical binaries.

The test uses trace-correlated listeners for existing Core maintenance activities
and the opt-in `Rowles.LeanCorpus.FileSystem` source. Reports retain file/directory
syncs, stream flushes, atomic writes, physical files created, tracked output bytes,
physical backup/restore copy bytes, read-open and move/delete operations, retry delay, mapping
opens and deferred deletion. Harness timers separate source commits, backup,
validation, restore, reader reopening, searches, reader close and final cleanup.
Mapping timers cover physical file/view creation and pointer acquisition,
native resource reclamation and input-operation drain waits. Finaliser reclamation
retains the opening trace identity so a GC thread can still be attributed to the
test that opened the mapping. Cleanup records whether the temporary tree was
actually removed.

Timings are inclusive: a restore includes its copies, validation and durable
writes, and an atomic write includes flush and publication. Do not add nested
category totals or describe the difference as pure CPU time. Counts include
failed attempts; bytes include data written or transferred before a rejected
checksum. Tracked output bytes are final stream lengths, not cumulative writes,
and exclude test sentinel/corruption writes. Read opens use `file.read_open`;
`file.delete` measures deletion only. The
`directory.sync` result distinguishes supported, unsupported and skipped barriers.
Retries are measured as events and actual elapsed sleeps. A run with no retry
category recorded observed zero retries, rather than an estimated retry cost.

Use the state-machine measurements to establish whether reader close, reopening,
cleanup or retries are material. Spike compound/durability results provide
mechanism context and cannot replace this workload's evidence. Hand demonstrated
handle-lifetime evidence to issue #86. Do not remove durable backup/restore,
change publication semantics or claim a production optimisation from a partial
matrix. Process-crash recovery and machine-reset durability remain separate claims.

See [the completed diagnostic investigation](backup-restore-results.md) for the
fixed-seed matrix, measured categories, conclusion and provenance limitations.
