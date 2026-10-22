[CmdletBinding()]
param(
    [switch]$WriteJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$legacyRoot = Join-Path $repositoryRoot 'scripts/devops/config'
$managedRoot = Join-Path $repositoryRoot '.config/devops'
$configurationNames = @(
    'test-suites',
    'code-areas',
    'benchmark-suites',
    'benchmark-strategies',
    'benchmark-datasets',
    'benchmark-groups'
)

function ConvertTo-NormalisedValue {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $normalised = [ordered]@{}
        foreach ($key in @($Value.Keys | ForEach-Object { [string]$_ } | Sort-Object -CaseSensitive)) {
            $normalised[$key] = ConvertTo-NormalisedValue $Value[$key]
        }

        return $normalised
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $normalisedItems = [System.Collections.Generic.List[object]]::new()
        foreach ($item in $Value) {
            $normalisedItems.Add((ConvertTo-NormalisedValue $item))
        }

        return ,$normalisedItems.ToArray()
    }

    return $Value
}

function ConvertTo-CanonicalJson {
    param([AllowNull()][object]$Value)

    $normalised = ConvertTo-NormalisedValue $Value
    return ConvertTo-Json -InputObject $normalised -Depth 100 -Compress
}

New-Item -ItemType Directory -Path $managedRoot -Force | Out-Null
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($name in $configurationNames) {
    $legacyPath = Join-Path $legacyRoot "$name.psd1"
    $jsonPath = Join-Path $managedRoot "$name.json"
    if (-not (Test-Path -LiteralPath $legacyPath -PathType Leaf)) {
        $failures.Add("Missing authoritative PowerShell data file: $legacyPath")
        continue
    }

    $legacyData = Import-PowerShellDataFile -LiteralPath $legacyPath
    if ($WriteJson) {
        $document = [ordered]@{
            schemaVersion = 1
            data = $legacyData
        }
        $json = ConvertTo-Json -InputObject $document -Depth 100
        Set-Content -LiteralPath $jsonPath -Value $json -Encoding utf8NoBOM
        Write-Output "Wrote $jsonPath from $legacyPath"
        continue
    }

    if (-not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
        $failures.Add("Missing managed JSON file paired with authoritative source: $jsonPath")
        continue
    }

    try {
        $document = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    }
    catch {
        $failures.Add("Invalid JSON in $jsonPath`: $($_.Exception.Message)")
        continue
    }

    if ($document.schemaVersion -isnot [int] -and $document.schemaVersion -isnot [long] -or $document.schemaVersion -ne 1) {
        $failures.Add("Unsupported schemaVersion in $jsonPath; expected integer 1")
        continue
    }

    if (-not $document.Contains('data')) {
        $failures.Add("Missing data object in $jsonPath")
        continue
    }

    $legacyCanonical = ConvertTo-CanonicalJson $legacyData
    $managedCanonical = ConvertTo-CanonicalJson $document.data
    if ($legacyCanonical -cne $managedCanonical) {
        $failures.Add("Semantic content differs between $legacyPath and $jsonPath")
        continue
    }

    Write-Output "Equivalent: $name"
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        [Console]::Error.WriteLine($failure)
    }

    exit 1
}

if (-not $WriteJson) {
    Write-Output 'All six managed JSON files match the authoritative PowerShell data.'
}
