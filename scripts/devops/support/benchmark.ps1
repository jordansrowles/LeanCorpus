$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-BenchmarkStrategy {
    param([string]$Name)

    $candidates = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-strategies.psd1"
    if (-not $candidates.ContainsKey($Name)) {
        throw "Unknown benchmark strategy '$Name'. Valid: $($candidates.Keys -join ', ')"
    }
    return $candidates[$Name]
}

function Prepare-BenchmarkData {
    param(
        [string]$RepoRoot,
        [string]$ScriptsPath,
        [int]$BookCount,
        [string]$Suite,
        [string[]]$PassThrough
    )

    $needsGutenberg = $Suite -in @('gutenberg-index', 'gutenberg-search') -or
        ($Suite -eq 'text' -and ($PassThrough -match 'GutenbergAnalysisBenchmarks').Count -gt 0)
    if (-not $needsGutenberg) {
        Write-Info "No external data preparation is required for suite '$Suite'."
        return
    }

    $dataDir = Get-BenchmarkDataRoot -RepoRoot $RepoRoot
    $gutenbergDir = Join-Path $dataDir 'gutenberg-ebooks'

    $gutenbergCount = if (Test-Path $gutenbergDir) {
        (Get-ChildItem $gutenbergDir -Filter '*.txt' -ErrorAction SilentlyContinue).Count
    } else { 0 }

    if ($gutenbergCount -lt $BookCount) {
        Write-Heading "Preparing Gutenberg data (BookCount=$BookCount)..."
        & (Join-Path $ScriptsPath 'data/download-gutenberg.ps1') -BookCount $BookCount
    } else {
        Write-Info "Gutenberg data present ($gutenbergCount books), skipping download."
    }

    Write-Host ''
}

function Build-BenchmarkArguments {
    param(
        [hashtable]$Strategy,
        [string]$Suite,
        [string]$Framework,
        [int]$DocCount,
        [bool]$CorpusOnly,
        [bool]$GcDump,
        [string]$SourceCommit,
        [string]$SourceRef,
        [string]$SourceManifest,
        [string[]]$PassThrough
    )

    $runArgs = @('--suite', $Suite)

    $effectiveDocCount = 0
    if ($DocCount -gt 0) {
        $effectiveDocCount = $DocCount
    } elseif ($Strategy.DocCount -gt 0) {
        $effectiveDocCount = $Strategy.DocCount
    }

    if ($effectiveDocCount -gt 0) {
        $runArgs += @('--doccount', $effectiveDocCount.ToString())
        $env:BENCH_DOC_COUNT = $effectiveDocCount.ToString()
    }
    if ($CorpusOnly) { $runArgs += '--corpus-only' }
    if ($GcDump) { $runArgs += '--gcdump' }
    if ($SourceCommit)   { $env:BENCH_SOURCE_COMMIT   = $SourceCommit }
    if ($SourceRef)      { $env:BENCH_SOURCE_REF      = $SourceRef }
    if ($SourceManifest) { $env:BENCH_SOURCE_MANIFEST = [System.IO.Path]::GetFullPath($SourceManifest) }

    return @{
        Framework = $Framework
        RunArgs = $runArgs
        EffectiveDocCount = $effectiveDocCount
    }
}
