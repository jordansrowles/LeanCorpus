<#
.SYNOPSIS
    Downloads the 20 Newsgroups corpus for the explicit indexer example.

.DESCRIPTION
    Downloads and extracts the 20 Newsgroups corpus into bench/data/20newsgroups.
    The corpus is owned by Rowles.LeanCorpus.Example.NewsgroupsIndexer and is
    not used by benchmark or DataForge workflows.

.PARAMETER OutputDir
    Override the output directory. Defaults to bench/data under the repository root.

.EXAMPLE
    .\scripts\data\download-news.ps1
    Downloads the corpus used by the Newsgroups indexer example.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'bench/data'
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $repoRoot $OutputDir
}

$newsDir = Join-Path $OutputDir '20newsgroups'
$newsArchive = Join-Path $OutputDir '20news-bydate.tar.gz'
[void](New-Item -ItemType Directory -Force -Path $OutputDir)

if (-not (Test-Path -LiteralPath $newsArchive)) {
    Write-Host 'Downloading 20 Newsgroups corpus...'
    try {
        Invoke-WebRequest -Uri 'http://qwone.com/~jason/20Newsgroups/20news-bydate.tar.gz' `
            -OutFile $newsArchive -UseBasicParsing -UserAgent 'Mozilla/5.0 (compatible; BenchmarkDataBot/1.0)'
    } catch {
        Write-Host 'Primary source failed; trying the Figshare mirror.'
        Invoke-WebRequest -Uri 'https://ndownloader.figshare.com/files/5975967' `
            -OutFile $newsArchive -UseBasicParsing -UserAgent 'Mozilla/5.0 (compatible; BenchmarkDataBot/1.0)'
    }
} else {
    Write-Host '20 Newsgroups archive already present.'
}

if (-not (Test-Path -LiteralPath $newsDir) -or @(Get-ChildItem -LiteralPath $newsDir -Recurse -File -ErrorAction SilentlyContinue).Count -eq 0) {
    [void](New-Item -ItemType Directory -Force -Path $newsDir)
    tar -xzf $newsArchive -C $newsDir
    if ($LASTEXITCODE -ne 0) {
        throw "Could not extract the 20 Newsgroups archive (tar exit code $LASTEXITCODE)."
    }
} else {
    Write-Host '20 Newsgroups corpus already extracted.'
}

$documentCount = @(Get-ChildItem -LiteralPath $newsDir -Recurse -File | Where-Object { $_.Extension -eq '' -or $_.Extension -eq '.txt' }).Count
Write-Host "Documents: ~$documentCount"
Write-Host "Path: $newsDir"
