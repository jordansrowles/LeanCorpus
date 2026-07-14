<#
.SYNOPSIS
    Downloads Wikipedia article introductions for benchmark testing.

.DESCRIPTION
    Uses the Wikipedia MediaWiki API to download the introductory text of randomly
    selected Wikipedia articles as plain-text .txt files in
    bench/data/wikipedia/{language}/. Each article is written to its own file,
    named with a random GUID (e.g. 7a3b2c1d-...txt).

    Specify a BCP 47 language code (e.g. en, fr, de, zh, ja) to target that
    language edition of Wikipedia. All language editions use the same API.

    Note: The Wikimedia abstract dump format was discontinued in 2024. This script
    uses the API's random-page generator with plaintext extracts instead.

    API rate: ~50 articles per request, 500ms between requests. Polite and compliant
    with Wikimedia's API usage policy.

    Licence: Article text is CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/)

.PARAMETER Language
    BCP 47 language code. Defaults to "en". Sets the Wikipedia language edition
    to download from (e.g. "fr" for fr.wikipedia.org).

.PARAMETER OutputDir
    Override the output directory. Defaults to bench/data/wikipedia/{language} relative
    to the repository root.

.PARAMETER ArticleCount
    Total number of articles to download. Default: 5000.

.EXAMPLE
    .\scripts\download-wikipedia.ps1
    Downloads 5,000 English article introductions, one file per article.

.EXAMPLE
    .\scripts\download-wikipedia.ps1 -Language fr -ArticleCount 2000
    Downloads 2,000 French article introductions, one file per article.

.EXAMPLE
    .\scripts\download-wikipedia.ps1 -Language zh -ArticleCount 10000
    Downloads 10,000 Chinese article introductions, one file per article.
#>
param(
    [string]$Language = 'en',
    [string]$OutputDir = '',
    [int]$ArticleCount = 5000
)

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "bench\data\wikipedia\$Language"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$apiBase  = "https://${Language}.wikipedia.org/w/api.php"
$headers  = @{ 'Api-User-Agent' = "BenchmarkDataBot/1.0 ($Language benchmark testing; non-commercial)" }
$batchSize = 50  # max allowed by the random generator

$totalFetched = 0

Write-Host "Downloading $ArticleCount Wikipedia ($Language) article introductions..." -ForegroundColor Cyan
Write-Host "API:    $apiBase"
Write-Host "Output: $OutputDir"
Write-Host ""

while ($totalFetched -lt $ArticleCount) {
    $limit = [Math]::Min($batchSize, $ArticleCount - $totalFetched)

    $url = "$apiBase`?action=query&generator=random&grnnamespace=0&grnlimit=$limit" +
           "&prop=extracts&exintro=1&explaintext=1&exsectionformat=plain" +
           "&format=json&formatversion=2"

    try {
        $response = Invoke-RestMethod -Uri $url -Headers $headers -UseBasicParsing
        $pages    = $response.query.pages

        foreach ($page in $pages) {
            $extract = ($page.extract ?? '').Trim()
            if ($extract.Length -gt 50) {
                # Write each article to its own GUID-named file: title on line 1, extract on line 2
                $outFile = Join-Path $OutputDir "$([guid]::NewGuid()).txt"
                "$($page.title)`n$extract" | Set-Content -Path $outFile -NoNewline
                $totalFetched++
            }
        }

        Write-Host "  $totalFetched / $ArticleCount articles..." -ForegroundColor DarkGray
    }
    catch {
        Write-Host "  Request failed: $_" -ForegroundColor Red
    }

    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "Complete: $totalFetched articles." -ForegroundColor Yellow
Write-Host "Data in: $OutputDir"
