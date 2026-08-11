$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsAot {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $runtimeIdentifier = $parsed.Get('RuntimeIdentifier', '')
    $repoRoot = Get-RepoRoot

    if (-not $runtimeIdentifier) {
        $runtimeIdentifier = if ($IsLinux) { 'linux-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'win-x64' }
    }

    # Use writable NuGet cache locations (default HTTP cache may be read-only in CI)
    if (-not $env:NUGET_HTTP_CACHE_PATH) {
        $env:NUGET_HTTP_CACHE_PATH = Join-Path ([System.IO.Path]::GetTempPath()) 'nuget-http-cache'
    }
    if (-not $env:NUGET_PACKAGES) {
        $env:NUGET_PACKAGES = Join-Path ([System.IO.Path]::GetTempPath()) 'nuget-packages'
    }

    $project = Get-AotProjectPath

    $failed = @()
    foreach ($tfm in @('net10.0', 'net11.0')) {
        Write-Heading "Publishing AOT smoke tests for $tfm ($runtimeIdentifier)..."
        dotnet publish $project -c Release -r $runtimeIdentifier --self-contained true -f $tfm
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet publish failed for $tfm with exit code $LASTEXITCODE."
            $failed += $tfm
            continue
        }

        $publishDir = Join-Path $repoRoot "src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/bin/Release/$tfm/$runtimeIdentifier/publish"
        $exe = if ($runtimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
            Join-Path $publishDir 'Rowles.LeanCorpus.Tests.AOTSmoke.exe'
        } else {
            Join-Path $publishDir 'Rowles.LeanCorpus.Tests.AOTSmoke'
        }

        Write-Heading "Running AOT smoke tests for $tfm..."
        & $exe
        if ($LASTEXITCODE -ne 0) {
            Write-Failure "AOT smoke tests FAILED for $tfm (exit code $LASTEXITCODE)."
            $failed += $tfm
        } else {
            Write-Success "AOT smoke tests passed for $tfm."
        }
    }

    if ($failed.Count -gt 0) {
        Write-Error "AOT smoke tests failed for: $($failed -join ', ')"
        exit 1
    }
    Write-Success 'All AOT smoke tests passed.'
    exit 0
}
