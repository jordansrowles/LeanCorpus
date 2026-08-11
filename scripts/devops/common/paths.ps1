$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RepoRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
}

function Get-ScriptsPath {
    return Join-Path (Get-RepoRoot) 'scripts'
}

function Get-AotProjectPath {
    return Join-Path (Get-RepoRoot) 'src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/Rowles.LeanCorpus.Tests.AOTSmoke.csproj'
}

function Get-BenchmarkProjectPath {
    return Join-Path (Get-RepoRoot) 'src/devops/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj'
}

function Get-DefaultFramework {
    return 'net10.0'
}

function Resolve-RepoPath {
    param([string]$Relative)
    return Join-Path (Get-RepoRoot) $Relative
}
