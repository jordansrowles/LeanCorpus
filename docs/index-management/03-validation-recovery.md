# Validation and recovery

## Validate an index

`IndexValidator.Check` checks the latest commit without modifying files:

```csharp
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;

using var dir = new MMapDirectory("./index");
IndexCheckResult result = IndexValidator.Check(dir);

if (!result.IsHealthy)
{
    foreach (var issue in result.DetailedIssues)
        Console.Error.WriteLine($"{issue.Severity} {issue.Code} {issue.SegmentId} {issue.FileName} {issue.Message}");
}

Console.WriteLine($"Commit generation: {result.CommitGeneration}");
Console.WriteLine($"Segments checked: {result.SegmentsChecked}");
Console.WriteLine($"Documents checked: {result.DocumentsChecked}");
```

## Shallow validation

The default check is catalogue-driven. It verifies the newest readable `segments_N` commit, logical loose and compound members, canonical or declared legacy framing, format identity and version support, exact body bounds, segment metadata, required files and relevant cross-file descriptors. It does not scan every large body checksum.

## Deep validation

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
| `VerifyPostings` | Reads postings, validates document IDs |
| `VerifyStoredFields` | Reads stored fields for every document |
| `VerifyDocValues` | Reads numeric, sorted, sorted-set, sorted-numeric, and binary DocValues |
| `VerifyVectors` | Opens vector files, checks count and dimensions |
| `VerifyHnsw` | Reads HNSW graph files through the vector reader source |
| `VerifyLiveDocs` | Deserialises live-doc bitsets, checks live counts |

Deep validation also streams canonical bodies through their declared checksum algorithm. It reports a structured frame or checksum issue without materialising random-access files.

## Issue fields

Each `IndexCheckIssue` has:

| Field | Meaning |
|---|---|
| `Severity` | `Info`, `Warning`, or `Error` |
| `Code` | Stable `LLIDX###` issue code |
| `Message` | Human-readable detail |
| `FileName` | Related file name |
| `SegmentId` | Related segment ID |
| `IsRepairable` | Whether future repair tooling could fix it |
| `SuggestedActions` | Repair or recovery actions to consider |

`IsHealthy` is true when no issue has `Error` severity.

## Crash recovery

```csharp
var commit = IndexRecovery.RecoverLatestCommit("./index", cleanupOrphans: true);
if (commit is null)
    Console.WriteLine("No valid commit; index is empty or unrecoverable.");
```

Finds the newest valid commit, falling back to older generations. Cleans up orphaned segment files and stale temp files. `IndexWriter` runs recovery on open; `SearcherManager` calls it with `cleanupOrphans: false`.

## Format inventory

```csharp
using Rowles.LeanCorpus.Index.Format;

var inventory = IndexFormatInspector.Inspect(dir);
foreach (var segment in inventory.Segments)
{
    Console.WriteLine(segment.SegmentId);
    foreach (var file in segment.Files)
        Console.WriteLine(
            $"  {file.FileName}: {file.FormatId}, frame={file.FrameVersion}, " +
            $"format={file.FormatVersion}, checksum={file.ChecksumStatus}, " +
            $"location={file.PhysicalLocation}");
}
```

Reports stable format and family IDs, frame kind/version, body-format version, current status, checksum algorithm/status, logical member name, physical loose/compound location, sidecars and orphan files. Future, unknown, mismatched and corrupt formats are reported in `inventory.Issues` rather than guessed.

## Compatibility and migration

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

Compatibility statuses: `Compatible`, `MigrationRecommended`, `MigrationRequired`, `UnsupportedFutureFormat`, `UnknownFormat`, `Corrupt`, `Empty`. The result also exposes `CanRead`, `CanWrite`, `CanValidate`, `CanMigrate`, `MustReject`, and `RequiresMigration`.

`IndexCodecMigrator.Migrate` copies the index to a staging directory, rewrites older codec files, deep-validates the staged index, publishes the files back, and records `migration_state.json` markers:

```csharp
var result = IndexCodecMigrator.Migrate(dir, new IndexCodecMigrationOptions
{
    DryRun = false,
    StagingDirectory = "./index.migration"
});
```

## Commit CRC

New commit files include a CRC32 trailer. Recovery validates it before loading the JSON body. A mismatch falls back to an older valid generation.

## See also

- [Index checker CLI](04-cli-checker.md)
- [Migrating indexes to 3.0](11-codec-migration-3-0.md)
- <xref:Rowles.LeanCorpus.Index.IndexValidator>
- <xref:Rowles.LeanCorpus.Index.IndexRecovery>
- <xref:Rowles.LeanCorpus.Index.Format.IndexFormatInspector>
- <xref:Rowles.LeanCorpus.Index.Compatibility.IndexCompatibility>
- <xref:Rowles.LeanCorpus.Index.Migration.IndexCodecMigrator>
