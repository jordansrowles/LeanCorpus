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

The existing commands remain available at the top level. The additive Workbench commands provide interactive inspection and deterministic test data while keeping their scriptable forms usable in CI.

## Workbench output modes

Inspection commands use Spectre.Console tables when stdout is an interactive terminal. Redirected output and `--plain` use stable plain text. `--json` writes machine-readable output without terminal prompts. `--no-color` selects plain output for environments that cannot display colour safely.

```bash
leancorpus-cli browse ./index
leancorpus-cli index info ./index
leancorpus-cli segment list ./index --json
leancorpus-cli field inspect ./index body --plain
leancorpus-cli term postings ./index body government
leancorpus-cli document get ./index 12 --json
```

`browse` opens a prompt-driven explorer only when stdin and stdout are interactive. With redirected streams it prints an index overview and exits.

The browser displays **LeanCorpus CLI Workbench** with the index path, live document count and segment count. Invalid queries or document selections show an error panel and return to the view menu.

From a repository checkout, build and start it with:

```bash
./devops build -Project src/devops/Rowles.LeanCorpus.Cli/Rowles.LeanCorpus.Cli.csproj -Framework net10.0
./artifacts/bin/Rowles.LeanCorpus.Cli/release_net10.0/leancorpus-cli browse /path/to/index
```

## Search and inspection

```bash
leancorpus-cli search run ./index 'government AND public' --field body --take 20
leancorpus-cli search explain ./index body government 12
leancorpus-cli vector inspect ./index --json
leancorpus-cli vector neighbours ./index embedding 12 --take 10
leancorpus-cli vector explain ./index embedding 12
leancorpus-cli facet inspect ./index category
leancorpus-cli geo inspect ./index location
leancorpus-cli geo query ./index location 51.5 -0.1 --radius-metres 5000
leancorpus-cli analyse 'A sample of searchable text' --json
```

The geo query accepts a centre latitude and longitude in decimal degrees and a positive radius in metres. Facet inspection reports indexed bucket counts for fields with ordinal DocValues.

## Deterministic index creation

Presets use DataForge as a library and write `.leancorpus/recipe.json` and `.leancorpus/dataforge-manifest.json` beside the index. The first catalogue includes `minimal`, `search-small`, `search-medium`, `realistic-mixed`, `vector-flat`, `vector-hnsw`, `vector-int8`, `vector-bbq`, `vector-filtered`, `hybrid-balanced`, `hybrid-text-heavy`, `hybrid-vector-heavy`, `multilingual`, `compound`, `non-compound`, `many-small-segments`, `delete-heavy`, `codec-current`, and the frozen `codec-migration-required` release fixture.

```bash
leancorpus-cli create preset vector-hnsw ./vector-index --seed 1234 --count 500 --dimension 64
leancorpus-cli create reproduce ./vector-index ./vector-index-recreated
leancorpus-cli index diff ./vector-index ./vector-index-recreated --logical --json
leancorpus-cli index fingerprint ./vector-index
```

Custom schemas use deterministic Search-profile records as their value source. Supported field types are `text`, `keyword`, `double`, `int64`, `vector`, and `geoPoint`; keyword fields can use sorted DocValues or facet paths, and vector fields can request dimensions and HNSW. For example:

```json
{
  "schemaVersion": 1,
  "compound": true,
  "fields": [
    { "name": "title", "type": "text", "source": "title", "stored": true, "positions": true },
    { "name": "category", "type": "keyword", "source": "category", "facet": true, "docValues": true },
    { "name": "price", "type": "double", "source": "price", "docValues": true },
    { "name": "embedding", "type": "vector", "dimensions": 64, "hnsw": true },
    { "name": "location", "type": "geoPoint" }
  ]
}
```

```bash
leancorpus-cli create custom ./custom-index --schema ./schema.json --seed 42 --count 100
```

The schema is copied to `.leancorpus/schema.json` and embedded in the recipe so reproduction does not depend on the original schema file.

## Test Lab and workloads

Mutating commands write a new destination and leave the source index unchanged. Corruption and non-corrupting stress mutations record their operation, seed, target and before/after fingerprints under `.leancorpus/mutation.json`.

```bash
leancorpus-cli testlab corrupt ./index ./corrupt-copy --component postings --mode truncate --seed 123
leancorpus-cli testlab mutate ./index ./many-segments --mode many-small-segments --count 64 --seed 123
leancorpus-cli testlab fixture pack ./index ./issue.lcf
leancorpus-cli testlab fixture unpack ./issue.lcf ./unpacked-index
leancorpus-cli testlab seed-search --from 1 --to 100 --find zero-results --count 64 --json
leancorpus-cli create workload ./workload --seed 42 --count 128 --queries 100
leancorpus-cli search replay ./workload --json
leancorpus-cli search compare ./index 'government' 'market' --field body --take 20
leancorpus-cli transform compact ./index ./compacted-index --segments 1
```

Seed search is bounded to 1,000 seeds per invocation. Available predicates are zero-result queries, segment-count thresholds, validation failures and size outliers. Workload replay compares result document IDs with the saved expected results and reports latency as a functional smoke measure; use BenchmarkDotNet for performance measurements.

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

Reports file inventory without constructing search readers. Text output separates the outer frame from the body format:

```text
seg_1.pos
  format: leancorpus.postings.data
  frame: 1
  format-version: 4
  current: True
  checksum: XxHash64
  checksum-status: NotVerified
  location: LooseFile
```

A deep `check` changes a valid canonical checksum status to `Valid`. Compound members report `CompoundMember` and include their owning `.cfs` file in JSON output.

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
| `UnknownFormat` | A persistent file or declared frame identity is not in the active catalogue |
| `Corrupt` | Validation found error-severity issues |

## Migration

```powershell
# Dry-run (safe, default)
leancorpus-cli.exe migrate .\index --dry-run --json

# Execute staged migration
leancorpus-cli.exe migrate .\index --execute --staging .\index.migration
```

Dry-run actions distinguish reframe, rewrite, coordinated rewrite, compound repack and unsupported paths. Staged migration writes `migration_state.json` while it works. Normal opens reject an incomplete marker.

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
- [Migrating indexes to 3.0](11-codec-migration-3-0.md)
- <xref:Rowles.LeanCorpus.Index.IndexValidator>
- <xref:Rowles.LeanCorpus.Index.Format.IndexFormatInspector>
