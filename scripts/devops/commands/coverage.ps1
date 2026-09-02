$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsCoverage {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $configuration = $parsed.Get('Configuration', 'Release')
    $suite = $parsed.Get('Suite', 'all')
    $clean = $parsed.Has('Clean')
    $includePerformance = $parsed.Has('IncludePerformance')
    $generateReport = $parsed.Has('GenerateReport')
    $repoRoot = Get-RepoRoot

    $testProjects = Find-CoverageProjects $repoRoot
    if ($suite -ne 'all') {
        $suiteProjects = @{
            core = 'Rowles.LeanCorpus.Tests.Core'
            sourcegen = 'Rowles.LeanCorpus.Tests.SourceGen'
        }
        if (-not $suiteProjects.ContainsKey($suite)) {
            Write-Error "Unknown coverage suite '$suite'. Expected core, sourcegen, or all."
            exit 1
        }
        $testProjects = @($testProjects | Where-Object {
            [System.IO.Path]::GetFileNameWithoutExtension($_) -eq $suiteProjects[$suite]
        })
    }

    if ($testProjects.Count -eq 0) {
        Write-Error 'No test projects found.'
        exit 1
    }

    $resultsDir = Join-Path $repoRoot 'coverage-results'
    if ($clean -and (Test-Path $resultsDir)) {
        Remove-Item $resultsDir -Recurse -Force
    }
    if (-not (Test-Path $resultsDir)) {
        New-Item -ItemType Directory -Path $resultsDir | Out-Null
    }

    Write-Heading 'Running tests with coverage collection...'
    Write-Host "  Framework:     $framework"
    Write-Host "  Configuration: $configuration"
    Write-Host "  Suite:         $suite"
    Write-Host "  Output:        $resultsDir"
    if (-not $includePerformance) {
        Write-Host '  Filter:        Coverage!=Skip'
    }
    Write-Host ''

    foreach ($tp in $testProjects) {
        $projName = [System.IO.Path]::GetFileNameWithoutExtension($tp)
        $projectResultsDir = Join-Path $resultsDir "$framework/$projName"
        Write-Info "  $projName..."
        $covArgs = @('test', $tp, '--configuration', $configuration, '--framework', $framework,
            '--results-directory', $projectResultsDir,
            '--coverlet', '--coverlet-output-format', 'cobertura',
            '--coverlet-file-prefix', "$projName-$framework",
            '--coverlet-exclude-by-file', '**/obj/**/*.cs')
        if (-not $includePerformance) {
            $covArgs += @('--filter', 'Coverage!=Skip')
        }
        Invoke-DotNet $covArgs
    }

    $xmlFiles = @(Find-CoverageResults $resultsDir)
    Write-Host ''
    Write-Success "Coverage data written to: $resultsDir"
    Write-Host "  Found $($xmlFiles.Count) coverage file(s)."

    if ($generateReport) {
        New-CoverageReport -XmlFiles $xmlFiles -OutputDir (Join-Path $repoRoot 'docs/coverage')
    }
    exit 0
}
