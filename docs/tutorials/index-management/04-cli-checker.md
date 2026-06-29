# Index checker CLI

`leancorpus-cli.exe` is a command-line front end for validation, inspection, compatibility checks, migration, backup, and restore.

## Build

```powershell
dotnet build .\src\devops\Rowles.LeanCorpus.Cli\Rowles.LeanCorpus.Cli.csproj -c Release
```

The binary lands under the target framework output directory:

```powershell
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe
```

## Commands

| Command | What it does |
|---|---|
| `check <path>` | Validates the latest commit and optional deep structures |
| `inspect <path>` | Reports commit, segment, codec, sidecar, vector, HNSW, live-doc, and orphan-file inventory |
| `compat <path>` | Reports whether the index can be read, written, migrated, or must be rejected |
| `migrate <path>` | Produces a dry-run migration plan or runs staged codec migration |
| `backup <path> <backup-path>` | Copies files for one commit point and writes a backup manifest |
| `restore <backup-path> <target-path>` | Validates a manifest and restores files into a target index directory |

## Check

```powershell
leancorpus-cli.exe check .\index --deep
```

```text
Healthy: checked 2 segment(s), 200 document(s), 46 file(s).
```

Unhealthy output includes one line per issue with severity, code, segment ID, file name, message, and suggested actions.

```
leancorpus-cli.exe check <path> [--deep] [--json] [--postings] [--stored-fields]
    [--doc-values] [--vectors] [--hnsw] [--live-docs] [--summary-only]
    [--fail-on-warnings] [--output <path>]
```

| Option | Effect |
|---|---|
| `--deep` | Every deep validation check |
| `--json` | JSON output |
| `--postings` | Deep-check postings |
| `--stored-fields` | Deep-check stored fields |
| `--doc-values` | Deep-check DocValues |
| `--vectors` | Deep-check vector files |
| `--hnsw` | Deep-check HNSW graph files |
| `--live-docs` | Deep-check live-doc bitsets |
| `--summary-only` | Only the healthy/unhealthy summary line |
| `--fail-on-warnings` | Exit code 1 for warnings too |
| `--output <path>` | Write report to file |

## Inspect

```powershell
leancorpus-cli.exe inspect .\index --json --output .\inventory.json
```

Reports file inventory without constructing search readers.

## Compatibility

```powershell
leancorpus-cli.exe compat .\index --deep
```

| Status | Meaning |
|---|---|
| `Empty` | No commit file |
| `Compatible` | Can be read and written by this build |
| `MigrationRecommended` | Readable, but a current-format rewrite is available |
| `MigrationRequired` | Policy requires migration before open |
| `UnsupportedFutureFormat` | At least one codec version is newer than this build |
| `Corrupt` | Validation found error-severity issues |

## Migration

```powershell
# Dry-run (safe, default)
leancorpus-cli.exe migrate .\index --dry-run --json

# Execute staged migration
leancorpus-cli.exe migrate .\index --execute --staging .\index.migration
```

Staged migration writes `migration_state.json` while it works. Normal opens reject an incomplete marker.

## Backup and restore

```powershell
leancorpus-cli.exe backup .\index .\index.backup --json
leancorpus-cli.exe backup .\index .\index.backup --commit-generation 3 --overwrite
leancorpus-cli.exe restore .\index.backup .\index.restored
```

`backup` writes `leancorpus-backup-manifest.json` with commit generation, file names, lengths, CRC-32 checksums, and file roles. `restore` validates the manifest and the restored index.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Validation, compatibility, migration, or restore reported an error |
| `2` | Invalid arguments, missing path, or CLI error |

## JSON output

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

```powershell
dotnet run --project .\src\examples\Rowles.LeanCorpus.Example.NewsgroupsIndexer -- --index .\artifacts\newsgroups-index --limit 500
leancorpus-cli.exe check .\artifacts\newsgroups-index --deep
leancorpus-cli.exe inspect .\artifacts\newsgroups-index --json
leancorpus-cli.exe compat .\artifacts\newsgroups-index
```

## See also

- [Validation and recovery](03-validation-recovery.md)
- <xref:Rowles.LeanCorpus.Index.IndexValidator>
- <xref:Rowles.LeanCorpus.Index.Format.IndexFormatInspector>
