# Index checker CLI

`Rowles.LeanLucene.Cli` builds `leanlucene-cli.exe`, a Spectre.Console.Cli
wrapper around `IndexValidator.Check`.

## Build the CLI

```powershell
dotnet build .\src\devops\Rowles.LeanLucene.Cli\Rowles.LeanLucene.Cli.csproj -c Release
```

The executable is written under the target framework output directory:

```powershell
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe
```

## Check an index

```powershell
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe check .\index
```

Healthy output is a Spectre.Console panel:

```text
Healthy: checked 2 segment(s), 200 document(s), 46 file(s).
```

Unhealthy output includes one line per issue:

```text
Unhealthy: checked 1 segment(s), 10 document(s), 8 file(s).
Error LLIDX006 seg_0 seg_0.dic Segment 'seg_0' is missing required file 'seg_0.dic'.
```

The formatted output uses a table. The issue columns are severity, stable issue
code, segment ID, file name, repairability, and message.

## Options

```text
leanlucene-cli.exe check <index-path> [--deep] [--json] [--postings] [--stored-fields] [--doc-values] [--vectors] [--hnsw] [--live-docs] [--summary-only] [--fail-on-warnings] [--output <path>]
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

## Exit codes

| Code | Meaning |
|---|---|
| `0` | The index is healthy, or only warning and info issues were found without `--fail-on-warnings` |
| `1` | One or more error-severity validation issues were found |
| `2` | Arguments were invalid, the path did not exist, or the CLI could not run the check |

## JSON output

Use `--json` for automation:

```powershell
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe check .\index --json --doc-values
```

The JSON shape is stable for the CLI:

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
      "isRepairable": true
    }
  ]
}
```

## File output

Use `--output` to write either text or JSON to a file:

```powershell
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe check .\index --summary-only --output .\check-summary.txt
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe check .\index --json --deep --output .\check.json
```

## Interactive mode

Interactive mode prompts for the index path, validation depth, output format,
summary-only output, warning handling, and optional report file:

```powershell
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe interactive
```

You can prefill the path and still choose the remaining options from prompts:

```powershell
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe interactive --index .\index
```

## Create a sample index

`Rowles.LeanLucene.Example.NewsgroupsIndexer` bundles 20 Newsgroups files and
creates a checker-ready index with postings, stored fields, DocValues, vectors,
HNSW, term vectors, and stored-field compression metadata.

```powershell
dotnet run --project .\src\examples\Rowles.LeanLucene.Example.NewsgroupsIndexer -- --index .\artifacts\newsgroups-index --limit 500
.\src\devops\Rowles.LeanLucene.Cli\bin\Release\net10.0\leanlucene-cli.exe check .\artifacts\newsgroups-index --deep
```

The example options are:

| Option | Behaviour |
|---|---|
| `--source <path>` | Use another 20 Newsgroups root instead of the bundled `data\20newsgroups` copy |
| `--index <path>` | Output index path. Defaults to `artifacts\newsgroups-index` |
| `--limit <count>` | Maximum documents to index. Defaults to `500` |
| `--append` | Keep existing index files instead of recreating the output directory |

## See also

- [Validation and recovery](03-validation-recovery.md)
- <xref:Rowles.LeanLucene.Index.IndexValidator>
- <xref:Rowles.LeanLucene.Index.IndexCheckResult>
