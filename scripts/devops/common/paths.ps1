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
    $projects = Get-DevOpsConfigurationData -ConfigurationName 'benchmark-projects'
    return Join-Path (Get-RepoRoot) $projects.core.Project
}

function Get-DefaultFramework {
    return 'net10.0'
}

function Resolve-RepoPath {
    param([string]$Relative)
    return Join-Path (Get-RepoRoot) $Relative
}
