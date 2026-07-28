<#
.SYNOPSIS
    Generates the interactive feature-comparison index from item front matter.

.DESCRIPTION
    Reads each Markdown file under docs/articles/features/items, extracts the
    comparison fields from its YAML front matter, and replaces
    docs/articles/features/index.md with a compact Tabulator table.

.PARAMETER ItemsDir
    Directory containing one Markdown file per feature.

.PARAMETER OutputPath
    Path of the generated feature-comparison index.
#>
param(
    [string]$ItemsDir = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($ItemsDir)) {
    $ItemsDir = Join-Path $repoRoot 'docs/articles/features/items'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'docs/articles/features/index.md'
}

$ItemsDir = [System.IO.Path]::GetFullPath($ItemsDir)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

function ConvertFrom-YamlScalar([string]$Value, [string]$Path, [string]$Key) {
    $value = $Value.Trim()
    if ($value.Length -eq 0) {
        return ''
    }

    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
        try {
            return [System.Text.Json.JsonSerializer]::Deserialize($value, [string])
        } catch {
            throw "Invalid quoted value for '$Key' in '$Path': $value"
        }
    }

    if ($value.StartsWith("'") -and $value.EndsWith("'")) {
        return $value.Substring(1, $value.Length - 2).Replace("''", "'")
    }

    return $value
}

function Get-FeatureFrontMatter([string]$Path) {
    $lines = [System.IO.File]::ReadAllLines($Path)
    if ($lines.Count -lt 2 -or $lines[0].Trim() -ne '---') {
        throw "Feature item '$Path' does not start with YAML front matter."
    }

    $closingLine = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') {
            $closingLine = $i
            break
        }
    }
    if ($closingLine -lt 0) {
        throw "Feature item '$Path' has no closing YAML front-matter delimiter."
    }

    $frontMatter = @{}
    for ($i = 1; $i -lt $closingLine; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([^:#][^:]*):(?:\s?(.*))$') {
            throw "Unsupported YAML front-matter line in '$Path': $line"
        }

        $key = $Matches[1].Trim()
        $rawValue = $Matches[2]

        if ($rawValue -match '^[>|][+-]?$') {
            $blockStyle = $rawValue[0]
            $blockLines = [System.Collections.Generic.List[string]]::new()

            while ($i + 1 -lt $closingLine) {
                $nextLine = $lines[$i + 1]
                if (-not [string]::IsNullOrWhiteSpace($nextLine) -and $nextLine -notmatch '^\s') {
                    break
                }

                $i++
                if ($nextLine -match '^\s{2}(.*)$') {
                    $blockLines.Add($Matches[1])
                } else {
                    $blockLines.Add('')
                }
            }

            $value = ($blockLines -join "`n").Trim()
            if ($blockStyle -eq '>') {
                $value = ($value -replace '\s*\r?\n\s*', ' ').Trim()
            }
            $frontMatter[$key] = $value
        } else {
            $frontMatter[$key] = ConvertFrom-YamlScalar $rawValue $Path $key
        }
    }

    foreach ($requiredKey in @('category', 'leancorpus', 'lucene.net', 'lucene (java)')) {
        if (-not $frontMatter.ContainsKey($requiredKey)) {
            throw "Feature item '$Path' is missing '$requiredKey' front matter."
        }
    }

    return $frontMatter
}

if (-not (Test-Path $ItemsDir -PathType Container)) {
    throw "Feature item directory not found: $ItemsDir"
}

$itemFiles = @(Get-ChildItem $ItemsDir -File -Filter '*.md' | Sort-Object Name)
if ($itemFiles.Count -eq 0) {
    throw "No feature items found in $ItemsDir"
}

$features = @(
    foreach ($itemFile in $itemFiles) {
        $frontMatter = Get-FeatureFrontMatter $itemFile.FullName
        [pscustomobject][ordered]@{
            feature = $itemFile.BaseName
            category = $frontMatter['category']
            leancorpus = $frontMatter['leancorpus']
            luceneNet = $frontMatter['lucene.net']
            luceneJava = $frontMatter['lucene (java)']
            notes = if ($frontMatter.ContainsKey('notes')) { $frontMatter['notes'] } else { '' }
        }
    }
)

$featureData = $features | ConvertTo-Json -Depth 3
$featureData = $featureData.Replace('</', '<\/')

$content = @"
---
title: Feature comparison
_description: Compare LeanCorpus features with Lucene.NET and Lucene for Java.
---

<link href="https://unpkg.com/tabulator-tables@6.5.0/dist/css/tabulator.min.css" rel="stylesheet">

# Feature comparison

`✔` means a direct equivalent is available, `◐` means a broadly comparable capability, and `❌` means no equivalent is available.

Lucene.NET refers to the packaged 4.8 line. Use the column filters to narrow the results, select a heading to sort, or change the grouping below.

<div class="feature-comparison-toolbar">
  <label for="feature-comparison-group">Group by</label>
  <select id="feature-comparison-group" class="form-select form-select-sm">
    <option value="">Nothing</option>
    <option value="category" selected>Category</option>
  </select>
  <span id="feature-comparison-count" aria-live="polite"></span>
</div>

<div id="feature-comparison-table" aria-label="LeanCorpus and Lucene feature comparison"></div>

<style>
  .feature-comparison-toolbar {
    align-items: center;
    display: flex;
    gap: 0.5rem;
    margin: 0.75rem 0;
  }

  .feature-comparison-toolbar select {
    width: auto;
  }

  #feature-comparison-count {
    color: var(--bs-secondary-color);
    margin-left: auto;
  }

  #feature-comparison-table {
    font-size: 0.82rem;
    height: 72vh;
    min-height: 28rem;
    width: 100%;
  }

  #feature-comparison-table .tabulator-header .tabulator-col,
  #feature-comparison-table .tabulator-row .tabulator-cell {
    padding: 0.25rem 0.4rem;
  }

  #feature-comparison-table .tabulator-cell[tabulator-field="notes"] {
    white-space: normal;
  }

  [data-bs-theme="dark"] #feature-comparison-table.tabulator {
    background-color: var(--bs-body-bg);
    border-color: var(--bs-border-color);
    color: var(--bs-body-color);
  }

  [data-bs-theme="dark"] #feature-comparison-table .tabulator-header,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-header .tabulator-col,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-row,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-row-even,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-group {
    background-color: var(--bs-body-bg);
    border-color: var(--bs-border-color);
    color: var(--bs-body-color);
  }

  [data-bs-theme="dark"] #feature-comparison-table .tabulator-row:hover {
    background-color: var(--bs-tertiary-bg);
  }

  [data-bs-theme="dark"] #feature-comparison-table input {
    background-color: var(--bs-tertiary-bg);
    border-color: var(--bs-border-color);
    color: var(--bs-body-color);
  }
</style>

<script id="feature-comparison-data" type="application/json">
$featureData
</script>
<script src="https://unpkg.com/tabulator-tables@6.5.0/dist/js/tabulator.min.js"></script>
<script>
  (() => {
    const initialiseFeatureComparison = () => {
      const dataElement = document.getElementById("feature-comparison-data");
      const tableElement = document.getElementById("feature-comparison-table");
      if (!dataElement || !tableElement || typeof Tabulator === "undefined") {
        return;
      }

      const data = JSON.parse(dataElement.textContent);
      const countElement = document.getElementById("feature-comparison-count");
      const updateCount = rows => {
        countElement.textContent = rows.length + " of " + data.length + " features";
      };

      const table = new Tabulator(tableElement, {
        data,
        groupBy: "category",
        groupStartOpen: false,
        height: "72vh",
        initialSort: [{ column: "feature", dir: "asc" }],
        layout: "fitDataStretch",
        placeholder: "No matching features",
        columns: [
          { title: "Feature", field: "feature", headerFilter: "input", minWidth: 220, width: 260 },
          { title: "Category", field: "category", headerFilter: "input", minWidth: 160, width: 190 },
          { title: "LeanCorpus", field: "leancorpus", headerFilter: "input", minWidth: 220, width: 280 },
          { title: "Lucene.NET", field: "luceneNet", headerFilter: "input", minWidth: 110, width: 120 },
          { title: "Lucene (Java)", field: "luceneJava", headerFilter: "input", minWidth: 120, width: 130 },
          {
            title: "Notes",
            field: "notes",
            formatter: "textarea",
            headerFilter: "input",
            minWidth: 360,
            variableHeight: true
          }
        ]
      });

      table.on("dataFiltered", (_filters, rows) => updateCount(rows));
      table.on("tableBuilt", () => updateCount(table.getRows("active")));

      document.getElementById("feature-comparison-group").addEventListener("change", event => {
        table.setGroupBy(event.target.value || false);
      });
    };

    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", initialiseFeatureComparison, { once: true });
    } else {
      initialiseFeatureComparison();
    }
  })();
</script>
"@

$outputDirectory = Split-Path $OutputPath -Parent
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

# WriteAllText replaces the file, so stale rows cannot survive regeneration.
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($OutputPath, $content, $utf8NoBom)

Write-Host "Generated $($features.Count) feature rows: $OutputPath" -ForegroundColor Green
