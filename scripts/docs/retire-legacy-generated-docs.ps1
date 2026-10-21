$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$docsRoot = Join-Path $repoRoot 'docs'
$artifactRoot = Join-Path $repoRoot 'artifacts/docs'
$contentRoot = Join-Path $artifactRoot 'content'

function Assert-RequiredFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Replacement documentation output is missing: $Path"
    }
}

$requiredFiles = @(
    (Join-Path $artifactRoot 'api/toc.yml'),
    (Join-Path $contentRoot 'api/index.md'),
    (Join-Path $contentRoot 'toc.yml'),
    (Join-Path $contentRoot 'generated/examples/index.md'),
    (Join-Path $contentRoot 'changelog/index.md'),
    (Join-Path $contentRoot 'changelog/toc.yml'),
    (Join-Path $contentRoot 'benchmarks/overview.md'),
    (Join-Path $contentRoot 'benchmarks/toc.yml'),
    (Join-Path $contentRoot 'benchmarks/benchmark-charts.js'),
    (Join-Path $contentRoot 'benchmarks/chart.umd.js'),
    (Join-Path $contentRoot 'articles/ADRs/index.md'),
    (Join-Path $contentRoot 'articles/ADRs/toc.yml'),
    (Join-Path $contentRoot 'assets/diagrams/codeckit-envelope.svg'),
    (Join-Path $contentRoot 'assets/diagrams/mermaid-11.16.0.min.js'),
    (Join-Path $artifactRoot 'site/index.html'),
    (Join-Path $artifactRoot 'site/api/index.html'),
    (Join-Path $artifactRoot 'site/benchmarks/index.html')
)
foreach ($path in $requiredFiles) { Assert-RequiredFile -Path $path }

$apiFiles = @(Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'api') -File -Filter '*.yml')
$changelogPages = @(Get-ChildItem -LiteralPath (Join-Path $contentRoot 'changelog') -Recurse -File -Filter '*.md')
$benchmarkPages = @(Get-ChildItem -LiteralPath (Join-Path $contentRoot 'benchmarks') -File -Filter '*.md')
$adrIndex = [System.IO.File]::ReadAllText((Join-Path $contentRoot 'articles/ADRs/index.md'))
$adrRows = [regex]::Matches($adrIndex, '<tr><td><a href=').Count
if ($apiFiles.Count -lt 100) { throw "Expected staged API metadata, found only $($apiFiles.Count) YAML files." }
if ($changelogPages.Count -lt 19) { throw "Expected staged changelog history, found only $($changelogPages.Count) pages." }
if ($benchmarkPages.Count -lt 40) { throw "Expected staged benchmark pages, found only $($benchmarkPages.Count) pages." }
if ($adrRows -lt 40) { throw "Expected a generated core ADR index, found only $adrRows rows." }

$legacyRoots = @('docs/benchmarks', 'docs/changelog')
$legacyTracked = @(& git -C $repoRoot ls-files -- docs/benchmarks docs/changelog docs/articles/ADRs/index.md `
    docs/assets/diagrams/codeckit-envelope.svg docs/assets/diagrams/mermaid-11.16.0.min.js)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked legacy documentation outputs.' }
$legacyTrackedSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $legacyTracked) { [void]$legacyTrackedSet.Add([string]$path) }

foreach ($relativeRoot in $legacyRoots) {
    $absoluteRoot = Join-Path $repoRoot $relativeRoot
    if (-not (Test-Path -LiteralPath $absoluteRoot -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $absoluteRoot -Recurse -File -Force) {
        $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
        if (-not $legacyTrackedSet.Contains($relativePath)) {
            throw "Untracked work exists under legacy output '$relativeRoot': $relativePath"
        }
    }
}

$canonicalUnreleased = Join-Path $repoRoot 'changelog/unreleased.md'
$legacyUnreleased = Join-Path $repoRoot 'docs/changelog/unreleased.md'
if (Test-Path -LiteralPath $legacyUnreleased -PathType Leaf) {
    if (-not (Test-Path -LiteralPath $canonicalUnreleased -PathType Leaf)) {
        throw 'The canonical unreleased changelog is missing.'
    }
    $canonicalHash = (Get-FileHash -LiteralPath $canonicalUnreleased -Algorithm SHA256).Hash
    $legacyHash = (Get-FileHash -LiteralPath $legacyUnreleased -Algorithm SHA256).Hash
    if ($canonicalHash -ne $legacyHash) {
        throw 'The legacy unreleased changelog differs from its canonical source; it was preserved.'
    }
}

$diagramCopies = @(
    [pscustomobject]@{
        Legacy = Join-Path $docsRoot 'site-config/template/public/assets/diagrams/codeckit-envelope.svg'
        Generated = Join-Path $contentRoot 'assets/diagrams/codeckit-envelope.svg'
    }
    [pscustomobject]@{
        Legacy = Join-Path $docsRoot 'site-config/template/public/assets/diagrams/mermaid-11.16.0.min.js'
        Generated = Join-Path $contentRoot 'assets/diagrams/mermaid-11.16.0.min.js'
    }
)
foreach ($copy in $diagramCopies) {
    if (Test-Path -LiteralPath $copy.Legacy -PathType Leaf) {
        $legacyHash = (Get-FileHash -LiteralPath $copy.Legacy -Algorithm SHA256).Hash
        $generatedHash = (Get-FileHash -LiteralPath $copy.Generated -Algorithm SHA256).Hash
        if ($legacyHash -ne $generatedHash) {
            throw "Legacy diagram asset differs from its generated replacement; it was preserved: $($copy.Legacy)"
        }
    }
}

$legacyApiDirectory = Join-Path $docsRoot 'api'
$legacyApiFiles = @()
if (Test-Path -LiteralPath $legacyApiDirectory -PathType Container) {
    $legacyApiFiles = @(Get-ChildItem -LiteralPath $legacyApiDirectory -File -Force |
        Where-Object { $_.Extension -eq '.yml' -or $_.Name -eq '.manifest' })
    $unexpectedApiFiles = @(Get-ChildItem -LiteralPath $legacyApiDirectory -File -Force |
        Where-Object { $_.Name -ne 'index.md' -and $_.Extension -ne '.yml' -and $_.Name -ne '.manifest' })
    if ($unexpectedApiFiles.Count -gt 0) {
        throw "Unexpected files exist in the legacy API output directory; they were preserved: $($unexpectedApiFiles.Name -join ', ')"
    }
}

$legacyFilesToRemove = [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $legacyTracked) {
    $legacyFilesToRemove.Add((Join-Path $repoRoot $relativePath))
}
foreach ($copy in $diagramCopies) { $legacyFilesToRemove.Add($copy.Legacy) }
$legacyAdrIndex = Join-Path $docsRoot 'content/articles/ADRs/index.md'
if (Test-Path -LiteralPath $legacyAdrIndex -PathType Leaf) {
    $legacyAdrContent = [System.IO.File]::ReadAllText($legacyAdrIndex)
    if ($legacyAdrContent -notmatch 'adr-index-table|<table') {
        throw "The staged content path contains a non-generated ADR index; it was preserved: $legacyAdrIndex"
    }
    $legacyFilesToRemove.Add($legacyAdrIndex)
}
foreach ($file in $legacyApiFiles) { $legacyFilesToRemove.Add($file.FullName) }

$removed = 0
foreach ($path in $legacyFilesToRemove | Sort-Object -Unique) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
        $removed++
    }
}

Write-Host "Retired legacy generated documentation files: $removed"
Write-Host "Replacement API metadata: $($apiFiles.Count) YAML files under artifacts/docs/api"
Write-Host "Replacement changelog pages: $($changelogPages.Count) under artifacts/docs/content/changelog"
Write-Host "Replacement benchmark pages: $($benchmarkPages.Count) under artifacts/docs/content/benchmarks"
Write-Host "Replacement core ADR rows: $adrRows under artifacts/docs/content/articles/ADRs"
