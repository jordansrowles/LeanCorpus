---
adr: ADR010
title: IndexOutput must be disposed before File.Move on Windows
date: 2026-07-14
status: Accepted
version-added: 2.0.0
summary: Dispose IndexOutput before Windows rename or delete operations.
areas: [store, windows, reliability]
---

# ADR010: IndexOutput must be disposed before File.Move on Windows

- **Date:** 2026-07-14
- **Status:** Accepted

## Context

Nineteen migration integration tests consistently failed on Windows CI with
`LLIDX040: The process cannot access the file because it is being used by another process`.
Seven prior commits (e064ac2f through c1c4daa1, tracked in #35) chased the wrong
side of the problem: they assumed memory-mapped file handles from the reader side
weren't released fast enough before `File.Move`. None of them fixed the failures.

The actual bug was that all seven DocValues/Norms/FieldLengths rewrite methods
called `FileOpenRetry.Move(temporaryPath, targetPath, overwrite: true)` while
the `using var output = new IndexOutput(temporaryPath, ...)` was still in scope.
`IndexOutput` opens its `FileStream` with `FileShare.None`: an exclusive lock.

On Linux, `rename(2)` on the same filesystem succeeds even when the source file
is held open by the calling process. On Windows, `MoveFileEx` fails with
`ERROR_SHARING_VIOLATION` because the process still holds the handle.

The `FileOpenRetry` retry loop (25 x 50ms, later 50 x 100ms) kept retrying a
`File.Move` that could never succeed because the lock was permanent, not
transient. Debug logging confirmed every Move failure was on the temp file path
just written by `IndexOutput`.

## Decision

All rewrite methods must close `IndexOutput` before calling `File.Move`. The
`using var output` block is brace-scoped so the `FileStream` is disposed and
the file handle released before the rename:

```csharp
try
{
    {
        using var output = new IndexOutput(temporaryPath, durable: true);
        using var scope = CodecFileHeader.BeginStreamingWrite(output, version);
        // write body
    }  // output and scope disposed here, file handle released
    FileOpenRetry.Move(temporaryPath, targetPath, overwrite: true);
}
catch { TryDeleteTemporaryFile(temporaryPath); throw; }
```

Additionally, all seven `EnumerateFields` methods in the DocValues readers
were refactored from `IEnumerable<T>` with `yield return` to returning
`List<T>` directly. A `yield return` iterator holding a `using var IndexInput`
is fragile: any caller that breaks early (`.First()`, `.Take()`, `foreach` with
`break`) leaks the MMF until GC. Returning a `List` eliminates this class of
leak entirely.

The `FileOpenRetry` retry ceiling was raised from 25 x 50ms (1.25s) to
50 x 100ms (5s) as defence against genuine transient locks from Defender AV
scanning on CI runners. On Linux, `TransientMaxRetries` remains zero so these
paths are direct calls with no overhead.

## Rationale

- The scope-based disposal is deterministic and zero-cost: `{ }` introduces no
  IL, just a lexical scope that the C# compiler already honours for `using`
  disposal ordering.
- The `List<T>` return avoids a GC-dependent cleanup path for MMF handles.
  MMF disposal in a finalizer is unreliable and non-deterministic.
- The increased retry ceiling is a defensive measure, not a fix. The primary
  fix makes retries unnecessary for this bug class. The ceiling is kept high
  because Windows CI runners have genuine transient locking from Defender.

## Consequences

- Seven rewrite methods in `IndexCodecMigrator.cs` use brace-scoped using
  blocks: RewriteNumericDocValues, RewriteSortedDocValues, RewriteNorms,
  RewriteSortedSetDocValues, RewriteSortedNumericDocValues,
  RewriteBinaryDocValues, RewriteFieldLengths.
- `RewritePostings` and `RewriteTermDictionary` were already correct.
- Seven `EnumerateFields` methods in `NormsReader`, `SortedDocValuesReader`,
  `BinaryDocValuesReader`, `FieldLengthReader`, `SortedSetDocValuesReader`,
  `SortedNumericDocValuesReader`, and `NumericDocValuesReader` return
  `List<T>` instead of `IEnumerable<T>`.
- `.ToList()` call sites in `IndexCodecMigrator` removed (now redundant).
- `FileOpenRetry`: MaxRetries 25->50, RetryDelayMs 50->100, TransientMaxRetries
  still 0 on Linux.
- Any future migration rewrite method that writes a temp file and renames it
  must follow the brace-scoped using pattern. The `FileOpenRetry` retry loop
  cannot compensate for a handle held open by the calling process.
- Migration integration tests: 40 passed, 0 failed on both net10.0 and net11.0.
