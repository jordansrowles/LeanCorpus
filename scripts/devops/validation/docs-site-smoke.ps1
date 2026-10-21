param(
    [string]$RepoRoot = '',
    [string]$ContentDirectory = '',
    [string]$ApiDirectory = '',
    [string]$SiteDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$artifactRoot = Join-Path $RepoRoot 'artifacts/docs'
if ([string]::IsNullOrWhiteSpace($ContentDirectory)) { $ContentDirectory = Join-Path $artifactRoot 'content' }
if ([string]::IsNullOrWhiteSpace($ApiDirectory)) { $ApiDirectory = Join-Path $artifactRoot 'api' }
if ([string]::IsNullOrWhiteSpace($SiteDirectory)) { $SiteDirectory = Join-Path $artifactRoot 'site' }
$ContentDirectory = [System.IO.Path]::GetFullPath($ContentDirectory)
$ApiDirectory = [System.IO.Path]::GetFullPath($ApiDirectory)
$SiteDirectory = [System.IO.Path]::GetFullPath($SiteDirectory)

function Assert-DocsSmoke([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Documentation site smoke failed: $Message" }
}

function Get-TocTargets([string]$Path) {
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $match = [regex]::Match($line, '^\s*href:\s*(?:''(?<single>[^'']*)''|"(?<double>[^"]*)"|(?<plain>\S+))\s*$')
        if ($match.Success) {
            $target = if ($match.Groups['single'].Success) { $match.Groups['single'].Value } elseif ($match.Groups['double'].Success) { $match.Groups['double'].Value } else { $match.Groups['plain'].Value }
            if (-not [string]::IsNullOrWhiteSpace($target)) { $target }
        }
    }
}

$provenancePath = Join-Path $artifactRoot 'generated/provenance.json'
$siteMetadataPath = Join-Path $artifactRoot 'generated/site-metadata.json'
Assert-DocsSmoke (Test-Path -LiteralPath $provenancePath -PathType Leaf) 'Generated provenance metadata is missing.'
Assert-DocsSmoke (Test-Path -LiteralPath $siteMetadataPath -PathType Leaf) 'Generated site metadata is missing.'
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
$siteMetadata = Get-Content -LiteralPath $siteMetadataPath -Raw | ConvertFrom-Json
Assert-DocsSmoke ($provenance.commit -match '^[0-9a-f]{40}$') 'The repository revision is missing or malformed.'
$projectPath = Join-Path $RepoRoot 'src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
Assert-DocsSmoke ($siteMetadata.productVersion -eq $version) 'Site version does not match the LeanCorpus project version.'

$requiredContent = @(
    'index.md',
    'toc.yml',
    'articles/ADRs/index.md',
    'articles/ADRs/toc.yml',
    'articles/ADRs/ADR040-bounded-field-length-bodies.md',
    'articles/ADRs/ADR041-physical-index-runtime-lifecycle.md',
    'articles/ADRs/Server/ADR-S005-aot-and-framework-compatibility-boundary.md',
    'changelog/index.md',
    'changelog/toc.yml',
    'benchmarks/overview.md',
    'benchmarks/toc.yml'
)
foreach ($relativePath in $requiredContent) {
    Assert-DocsSmoke (Test-Path -LiteralPath (Join-Path $ContentDirectory $relativePath) -PathType Leaf) `
        "Required staged document is missing: $relativePath"
}
Assert-DocsSmoke (Test-Path -LiteralPath (Join-Path $SiteDirectory 'index.html') -PathType Leaf) 'The generated site homepage is missing.'
Assert-DocsSmoke (Test-Path -LiteralPath (Join-Path $SiteDirectory 'api/index.html') -PathType Leaf) 'The API landing page is missing.'

$markdownFiles = @(Get-ChildItem -LiteralPath $ContentDirectory -Recurse -File -Filter '*.md')
Assert-DocsSmoke ($markdownFiles.Count -gt 0) 'No staged Markdown pages were found.'
$expectedUrlPrefix = "https://github.com/jordansrowles/LeanCorpus/blob/$($provenance.commit)/"
foreach ($file in $markdownFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $frontMatter = [regex]::Match($text, '\A---\r?\n(?<yaml>[\s\S]*?)\r?\n---(?=\r?\n|$)')
    Assert-DocsSmoke $frontMatter.Success "Staged Markdown has no front matter: $($file.FullName)"
    $urlMatch = [regex]::Match($frontMatter.Groups['yaml'].Value, '(?m)^docurl:\s*''(?<url>[^'']+)''\s*$')
    Assert-DocsSmoke $urlMatch.Success "Staged Markdown has no canonical contribution URL: $($file.FullName)"
    $url = $urlMatch.Groups['url'].Value
    Assert-DocsSmoke ($url.StartsWith($expectedUrlPrefix, [StringComparison]::Ordinal)) `
        "Contribution URL does not use the staged revision: $($file.FullName)"
    Assert-DocsSmoke (-not $url.Contains('/artifacts/')) "Contribution URL points into generated artifacts: $url"
    $sourcePath = [Uri]::UnescapeDataString($url.Substring($expectedUrlPrefix.Length)).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    Assert-DocsSmoke (Test-Path -LiteralPath (Join-Path $RepoRoot $sourcePath) -PathType Leaf) `
        "Contribution URL target does not exist in the repository: $sourcePath"
}

$tocFiles = @(Get-ChildItem -LiteralPath $ContentDirectory -Recurse -File -Filter 'toc.yml')
Assert-DocsSmoke ($tocFiles.Count -gt 0) 'No generated navigation TOCs were found.'
$artifactPrefix = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($toc in $tocFiles) {
    foreach ($target in Get-TocTargets $toc.FullName) {
        if ($target -match '^[a-z][a-z0-9+.-]*:' -or $target.StartsWith('#')) { continue }
        $pathPart = ($target -split '[?#]', 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
        $decoded = [Uri]::UnescapeDataString($pathPart)
        $targetPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $toc.FullName -Parent) $decoded))
        Assert-DocsSmoke ($targetPath.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) `
            "TOC target escapes the documentation artefact tree: $($toc.FullName) -> $target"
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            $targetPath = [System.IO.Path]::GetFullPath((Join-Path $ApiDirectory $decoded))
        }
        Assert-DocsSmoke (Test-Path -LiteralPath $targetPath -PathType Leaf) `
            "TOC target is missing: $($toc.FullName) -> $target"
    }
}

$htmlFiles = @(Get-ChildItem -LiteralPath $SiteDirectory -Recurse -File -Filter '*.html')
Assert-DocsSmoke ($htmlFiles.Count -gt 0) 'No generated HTML pages were found.'
$checkedReferences = 0
$checkedFragments = 0
$anchorsByPage = @{}
$siteUri = [Uri]'https://docs.local/'
foreach ($file in $htmlFiles) {
    $html = [System.IO.File]::ReadAllText($file.FullName)
    $relativeHtml = [System.IO.Path]::GetRelativePath($SiteDirectory, $file.FullName).Replace('\\', '/')
    $htmlUri = [Uri]::new($siteUri, $relativeHtml)
    foreach ($match in [regex]::Matches($html, '(?is)\b(?:href|src)\s*=\s*["''](?<url>[^"'']+)["'']')) {
        $reference = [System.Net.WebUtility]::HtmlDecode($match.Groups['url'].Value.Trim())
        if ([string]::IsNullOrWhiteSpace($reference) -or $reference.StartsWith('#') -or $reference.StartsWith('data:', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $targetUri = [Uri]::new($htmlUri, $reference)
        if ($targetUri.Scheme -notin @('http', 'https') -or $targetUri.Authority -ne $siteUri.Authority) { continue }
        $relativeTarget = [Uri]::UnescapeDataString($targetUri.AbsolutePath.TrimStart('/'))
        if ([string]::IsNullOrWhiteSpace($relativeTarget)) { $relativeTarget = 'index.html' }
        $targetPath = [System.IO.Path]::GetFullPath((Join-Path $SiteDirectory $relativeTarget.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
        Assert-DocsSmoke ($targetPath.StartsWith([System.IO.Path]::GetFullPath($SiteDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
            "Generated HTML reference escapes the site: $($file.FullName) -> $reference"
        if (Test-Path -LiteralPath $targetPath -PathType Container) {
            $targetPath = Join-Path $targetPath 'index.html'
        }
        Assert-DocsSmoke (Test-Path -LiteralPath $targetPath -PathType Leaf) `
            "Generated HTML references a missing local page or asset: $($file.FullName) -> $reference"
        if (-not [string]::IsNullOrWhiteSpace($targetUri.Fragment) -and [System.IO.Path]::GetExtension($targetPath) -eq '.html') {
            $anchorKey = [System.IO.Path]::GetFullPath($targetPath)
            if (-not $anchorsByPage.ContainsKey($anchorKey)) {
                $targetHtml = [System.IO.File]::ReadAllText($anchorKey)
                $anchors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($anchorMatch in [regex]::Matches($targetHtml, '(?is)\b(?:id|name)\s*=\s*["''](?<id>[^"'']+)["'']')) {
                    [void]$anchors.Add([System.Net.WebUtility]::HtmlDecode($anchorMatch.Groups['id'].Value))
                }
                $anchorsByPage[$anchorKey] = $anchors
            }
            $fragment = [Uri]::UnescapeDataString($targetUri.Fragment.TrimStart('#'))
            Assert-DocsSmoke ($anchorsByPage[$anchorKey].Contains($fragment)) `
                "Generated HTML references a missing fragment: $($file.FullName) -> $reference"
            $checkedFragments++
        }
        $checkedReferences++
    }
}

Write-Host "Documentation site smoke passed: $($markdownFiles.Count) Markdown pages, $($tocFiles.Count) TOCs, $($htmlFiles.Count) HTML pages, $checkedReferences local references and $checkedFragments fragments."
