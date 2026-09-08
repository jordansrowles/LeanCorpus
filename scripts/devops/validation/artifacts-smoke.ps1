Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "leancorpus-artifacts-smoke-$([Guid]::NewGuid().ToString('N'))"
$modulePath = Join-Path $PSScriptRoot '../DevOps.psm1'
$generatorPath = Join-Path $PSScriptRoot '../../benchmarks/generate-docs.ps1'

try {
    [void][System.IO.Directory]::CreateDirectory($temporaryRoot)
    $devopsModule = Import-Module -Name $modulePath -Force -PassThru

    & $devopsModule {
        param(
            [string]$Root,
            [string]$Generator
        )

        function Assert-Smoke {
            param(
                [bool]$Condition,
                [Parameter(Mandatory = $true)]
                [string]$Message
            )

            if (-not $Condition) {
                throw "Artefact smoke assertion failed: $Message"
            }
        }

        function Write-SmokeMarker {
            param([string]$Path)

            $parent = Split-Path -Parent $Path
            [void][System.IO.Directory]::CreateDirectory($parent)
            [System.IO.File]::WriteAllText($Path, 'smoke')
        }

        # A. Common run lifecycle.
        $run = New-ArtifactRun -Kind test -Framework net10.0 -Configuration Release `
            -CommandLine 'artifacts-smoke' -RepoRoot $Root -RunId 'lifecycle'
        $manifestPath = Join-Path $run.RunDirectory 'run.json'
        Assert-Smoke (Test-Path $manifestPath -PathType Leaf) 'run.json was not created.'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-Smoke ($manifest.status -eq 'Running') 'new run was not Running.'
        Assert-Smoke ('commit' -in @($manifest.PSObject.Properties.Name)) 'common commit provenance is missing.'
        Update-ArtifactRunManifest -RunDirectory $run.RunDirectory -Values @{ customField = 'retained' }
        Complete-ArtifactRun -RunDirectory $run.RunDirectory -Status Passed
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-Smoke ($manifest.status -eq 'Passed') 'completed run was not Passed.'
        Assert-Smoke ([bool]$manifest.completedAtUtc) 'completion timestamp is missing.'
        Assert-Smoke ($manifest.customField -eq 'retained') 'custom manifest field was lost.'
        $latestRun = Get-LatestSuccessfulArtifactRun -Kind test -RepoRoot $Root
        Assert-Smoke ($null -ne $latestRun -and $latestRun.RunDirectory -eq $run.RunDirectory) 'latest successful run discovery failed.'

        # B. Clean containment.
        $artifactRoot = Get-ArtifactRoot -RepoRoot $Root
        $ownedFile = Join-Path $artifactRoot 'test/delete-me.txt'
        $outsideFile = Join-Path $Root 'outside/must-survive.txt'
        Write-SmokeMarker $ownedFile
        Write-SmokeMarker $outsideFile
        Invoke-ArtifactClean -Target test -RepoRoot $Root
        Assert-Smoke (-not (Test-Path $ownedFile)) 'owned test artefact was not removed.'
        Assert-Smoke (Test-Path $outsideFile) 'outside file was removed by owned clean.'
        $refused = $false
        try {
            Remove-OwnedArtifactPath -Path (Split-Path -Parent $outsideFile) -ArtifactRoot $artifactRoot
        } catch {
            $refused = $true
        }
        Assert-Smoke $refused 'clean accepted a path outside the artefact root.'

        # C. Default clean preservation.
        foreach ($relative in @(
            'test/marker.txt',
            'coverage/marker.txt',
            'docs/marker.txt',
            'diagnostics/marker.txt',
            'temp/marker.txt',
            'benchmark/runs/marker.txt',
            'benchmark/cache/marker.txt',
            'package/marker.txt'
        )) {
            Write-SmokeMarker (Join-Path $artifactRoot $relative)
        }
        Invoke-ArtifactClean -Target default -RepoRoot $Root
        foreach ($relative in @('test', 'coverage', 'docs', 'diagnostics', 'temp')) {
            Assert-Smoke (-not (Test-Path (Join-Path $artifactRoot $relative))) "default clean retained $relative."
        }
        foreach ($relative in @('benchmark/runs', 'benchmark/cache', 'package')) {
            Assert-Smoke (Test-Path (Join-Path $artifactRoot $relative)) "default clean removed $relative."
        }

        # D. Benchmark report schema coexistence.
        $benchmarkRoot = Join-Path $artifactRoot 'benchmark/runs/example'
        $coreRoot = Join-Path $benchmarkRoot 'core'
        $suiteRoot = Join-Path $coreRoot 'query'
        [void][System.IO.Directory]::CreateDirectory($suiteRoot)
        Write-AtomicJsonFile -Path (Join-Path $benchmarkRoot 'run-report.json') -Value @{
            schemaVersion = 1
            status = 'Passed'
            projects = @()
        }
        Write-AtomicJsonFile -Path (Join-Path $coreRoot 'report.json') -Value @{
            totalBenchmarkCount = 1
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            commitHash = 'smoke'
            dotnetVersion = 'smoke'
            provenance = @{ machineName = 'smoke'; effectiveDocCount = 1 }
            suites = @(@{ suiteName = 'query' })
        }
        @(
            '| Method | Mean |',
            '| --- | --- |',
            '| Example | 1 ns |'
        ) | Set-Content -LiteralPath (Join-Path $suiteRoot 'example-report-github.md') -Encoding UTF8
        $outputRoot = Join-Path $Root 'generated-benchmark-docs'
        & $Generator -BenchDir (Join-Path $artifactRoot 'benchmark/runs') -OutputDir $outputRoot
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'query.md')) 'Core benchmark page was not generated.'

        Write-Host 'Artefact infrastructure smoke validation passed.'
    } $temporaryRoot $generatorPath
    exit 0
} catch {
    Write-Error "Artefact infrastructure smoke validation failed: $($_.Exception.Message)"
    exit 1
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
