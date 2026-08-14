$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Find-CoverageProjects {
    param([string]$RepoRoot)

    $testProjectsRoot = Join-Path $RepoRoot 'src/devops'
    return @(
        Get-ChildItem $testProjectsRoot -Filter '*.csproj' -Recurse |
            Where-Object {
                $dirName = $_.Directory.Name
                $dirName -like 'Rowles.LeanCorpus.Tests.*' -and
                $dirName -ne 'Rowles.LeanCorpus.Tests.Shared' -and
                $dirName -ne 'Rowles.LeanCorpus.Tests.AOTSmoke' -and
                $dirName -ne 'Rowles.LeanCorpus.Benchmarks'
            } |
            Sort-Object FullName | ForEach-Object { $_.FullName }
    )
}

function Find-CoverageResults {
    param([string]$ResultsDir)

    return @(Get-ChildItem $ResultsDir -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)
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
    reportgenerator "-reports:$reportPaths" "-targetdir:$OutputDir" '-reporttypes:Html' "-title:$Title"
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Coverage report written to: $OutputDir"
    }
}
