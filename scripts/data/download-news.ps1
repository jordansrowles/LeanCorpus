<#
.SYNOPSIS
    Downloads news article datasets for benchmark testing.

.DESCRIPTION
    Downloads two standardised news/text corpora used widely in NLP research:

    1. 20 Newsgroups (20news-bydate)
       18,846 newsgroup posts across 20 topic categories. Plain-text, no parsing
       required. ~14 MB compressed. Widely used for text classification and indexing
       research.
       Source: http://qwone.com/~jason/20Newsgroups/

    2. Reuters-21578
       21,578 newswire articles from 1987. ~8 MB compressed. One of the oldest and
       most cited text categorisation benchmarks.
       Source: http://www.daviddlewis.com/resources/testcollections/reuters21578/

    Both datasets are free for research and academic use.

.PARAMETER OutputDir
    Override the base output directory. Defaults to bench/data relative to the
    repository root. Datasets are extracted into subdirectories within it.

.PARAMETER Skip20News
    Skip downloading the 20 Newsgroups dataset.

.PARAMETER SkipReuters
    Skip downloading the Reuters-21578 dataset.

.EXAMPLE
    .\scripts\download-news.ps1
    Downloads both datasets.

.EXAMPLE
    .\scripts\download-news.ps1 -SkipReuters
    Downloads only the 20 Newsgroups dataset.
#>
param(
    [string]$OutputDir = '',
    [switch]$Skip20News,
    [switch]$SkipReuters
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "bench\data"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Download-And-Extract {
    param(
        [string]$Url,
        [string]$FallbackUrl,
        [string]$ArchivePath,
        [string]$ExtractDir,
        [string]$DatasetName
    )

    if (-not (Test-Path $ArchivePath)) {
        Write-Host "Downloading $DatasetName..." -ForegroundColor Cyan
        Write-Host "  Source: $Url"
        try {
            Invoke-WebRequest -Uri $Url -OutFile $ArchivePath -UseBasicParsing `
                -UserAgent "Mozilla/5.0 (compatible; BenchmarkDataBot/1.0)"
        } catch {
            Write-Host "  Primary source failed, trying fallback: $FallbackUrl" -ForegroundColor Yellow
            Invoke-WebRequest -Uri $FallbackUrl -OutFile $ArchivePath -UseBasicParsing `
                -UserAgent "Mozilla/5.0 (compatible; BenchmarkDataBot/1.0)"
        }
        Write-Host "  Downloaded: $ArchivePath" -ForegroundColor Green
    } else {
        Write-Host "$DatasetName archive already present." -ForegroundColor DarkGray
    }

    if (-not (Test-Path $ExtractDir) -or (Get-ChildItem $ExtractDir -Recurse -File).Count -eq 0) {
        Write-Host "Extracting $DatasetName to: $ExtractDir" -ForegroundColor Cyan
        New-Item -ItemType Directory -Force -Path $ExtractDir | Out-Null
        tar -xzf $ArchivePath -C $ExtractDir
        Write-Host "  Extracted." -ForegroundColor Green
    } else {
        Write-Host "$DatasetName already extracted." -ForegroundColor DarkGray
    }
}

# ---- 20 Newsgroups ----
if (-not $Skip20News) {
    Write-Host ""
    Write-Host "=== 20 Newsgroups ===" -ForegroundColor White

    $newsDir     = Join-Path $OutputDir "20newsgroups"
    $newsArchive = Join-Path $OutputDir "20news-bydate.tar.gz"

    Download-And-Extract `
        -Url "http://qwone.com/~jason/20Newsgroups/20news-bydate.tar.gz" `
        -FallbackUrl "https://ndownloader.figshare.com/files/5975967" `
        -ArchivePath $newsArchive `
        -ExtractDir $newsDir `
        -DatasetName "20 Newsgroups"

    # Count documents
    $docCount = (Get-ChildItem $newsDir -Recurse -File | Where-Object { $_.Extension -eq '' -or $_.Extension -eq '.txt' }).Count
    Write-Host "  Documents: ~$docCount" -ForegroundColor Green
    Write-Host "  Path: $newsDir"
}

# ---- Reuters-21578 ----
if (-not $SkipReuters) {
    Write-Host ""
    Write-Host "=== Reuters-21578 ===" -ForegroundColor White

    $reutersDir     = Join-Path $OutputDir "reuters21578"
    $reutersArchive = Join-Path $OutputDir "reuters21578.tar.gz"

    Download-And-Extract `
        -Url "http://www.daviddlewis.com/resources/testcollections/reuters21578/reuters21578.tar.gz" `
        -FallbackUrl "https://archive.ics.uci.edu/ml/machine-learning-databases/reuters21578-mld/reuters21578.tar.gz" `
        -ArchivePath $reutersArchive `
        -ExtractDir $reutersDir `
        -DatasetName "Reuters-21578"

    # Count .sgm files
    $sgmCount = (Get-ChildItem $reutersDir -Filter "*.sgm" -File).Count
    Write-Host "  SGM files: $sgmCount (each contains multiple articles)" -ForegroundColor Green
    Write-Host "  Path: $reutersDir"
    Write-Host ""
    Write-Host "  Note: Reuters-21578 uses SGML format. Extract <BODY> content from" -ForegroundColor DarkGray
    Write-Host "  .sgm files to use with the benchmarks." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Complete. Data in: $OutputDir" -ForegroundColor Yellow
