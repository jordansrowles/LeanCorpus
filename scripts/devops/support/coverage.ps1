$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Find-CoverageProjects {
    param([string]$RepoRoot)

    return @(
        (Join-Path $RepoRoot 'src/devops/Rowles.LeanCorpus.Tests.Core/Rowles.LeanCorpus.Tests.Core.csproj')
        (Join-Path $RepoRoot 'src/devops/Rowles.LeanCorpus.Tests.SourceGen/Rowles.LeanCorpus.Tests.SourceGen.csproj')
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
