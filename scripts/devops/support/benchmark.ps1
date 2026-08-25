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
        [int]$BookCount
    )

    $dataDir = Join-Path $RepoRoot 'bench/data'
    $gutenbergDir = Join-Path $dataDir 'gutenberg-ebooks'
    $newsDir = Join-Path $dataDir '20newsgroups'
    $reutersDir = Join-Path $dataDir 'reuters21578'

    $gutenbergCount = if (Test-Path $gutenbergDir) {
        (Get-ChildItem $gutenbergDir -Filter '*.txt' -ErrorAction SilentlyContinue).Count
    } else { 0 }

    if ($gutenbergCount -lt $BookCount) {
        Write-Heading "Preparing Gutenberg data (BookCount=$BookCount)..."
        & (Join-Path $ScriptsPath 'data/download-gutenberg.ps1') -BookCount $BookCount
    } else {
        Write-Info "Gutenberg data present ($gutenbergCount books), skipping download."
    }

    $newsCount = if (Test-Path $newsDir) {
        (Get-ChildItem $newsDir -File -Recurse -ErrorAction SilentlyContinue).Count
    } else { 0 }
    $reutersCount = if (Test-Path $reutersDir) {
        (Get-ChildItem $reutersDir -Filter '*.sgm' -File -ErrorAction SilentlyContinue).Count
    } else { 0 }

    if ($newsCount -eq 0 -or $reutersCount -eq 0) {
        Write-Heading 'Preparing news data...'
        & (Join-Path $ScriptsPath 'data/download-news.ps1')
    } else {
        Write-Info "News data present ($newsCount posts, $reutersCount Reuters files), skipping download."
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
