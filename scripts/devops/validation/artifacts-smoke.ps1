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
            [string]$Generator,
            [string]$RepositoryRoot
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
        if (-not $IsWindows) {
            $caseDifferentPath = Join-Path $Root 'ARTIFACTS-case-test'
            Write-SmokeMarker (Join-Path $caseDifferentPath 'must-survive.txt')
            $caseRefused = $false
            try {
                Remove-OwnedArtifactPath -Path $caseDifferentPath -ArtifactRoot $artifactRoot
            } catch {
                $caseRefused = $true
            }
            Assert-Smoke $caseRefused 'Unix clean accepted a case-different sibling.'
            Assert-Smoke (Test-Path $caseDifferentPath) 'Unix clean removed a case-different sibling.'
        }

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

        # D. Benchmark evidence selection and overlay behaviour.
        $benchmarkRoot = Join-Path $artifactRoot 'benchmark/runs/example'
        $coreRoot = Join-Path $benchmarkRoot 'core'
        $suiteRoot = Join-Path $coreRoot 'query'
        [void][System.IO.Directory]::CreateDirectory($suiteRoot)
        Write-AtomicJsonFile -Path (Join-Path $benchmarkRoot 'run-report.json') -Value @{
            schemaVersion = 1
            status = 'Passed'
            projects = @(@{ project = 'core'; status = 'Passed'; exitCode = 0; path = 'core' })
        }
        Write-AtomicJsonFile -Path (Join-Path $benchmarkRoot 'run.json') -Value @{
            runId = 'example'
            status = 'Passed'
            completedAtUtc = '2026-01-01T00:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $coreRoot 'report.json') -Value @{
            totalBenchmarkCount = 1
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            commitHash = 'smoke'
            dotnetVersion = 'smoke'
            provenance = @{ machineName = 'smoke'; effectiveDocCount = 1 }
            suites = @(@{ suiteName = 'query'; failedBenchmarkCount = 0; missingBenchmarkCount = 0 })
        }
        @(
            '| Method | Mean |',
            '| --- | --- |',
            '| Example | 1 ns |'
        ) | Set-Content -LiteralPath (Join-Path $suiteRoot 'example-report-github.md') -Encoding UTF8
        $olderTextRoot = Join-Path $artifactRoot 'benchmark/runs/text-older'
        $olderTextResults = Join-Path $olderTextRoot 'text/results'
        [void][System.IO.Directory]::CreateDirectory($olderTextResults)
        Write-AtomicJsonFile -Path (Join-Path $olderTextRoot 'run.json') -Value @{
            runId = 'text-older'; status = 'Passed'; completedAtUtc = '2026-01-02T00:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $olderTextRoot 'run-report.json') -Value @{
            projects = @(@{ project = 'text'; status = 'Passed'; exitCode = 0; path = 'text' })
        }
        @('| Method | Mean |', '| --- | --- |', '| OLDER-PASSED | 1 ns |') |
            Set-Content -LiteralPath (Join-Path $olderTextResults 'text-report-github.md') -Encoding UTF8

        $newerTextRoot = Join-Path $artifactRoot 'benchmark/runs/text-newer'
        $newerTextResults = Join-Path $newerTextRoot 'text/results'
        [void][System.IO.Directory]::CreateDirectory($newerTextResults)
        Write-AtomicJsonFile -Path (Join-Path $newerTextRoot 'run.json') -Value @{
            runId = 'text-newer'; status = 'Failed'; completedAtUtc = '2026-01-03T00:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $newerTextRoot 'run-report.json') -Value @{
            projects = @(@{ project = 'text'; status = 'Failed'; exitCode = 1; path = 'text' })
        }
        @('| Method | Mean |', '| --- | --- |', '| NEWER-FAILED | 1 ns |') |
            Set-Content -LiteralPath (Join-Path $newerTextResults 'text-report-github.md') -Encoding UTF8

        $compressionRoot = Join-Path $artifactRoot 'benchmark/runs/compression-only'
        $compressionResults = Join-Path $compressionRoot 'compression/results'
        [void][System.IO.Directory]::CreateDirectory($compressionResults)
        Write-AtomicJsonFile -Path (Join-Path $compressionRoot 'run.json') -Value @{
            runId = 'compression-only'; status = 'Passed'; completedAtUtc = '2026-01-02T12:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $compressionRoot 'run-report.json') -Value @{
            projects = @(@{ project = 'compression'; status = 'Passed'; exitCode = 0; path = 'compression' })
        }
        @('| Method | Mean |', '| --- | --- |', '| COMPRESSION-PASSED | 1 ns |') |
            Set-Content -LiteralPath (Join-Path $compressionResults 'compression-report-github.md') -Encoding UTF8

        $publishedRoot = Join-Path $Root 'published-benchmark-docs'
        [void][System.IO.Directory]::CreateDirectory($publishedRoot)
        Set-Content -LiteralPath (Join-Path $publishedRoot 'unrelated.md') -Value 'PUBLISHED-BASELINE' -Encoding UTF8
        $outputRoot = Join-Path $Root 'generated-benchmark-docs'
        & $Generator -BenchDir (Join-Path $artifactRoot 'benchmark/runs') -OutputDir $outputRoot -PublishedDir $publishedRoot
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'query.md')) 'Core benchmark page was not generated.'
        $textPage = Get-Content -LiteralPath (Join-Path $outputRoot 'text.md') -Raw
        Assert-Smoke ($textPage.Contains('OLDER-PASSED')) 'older passed text evidence was not selected.'
        Assert-Smoke (-not $textPage.Contains('NEWER-FAILED')) 'newer failed text evidence replaced passed evidence.'
        Assert-Smoke ((Get-Content -LiteralPath (Join-Path $outputRoot 'compression.md') -Raw).Contains('COMPRESSION-PASSED')) 'compression-only evidence was not generated.'
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'unrelated.md')) 'published baseline page was erased.'

        # E. One-shot and repeated test classifications.
        $pass = [pscustomobject]@{ Outcome = 'Passed' }
        $fail = [pscustomobject]@{ Outcome = 'Failed' }
        Assert-Smoke ((Get-TestObservationClassification @($pass) 1) -eq 'Passed') 'one pass was not classified Passed.'
        Assert-Smoke ((Get-TestObservationClassification @($fail) 1) -eq 'Failed') 'one failure was not classified Failed.'
        Assert-Smoke ((Get-TestObservationClassification @($pass, $pass) 2) -eq 'Always passes') 'repeated passes were misclassified.'
        Assert-Smoke ((Get-TestObservationClassification @($fail, $fail) 2) -eq 'Always fails') 'repeated failures were misclassified.'
        Assert-Smoke ((Get-TestObservationClassification @($pass, $fail) 2) -eq 'Intermittent failure') 'mixed repeats were misclassified.'

        # F. Windows stress-test timeout and cleanup evidence contract.
        $stressTestPath = Join-Path $RepositoryRoot 'src/devops/Rowles.LeanCorpus.Tests.Core/Index/Integration/CommitSearcherRaceReproTests.cs'
        $stressTest = Get-Content -LiteralPath $stressTestPath -Raw
        Assert-Smoke ($stressTest.Contains('Timeout = 120_000')) 'stress-test timeout is not 120 seconds.'
        Assert-Smoke ($stressTest.Contains('const int iterations = 120')) 'stress iterations changed.'
        Assert-Smoke ($stressTest.Contains('const int pinnedGenerations = 8')) 'pinned generation count changed.'
        foreach ($cleanupStage in @('held searchers released', 'SearcherManager.Dispose', 'IndexWriter.Dispose', 'MMapDirectory.Dispose')) {
            Assert-Smoke ($stressTest.Contains($cleanupStage)) "stress cleanup timing is missing for $cleanupStage."
        }

        Write-Host 'Artefact infrastructure smoke validation passed.'
    } $temporaryRoot $generatorPath (Resolve-Path (Join-Path $PSScriptRoot '../../..'))
    exit 0
} catch {
    Write-Error "Artefact infrastructure smoke validation failed: $($_.Exception.Message)"
    exit 1
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
