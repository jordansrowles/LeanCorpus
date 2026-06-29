# Reliable commits

Commit files are written atomically and include a CRC32 trailer. Recovery strips and validates the trailer before loading a commit. If the newest commit was torn or corrupted, LeanCorpus falls back to an older generation.

`IndexWriter` runs recovery when it opens. `SearcherManager` also recovers during polling but keeps orphaned files so readers stay safe.

## Durable commits

On by default. Turn off for benchmarks where losing the latest commit is fine.

```csharp
var config = new IndexWriterConfig { DurableCommits = true };
```

## Manual recovery

```csharp
using Rowles.LeanCorpus.Index;

var commit = IndexRecovery.RecoverLatestCommit("./index", cleanupOrphans: true);
if (commit is null)
    Console.WriteLine("No valid commit; index is empty or unrecoverable.");
```

## See also

- [Validation and recovery](../tutorials/index-management/03-validation-recovery.md)
