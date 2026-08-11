$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsBenchmark {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot
    $scriptsPath = Get-ScriptsPath

    # Detect 'remote' subcommand before full parsing
    $subCmd = if ($Arguments.Count -gt 0 -and -not $Arguments[0].StartsWith('-')) { $Arguments[0] } else { 'run' }
    if ($subCmd -eq 'remote') {
        $remoteScript = Join-Path $scriptsPath 'benchmarks/send-remote.ps1'
        $remoteArgs = @($Arguments | Select-Object -Skip 1)
        if ($remoteArgs.Count -gt 0) { & $remoteScript @remoteArgs } else { & $remoteScript }
        exit $LASTEXITCODE
    }

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $suite = $parsed.Get('Suite', 'all')
    $strat = $parsed.Get('Strat', 'default')
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $docCount = [int]($parsed.Get('DocCount', '0'))
    $bookCount = [int]($parsed.Get('BookCount', '200'))
    $sourceCommit = $parsed.Get('SourceCommit', '')
    $sourceRef = $parsed.Get('SourceRef', '')
    $sourceManifest = $parsed.Get('SourceManifest', '')
    $prepareData = $parsed.Has('PrepareData')
    $corpusOnly = $parsed.Has('CorpusOnly')
    $list = $parsed.Has('List')
    $dry = $parsed.Has('Dry')
    $gcDump = $parsed.Has('GcDump')
    $controlled = $parsed.Has('Controlled')
    $passThrough = $parsed.PassThrough

    $suiteMap = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-suites.psd1"
    $stratMap = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-strategies.psd1"

    if ($list) {
        Write-Host ''
        Write-Host '  Available benchmark suites (-Suite):'
        Write-Host ''
        foreach ($name in $suiteMap.Keys) {
            Write-Host ("    {0,-22} {1}" -f $name, $suiteMap[$name])
        }
        Write-Host ''
        Write-Host '  Available strategies (-Strat):'
        Write-Host ''
        foreach ($name in $stratMap.Keys) {
            Write-Host ("    {0,-16} {1}" -f $name, $stratMap[$name].Description)
        }
        Write-Host ''
        exit 0
    }

    $stratCfg = Resolve-BenchmarkStrategy $strat
    $stratDocCount = $stratCfg.DocCount
    $stratJobArgs = $stratCfg.Job

    if ($controlled) {
        if ($docCount -le 0 -and $stratDocCount -le 0) { $stratDocCount = 1000 }
        if ($stratJobArgs.Count -eq 0) { $stratJobArgs = @('--job', 'short') }
        $corpusOnly = $true
    }

    $effectiveDocCount = 0
    if ($docCount -gt 0) { $effectiveDocCount = $docCount }
    elseif ($stratDocCount -gt 0) { $effectiveDocCount = $stratDocCount }

    $projectPath = Get-BenchmarkProjectPath

    if ($prepareData) {
        Prepare-BenchmarkData -RepoRoot $repoRoot -ScriptsPath $scriptsPath -BookCount $bookCount
    }

    $runArgs = @('--suite', $suite)
    if ($effectiveDocCount -gt 0) {
        $runArgs += @('--doccount', $effectiveDocCount.ToString())
        $env:BENCH_DOC_COUNT = $effectiveDocCount.ToString()
    }
    if ($corpusOnly) { $runArgs += '--corpus-only' }
    if ($sourceCommit)   { $env:BENCH_SOURCE_COMMIT   = $sourceCommit }
    if ($sourceRef)      { $env:BENCH_SOURCE_REF      = $sourceRef }
    if ($sourceManifest) { $env:BENCH_SOURCE_MANIFEST = [System.IO.Path]::GetFullPath($sourceManifest) }

    Write-Host "Suite:      $suite"
    Write-Host "Strat:      $strat"
    Write-Host "Framework:  $framework"
    if ($controlled)     { Write-Host 'Mode:       controlled' }
    if ($corpusOnly)     { Write-Host 'CorpusOnly: enabled' }
    if ($effectiveDocCount -gt 0) { Write-Host "Docs:       $effectiveDocCount" }
    if ($stratJobArgs)   { Write-Host "Job:        $($stratJobArgs -join ' ')" }
    if ($passThrough)    { Write-Host "BDN args:   $($passThrough -join ' ')" }

    if ($dry) {
        Write-Host ''
        Write-Host 'Dry run - command that would execute:'
        Write-Host "  dotnet run -c Release --framework $framework --project `"$projectPath`" -- $($runArgs -join ' ') $($stratJobArgs -join ' ') $($passThrough -join ' ')"
        Write-Host ''
        exit 0
    }

    if ($gcDump) {
        $runArgs += '--gcdump'
        Assert-DotNetTool 'dotnet-gcdump'
    }

    Write-Host ''
    dotnet run -c Release --framework $framework --project $projectPath -- @runArgs @stratJobArgs @passThrough
    exit $LASTEXITCODE
}
