$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsCoverage {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $configuration = $parsed.Get('Configuration', 'Release')
    $clean = $parsed.Has('Clean')
    $includePerformance = $parsed.Has('IncludePerformance')
    $generateReport = $parsed.Has('GenerateReport')
    $repoRoot = Get-RepoRoot

    $testProjects = Find-CoverageProjects $repoRoot

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
    Write-Host "  Output:        $resultsDir"
    if (-not $includePerformance) {
        Write-Host '  Filter:        Coverage!=Skip'
    }
    Write-Host ''

    $generatedSourceFilter = '**/obj/**/*.cs'
    foreach ($tp in $testProjects) {
        $projName = [System.IO.Path]::GetFileNameWithoutExtension($tp)
        Write-Info "  $projName..."
        $covArgs = @('test', $tp, '--configuration', $configuration, '--framework', $framework,
            '--collect', 'XPlat Code Coverage', '--results-directory', $resultsDir)
        if (-not $includePerformance) {
            $covArgs += @('--filter', 'Coverage!=Skip')
        }
        $covArgs += @('--', "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile=$generatedSourceFilter")
        Invoke-DotNet $covArgs
    }

    $xmlFiles = Find-CoverageResults $resultsDir
    Write-Host ''
    Write-Success "Coverage data written to: $resultsDir"
    Write-Host "  Found $($xmlFiles.Count) coverage file(s)."

    if ($generateReport) {
        New-CoverageReport -XmlFiles $xmlFiles -OutputDir (Join-Path $repoRoot 'docs/coverage')
    }
    exit 0
}
