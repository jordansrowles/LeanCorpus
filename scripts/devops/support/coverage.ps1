$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Find-CoverageProjects {
    param(
        [string]$RepoRoot,
        [hashtable]$TestSuites = (Get-TestSuiteRegistry)
    )

    return @(
        Get-CoverageSuiteKeys -TestSuites $TestSuites | ForEach-Object {
            $project = [string]$TestSuites[$_].Project
            if (-not $project) {
                throw "Coverage-eligible suite '$_' has no project path."
            }
            Join-Path $RepoRoot $project
        }
    )
}

function Find-CoverageResults {
    param([string]$ResultsDir)

    return @(Get-ChildItem $ResultsDir -Filter '*.coverage.cobertura.*.xml' -Recurse -ErrorAction SilentlyContinue)
}

function New-CoverageReport {
    param(
        [System.IO.FileInfo[]]$XmlFiles,
        [string]$OutputDir,
        [string]$Title = 'LeanCorpus Coverage'
    )

    Assert-DotNetTool 'reportgenerator' 'dotnet-reportgenerator-globaltool'
    $reportPaths = ($XmlFiles | ForEach-Object { $_.FullName }) -join ';'

    Write-Heading 'Generating coverage report...'
    reportgenerator "-reports:$reportPaths" "-targetdir:$OutputDir" '-reporttypes:Html' "-title:$Title" '-filefilters:-**/obj/**/*.cs'
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Coverage report written to: $OutputDir"
    }
}
