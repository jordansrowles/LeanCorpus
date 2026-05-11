# Reliable commits

Commit files are written atomically and include a CRC32 trailer. Recovery strips
and validates the trailer before loading a commit. If the newest commit was torn
or corrupted, LeanCorpus falls back to an older valid generation.

`IndexWriter` runs recovery when it opens. `SearcherManager` also uses recovery
while polling, but without deleting orphaned files so readers stay safe.

Durable commits are on by default. Turn them off only for benchmarks where losing
the latest commit is acceptable.

See [Validation and recovery](../tutorials/index-management/03-validation-recovery.md).
