param(
    [string]$RuntimeIdentifier
)

if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = if ($IsLinux) { "linux-x64" } elseif ($IsMacOS) { "osx-x64" } else { "win-x64" }
}

$ErrorActionPreference = "Stop"

# Use writable NuGet cache locations (default HTTP cache may be read-only in CI/containers)
if (-not $env:NUGET_HTTP_CACHE_PATH) {
    $env:NUGET_HTTP_CACHE_PATH = Join-Path ([System.IO.Path]::GetTempPath()) "nuget-http-cache"
}
if (-not $env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES = Join-Path ([System.IO.Path]::GetTempPath()) "nuget-packages"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\devops\Rowles.LeanCorpus.Tests.AOTSmoke\Rowles.LeanCorpus.Tests.AOTSmoke.csproj"

$failed = @()

foreach ($tfm in @("net10.0", "net11.0")) {
    Write-Host "Publishing AOT smoke tests for $tfm ($RuntimeIdentifier)..." -ForegroundColor Cyan
    dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true -f $tfm
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $tfm with exit code $LASTEXITCODE."
    }

    $publishDirectory = Join-Path $repoRoot "src\devops\Rowles.LeanCorpus.Tests.AOTSmoke\bin\Release\$tfm\$RuntimeIdentifier\publish"
    $executable = if ($RuntimeIdentifier.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $publishDirectory "Rowles.LeanCorpus.Tests.AOTSmoke.exe"
    } else {
        Join-Path $publishDirectory "Rowles.LeanCorpus.Tests.AOTSmoke"
    }

    Write-Host "Running AOT smoke tests for $tfm..." -ForegroundColor Cyan
    & $executable
    if ($LASTEXITCODE -ne 0) {
        $failed += $tfm
        Write-Host "AOT smoke tests FAILED for $tfm (exit code $LASTEXITCODE)." -ForegroundColor Red
    } else {
        Write-Host "AOT smoke tests passed for $tfm." -ForegroundColor Green
    }
}

if ($failed.Count -gt 0) {
    throw "AOT smoke tests failed for: $($failed -join ', ')"
}

Write-Host "All AOT smoke tests passed." -ForegroundColor Green
