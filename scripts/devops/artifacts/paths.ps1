$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-ArtifactRoot {
    param([string]$RepoRoot = (Get-RepoRoot))
    return Join-Path $RepoRoot 'artifacts'
}

function Get-ArtifactKindRoot {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('test', 'coverage', 'benchmark', 'diagnostics', 'docs', 'package', 'temp')]
        [string]$Kind,
        [string]$RepoRoot = (Get-RepoRoot)
    )
    return Join-Path (Get-ArtifactRoot -RepoRoot $RepoRoot) $Kind
}

function Get-ArtifactRunsRoot {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('test', 'coverage', 'benchmark', 'diagnostics')]
        [string]$Kind,
        [string]$RepoRoot = (Get-RepoRoot)
    )
    return Join-Path (Get-ArtifactKindRoot -Kind $Kind -RepoRoot $RepoRoot) 'runs'
}

function Get-BenchmarkDataRoot {
    param([string]$RepoRoot = (Get-RepoRoot))
    return Join-Path (Get-ArtifactKindRoot -Kind benchmark -RepoRoot $RepoRoot) 'cache/data'
}

function Get-DocsArtifactPath {
    param(
        [ValidateSet('api', 'coverage', 'site', 'diagnostics', 'generated')]
        [string]$Name,
        [string]$RepoRoot = (Get-RepoRoot)
    )
    return Join-Path (Get-ArtifactKindRoot -Kind docs -RepoRoot $RepoRoot) $Name
}
