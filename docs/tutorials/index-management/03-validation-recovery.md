# Validation and recovery

## Validate an index

`IndexValidator.Check` checks the latest commit without modifying files. It
returns an `IndexCheckResult` with compatibility string messages in `Issues` and
structured issues in `DetailedIssues`.

```csharp
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;

using var dir = new MMapDirectory("./index");
IndexCheckResult result = IndexValidator.Check(dir);

if (!result.IsHealthy)
{
    foreach (var issue in result.DetailedIssues)
    {
        Console.Error.WriteLine(
            $"{issue.Severity} {issue.Code} {issue.SegmentId ?? "-"} {issue.FileName ?? "-"} {issue.Message}");
    }
}

Console.WriteLine($"Commit generation: {result.CommitGeneration}");
Console.WriteLine($"Segments checked: {result.SegmentsChecked}");
Console.WriteLine($"Documents checked: {result.DocumentsChecked}");
Console.WriteLine($"Files checked: {result.FilesChecked}");
```

`IndexValidator.Validate` remains available and forwards to `Check` with default
options.

## What shallow validation checks

The default check verifies the newest readable `segments_N` commit, segment
metadata, required segment files, optional sidecars when present, codec headers,
stored-field compression metadata, stored-field index counts, deletion
generation files, vector descriptors, and HNSW descriptors.

| Area | Files |
|---|---|
| Required segment files | `.seg`, `.dic`, `.pos`, `.fdt`, `.fdx`, `.nrm` |
| DocValues sidecars | `.dvn`, `.dvs`, `.dss`, `.dsn`, `.dvb` |
| Other sidecars | `.num`, `.bkd`, `.fln`, `.tvd`, `.tvx`, `.pbs` |
| Vector search | `.vec`, `.hnsw` |
| Live docs | `.del`, `_gen_N.del` |

## Deep validation

Deep validation opens reader paths and verifies per-document counts. Use
`Deep = true` to run every deep check, or enable a subset for cheaper targeted
diagnostics.

```csharp
var result = IndexValidator.Check(dir, new IndexCheckOptions
{
    VerifyDocValues = true,
    VerifyStoredFields = true,
    VerifyLiveDocs = true
});
```

| Option | Checks |
|---|---|
| `Deep` | Enables every deep check |
| `VerifyPostings` | Reads postings and validates document IDs |
| `VerifyStoredFields` | Reads stored fields for every document |
| `VerifyDocValues` | Reads numeric, sorted, sorted-set, sorted-numeric, and binary DocValues |
| `VerifyVectors` | Opens vector files and checks vector count and dimensions |
| `VerifyHnsw` | Reads HNSW graph files through the vector reader source |
| `VerifyLiveDocs` | Deserialises live-doc bitsets and checks live counts |

## Issue fields

Each `IndexCheckIssue` includes:

| Field | Meaning |
|---|---|
| `Severity` | `Info`, `Warning`, or `Error` |
| `Code` | Stable `LLIDX###` issue code |
| `Message` | Human-readable detail |
| `FileName` | Related file name, when file-specific |
| `SegmentId` | Related segment ID, when segment-specific |
| `IsRepairable` | Whether future repair tooling could fix the issue |
| `SuggestedActions` | Repair or recovery actions to consider |

`IsHealthy` is true when no issue has `Error` severity.

## Crash recovery

`IndexRecovery.RecoverLatestCommit` finds the newest valid commit, falling back to
older generations if the latest is corrupt. It also cleans up orphaned segment
files and stale temp files left behind by an interrupted commit.

```csharp
var commit = IndexRecovery.RecoverLatestCommit("./index", cleanupOrphans: true);
if (commit is null)
    Console.WriteLine("No valid commit; index is empty or unrecoverable.");
```

`IndexWriter` runs writer-side recovery on open. Reader-side polling
(via `SearcherManager`) calls it with `cleanupOrphans: false`.

## Format inventory

`IndexFormatInspector.Inspect` reads commit metadata and codec headers without
constructing search readers. It reports segment IDs, file names, codec names,
codec versions, current versions, DocValues sidecars, vector files, HNSW files,
live-doc generations, and orphan files.

```csharp
using Rowles.LeanCorpus.Index.Format;

var inventory = IndexFormatInspector.Inspect(dir);

foreach (var segment in inventory.Segments)
{
    Console.WriteLine(segment.SegmentId);
    foreach (var file in segment.Files)
        Console.WriteLine($"{file.FileName}: {file.CodecName} v{file.Version}");
}
```

Future codec versions are reported in `inventory.Issues` and
`HasUnsupportedFutureFormat` rather than thrown from inspection.

## Compatibility and migration

`IndexCompatibility.Check` combines inventory, validation, and migration
planning. It returns `Compatible`, `MigrationRecommended`, `MigrationRequired`,
`UnsupportedFutureFormat`, `Corrupt`, or `Empty`.
The result also exposes `CanRead`, `CanWrite`, `CanValidate`, `CanMigrate`,
`MustReject`, and `RequiresMigration` flags for automation.

```csharp
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Migration;

var compatibility = IndexCompatibility.Check(dir, new IndexCompatibilityOptions
{
    DeepValidation = true,
    AllowSupportedOlderFormats = true
});

if (compatibility.CanMigrate)
{
    var plan = IndexCodecMigrator.Plan(dir);
    foreach (var action in plan.Actions)
        Console.WriteLine(action.Description);
}
```

`IndexCodecMigrator.Migrate` defaults to staged migration. It copies the index to
a sibling staging directory, rewrites executable older codec files, deep-validates
the staged index, publishes the staged files back, and records
`migration_state.json` markers during the workflow.

```csharp
var result = IndexCodecMigrator.Migrate(dir, new IndexCodecMigrationOptions
{
    DryRun = false,
    StagingDirectory = "./index.migration"
});

if (!result.Succeeded)
{
    foreach (var issue in result.Issues)
        Console.Error.WriteLine(issue.Message);
}
```

Use `IndexMigrationRecovery.RollBack("./index")` to delete marker and staging
files for an interrupted migration. Use `Abandon("./index")` only when you have
inspected the state and want to remove the marker without deleting staging data.

## Commit CRC

New commit files include a CRC32 trailer. Recovery validates it before loading the
JSON body. A mismatch is treated as a torn or corrupt commit, so recovery falls
back to an older valid generation.

## See also

- [Index checker CLI](04-cli-checker.md)
- <xref:Rowles.LeanCorpus.Index.Format.IndexFormatInspector>
- <xref:Rowles.LeanCorpus.Index.Compatibility.IndexCompatibility>
- <xref:Rowles.LeanCorpus.Index.Migration.IndexCodecMigrator>
- <xref:Rowles.LeanCorpus.Index.Migration.IndexMigrationRecovery>
- <xref:Rowles.LeanCorpus.Index.IndexValidator>
- <xref:Rowles.LeanCorpus.Index.IndexRecovery>
- <xref:Rowles.LeanCorpus.Index.IndexCheckResult>
- <xref:Rowles.LeanCorpus.Index.IndexCheckIssue>
- <xref:Rowles.LeanCorpus.Index.IndexCheckOptions>
