# Index checker CLI

`Rowles.LeanCorpus.Cli` builds `leancorpus-cli.exe`, a `System.CommandLine`
front end for index validation, format inspection, compatibility checks,
codec migration, snapshot backup, and restore.

## Build the CLI

```powershell
dotnet build .\src\devops\Rowles.LeanCorpus.Cli\Rowles.LeanCorpus.Cli.csproj -c Release
```

The executable is written under the target framework output directory:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe
```

## Commands

| Command | Behaviour |
|---|---|
| `check <index-path>` | Validates the latest commit and optional deep structures |
| `inspect <index-path>` | Reports commit, segment, codec, sidecar, vector, HNSW, live-doc, and orphan-file inventory |
| `compat <index-path>` | Reports whether the index can be read, written, migrated, or must be rejected |
| `migrate <index-path>` | Produces a dry-run migration plan or runs staged codec migration |
| `backup <index-path> <backup-path>` | Copies the files required to restore one commit point and writes a backup manifest |
| `restore <backup-path> <target-path>` | Validates a backup manifest and restores files into a target index directory |

## Check an index

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\index --deep
```

```text
Healthy: checked 2 segment(s), 200 document(s), 46 file(s).
```

Unhealthy output includes one line per issue:

```text
Unhealthy: checked 1 segment(s), 10 document(s), 8 file(s).
Error LLIDX006 seg_0 seg_0.dic Segment 'seg_0' is missing required file 'seg_0.dic'.
  Suggested action: Restore the missing or empty segment file from backup, or rebuild the affected segment from source documents.
```

The issue columns are severity, stable issue code, segment ID, file name, and
message, followed by suggested repair actions where available.

```text
leancorpus-cli.exe check <index-path> [--deep] [--json] [--postings] [--stored-fields] [--doc-values] [--vectors] [--hnsw] [--live-docs] [--summary-only] [--fail-on-warnings] [--output <path>]
```

| Option | Behaviour |
|---|---|
| `--deep` | Runs every deep validation check |
| `--json` | Writes JSON instead of text |
| `--postings` | Deep-checks postings |
| `--stored-fields` | Deep-checks stored fields |
| `--doc-values` | Deep-checks numeric, sorted, sorted-set, sorted-numeric, and binary DocValues |
| `--vectors` | Deep-checks vector files |
| `--hnsw` | Deep-checks HNSW graph files |
| `--live-docs` | Deep-checks live-doc bitsets |
| `--summary-only` | Writes only the healthy or unhealthy summary |
| `--fail-on-warnings` | Returns exit code `1` for warning-severity issues as well as errors |
| `--output <path>` | Writes the selected text or JSON report to a file |

## Inspect an index

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe inspect .\index --json --output .\inventory.json
```

`inspect` reports file inventory without constructing search readers. Use it to
see current and older codec versions, optional sidecars, vector and HNSW files,
deletion generations, missing files, and orphan files.

```text
leancorpus-cli.exe inspect <index-path> [--json] [--output <path>]
```

## Check compatibility

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe compat .\index --deep
```

Compatibility statuses are:

| Status | Meaning |
|---|---|
| `Empty` | No commit file exists |
| `Compatible` | The index can be read and written by this build |
| `MigrationRecommended` | Readers can open it, but a current-format rewrite is available |
| `MigrationRequired` | The requested policy requires migration before open |
| `UnsupportedFutureFormat` | At least one codec version is newer than this build |
| `Corrupt` | Validation found error-severity issues |

```text
leancorpus-cli.exe compat <index-path> [--deep] [--json] [--output <path>]
```

## Plan or run migration

Dry-run mode is the default safe workflow for automation:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe migrate .\index --dry-run --json
```

Run staged migration with an explicit staging directory:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe migrate .\index --execute --staging .\index.migration
```

```text
leancorpus-cli.exe migrate <index-path> [--dry-run] [--execute] [--staging <path>] [--in-place] [--json] [--output <path>]
```

| Option | Behaviour |
|---|---|
| `--dry-run` | Reports every planned rewrite without modifying files |
| `--execute` | Runs the migration. Without this option, dry-run mode is used |
| `--staging <path>` | Uses an explicit staging directory |
| `--in-place` | Allows source-directory migration instead of staged migration |
| `--json` | Writes JSON instead of text |
| `--output <path>` | Writes the selected text or JSON report to a file |

Staged migration writes `migration_state.json` while it works. Normal reader and
writer opens reject an incomplete marker. Use the core
`IndexMigrationRecovery.RollBack` API to remove the staging directory and marker
after an interrupted migration.

## Back up and restore

Back up the latest commit point:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe backup .\index .\index.backup --json
```

Back up a specific commit generation:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe backup .\index .\index.backup --commit-generation 3 --overwrite
```

Restore into a new index directory:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe restore .\index.backup .\index.restored
```

```text
leancorpus-cli.exe backup <index-path> <backup-path> [--commit-generation <generation>] [--overwrite] [--json] [--output <path>]
leancorpus-cli.exe restore <backup-path> <target-path> [--overwrite] [--skip-validation] [--json] [--output <path>]
```

`backup` writes `leancorpus-backup-manifest.json` with the commit generation,
file names, lengths, CRC-32 checksums, and file roles. `restore` validates the
manifest before copying and validates the restored index unless
`--skip-validation` is supplied.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | The command succeeded |
| `1` | Validation, compatibility, migration, or restore reported an error state |
| `2` | Arguments were invalid, the path did not exist, or the CLI could not run the command |

## JSON output

Use `--json` for automation:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\index --json --doc-values
```

The check JSON shape includes stable issue fields:

```json
{
  "isHealthy": false,
  "commitGeneration": 3,
  "segmentsChecked": 1,
  "documentsChecked": 10,
  "filesChecked": 8,
  "issues": [
    {
      "severity": "Error",
      "code": "LLIDX006",
      "message": "Segment 'seg_0' is missing required file 'seg_0.dic'.",
      "fileName": "seg_0.dic",
      "segmentId": "seg_0",
      "isRepairable": true,
      "suggestedActions": [
        "Restore the missing or empty segment file from backup, or rebuild the affected segment from source documents."
      ]
    }
  ]
}
```

## Create a sample index

`Rowles.LeanCorpus.Example.NewsgroupsIndexer` reads the shared `bench\data\20newsgroups` corpus and
creates a checker-ready index with postings, stored fields, DocValues, vectors,
HNSW, term vectors, and stored-field compression metadata.

```powershell
dotnet run --project .\src\examples\Rowles.LeanCorpus.Example.NewsgroupsIndexer -- --index .\artifacts\newsgroups-index --limit 500
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\artifacts\newsgroups-index --deep
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe inspect .\artifacts\newsgroups-index --json
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe compat .\artifacts\newsgroups-index
```

The example options are:

| Option | Behaviour |
|---|---|
| `--source <path>` | Use another 20 Newsgroups root instead of the shared `bench\data\20newsgroups` corpus |
| `--index <path>` | Output index path. Defaults to `artifacts\newsgroups-index` |
| `--limit <count>` | Maximum documents to index. Defaults to `500` |
| `--append` | Keep existing index files instead of recreating the output directory |

## See also

- [Validation and recovery](03-validation-recovery.md)
- <xref:Rowles.LeanCorpus.Index.IndexValidator>
- <xref:Rowles.LeanCorpus.Index.Format.IndexFormatInspector>
- <xref:Rowles.LeanCorpus.Index.Compatibility.IndexCompatibility>
- <xref:Rowles.LeanCorpus.Index.Migration.IndexCodecMigrator>
