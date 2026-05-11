<#
.SYNOPSIS
    Runs LeanLucene tests with code coverage collection.

.DESCRIPTION
    Executes the test suite under coverlet, collecting XPlat Code Coverage
    (Cobertura XML) into ./coverage-results.

.PARAMETER Framework
    Target framework to test. Defaults to net10.0.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Clean
    Remove existing coverage-results before running.

.PARAMETER IncludePerformance
    Include performance-budget tests in coverage collection.

.EXAMPLE
    .\scripts\coverage.ps1
    Runs tests and writes Cobertura XML into ./coverage-results.

.EXAMPLE
    .\scripts\coverage.ps1 -Framework net11.0
    Runs tests targeting net11.0.

.EXAMPLE
    .\scripts\coverage.ps1 -Clean
    Removes previous results before collecting fresh coverage.

.EXAMPLE
    .\scripts\coverage.ps1 -IncludePerformance
    Runs coverage without excluding tests marked Coverage=Skip.
#>
[CmdletBinding()]
param(
    [ValidateSet('net10.0', 'net11.0')]
    [string]$Framework = 'net10.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Clean,

    [switch]$IncludePerformance
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot         = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$testProjectsRoot = Join-Path $repoRoot "src\devops"
$testProjects     = @(
    Get-ChildItem -Path $testProjectsRoot -Filter "*.csproj" -Recurse |
        Where-Object {
            $_.Directory.Name -like "Rowles.LeanLucene.Tests.*" -and
            $_.Directory.Name -ne "Rowles.LeanLucene.Tests.Shared"
        } |
        Sort-Object -Property FullName |
        ForEach-Object { $_.FullName }
)
$resultsDir       = Join-Path $repoRoot "coverage-results"

if ($testProjects.Count -eq 0) {
    Write-Error "No test projects found under: $testProjectsRoot"
    exit 1
}

foreach ($testProject in $testProjects) {
    if (-not (Test-Path $testProject)) {
        Write-Error "Test project not found at: $testProject"
        exit 1
    }
}

if ($Clean -and (Test-Path $resultsDir)) {
    Write-Host "Cleaning coverage-results..." -ForegroundColor Cyan
    Remove-Item $resultsDir -Recurse -Force
}

if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
}

Write-Host "Running tests with coverage collection..." -ForegroundColor Cyan
Write-Host "  Framework:     $Framework" -ForegroundColor DarkGray
Write-Host "  Configuration: $Configuration" -ForegroundColor DarkGray
Write-Host "  Output:        $resultsDir" -ForegroundColor DarkGray
if (-not $IncludePerformance) {
    Write-Host "  Filter:        Coverage!=Skip" -ForegroundColor DarkGray
}
Write-Host ""

foreach ($testProject in $testProjects) {
    Write-Host "  Project:       $([System.IO.Path]::GetFileNameWithoutExtension($testProject))" -ForegroundColor DarkGray

    $testArgs = @(
        'test',
        $testProject,
        '--configuration', $Configuration,
        '--framework', $Framework,
        '--collect', 'XPlat Code Coverage',
        '--results-directory', $resultsDir
    )

    if (-not $IncludePerformance) {
        $testArgs += @('--filter', 'Coverage!=Skip')
    }

    dotnet @testArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

$xmlFiles = @(Get-ChildItem $resultsDir -Filter 'coverage.cobertura.xml' -Recurse)
Write-Host ""
Write-Host "Coverage data written to: $resultsDir" -ForegroundColor Green
Write-Host "  Found $($xmlFiles.Count) coverage file(s)." -ForegroundColor DarkGray
