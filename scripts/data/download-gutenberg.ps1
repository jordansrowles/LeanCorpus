<#
.SYNOPSIS
    Downloads Project Gutenberg plain-text ebooks for benchmark testing.

.DESCRIPTION
    Downloads a curated set of public-domain books from Project Gutenberg
    into bench/data/gutenberg-ebooks/. Files are named by Gutenberg ID (e.g. 84.txt).

    When BookCount exceeds the curated seed list, the script downloads the
    Gutenberg catalogue CSV, caches it locally, and supplements from the
    catalogue (English texts, sorted by ID).

    Please be courteous to Gutenberg's servers. This script inserts a 1-second
    delay between requests. Do not run repeatedly in a short period of time.

.PARAMETER BookCount
    Number of books to download. Defaults to 200.
    When BookCount <= seed list size, the first N seeds are used.
    When BookCount > seed list size, the catalogue is consulted for the remainder.

.PARAMETER OutputDir
    Override the output directory. Defaults to bench/data/gutenberg-ebooks relative
    to the repository root.

.EXAMPLE
    .\scripts\download-gutenberg.ps1
    Downloads 200 books to the default location.

.EXAMPLE
    .\scripts\download-gutenberg.ps1 -BookCount 50
    Downloads the first 50 books from the seed list.

.EXAMPLE
    .\scripts\download-gutenberg.ps1 -BookCount 500 -OutputDir C:\data\books
    Downloads 500 books to a custom directory, supplementing from the catalogue.
#>
param(
    [int]$BookCount = 200,
    [string]$OutputDir = ''
)

if ($BookCount -lt 1) {
    throw [System.ArgumentOutOfRangeException]::new(
        'BookCount',
        $BookCount,
        'BookCount must be greater than zero.')
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "bench\data\gutenberg-ebooks"
}

$catalogCacheDir  = Join-Path $repoRoot "bench\data"
$catalogCachePath = Join-Path $catalogCacheDir ".gutenberg-catalog.csv"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $catalogCacheDir | Out-Null

# Curated seed list of public-domain books by Gutenberg ID.
# Covers a wide range of genres, authors, and writing styles.
$seedBooks = [ordered]@{
    9     = "Hamlet and works (Shakespeare)"
    11    = "Alice's Adventures in Wonderland (Carroll)"
    12    = "Through the Looking-Glass (Carroll)"
    16    = "Peter Pan (Barrie)"
    23    = "Narrative of the Life of Frederick Douglass (Douglass)"
    26    = "The Jungle (Sinclair)"
    35    = "The Time Machine (Wells)"
    43    = "The Strange Case of Dr Jekyll and Mr Hyde (Stevenson)"
    45    = "Anne of Green Gables (Montgomery)"
    46    = "A Christmas Carol (Dickens)"
    47    = "Treasure Island (Stevenson)"
    55    = "The Wonderful Wizard of Oz (Baum)"
    61    = "The Secret Garden (Burnett)"
    74    = "Adventures of Huckleberry Finn (Twain)"
    84    = "Frankenstein (Shelley)"
    98    = "A Tale of Two Cities (Dickens)"
    113   = "The Secret Sharer (Conrad)"
    158   = "Emma (Austen)"
    174   = "The Picture of Dorian Gray (Wilde)"
    209   = "The Turn of the Screw (James)"
    215   = "The Call of the Wild (London)"
    244   = "A Study in Scarlet (Doyle)"
    345   = "Dracula (Stoker)"
    514   = "Little Women (Alcott)"
    768   = "Wuthering Heights (Bronte)"
    776   = "What Is Man? (Twain)"
    829   = "Gulliver's Travels (Swift)"
    844   = "The Importance of Being Earnest (Wilde) - variant"
    863   = "Hamlet (Shakespeare)"
    1080  = "A Modest Proposal (Swift)"
    1184  = "The Count of Monte Cristo (Dumas)"
    1232  = "The Prince (Machiavelli)"
    1260  = "Jane Eyre (Bronte)"
    1342  = "Pride and Prejudice (Austen)"
    1399  = "Vanity Fair (Thackeray)"
    1400  = "Great Expectations (Dickens)"
    1497  = "The Republic (Plato)"
    1512  = "Romeo and Juliet (Shakespeare)"
    1513  = "Macbeth (Shakespeare)"
    1661  = "The Adventures of Sherlock Holmes (Doyle)"
    1952  = "The Yellow Wallpaper (Gilman)"
    2097  = "The War of the Worlds (Wells)"
    2148  = "Uncle Tom's Cabin (Stowe)"
    2542  = "A Doll's House (Ibsen)"
    2554  = "Crime and Punishment (Dostoevsky)"
    2591  = "Grimms' Fairy Tales"
    2701  = "Moby Dick (Melville)"
    2814  = "Dubliners (Joyce)"
    3207  = "Leviathan (Hobbes)"
    3268  = "The Mysterious Affair at Styles (Christie)"
    3825  = "The Cherry Orchard (Chekhov)"
    4280  = "The Life and Adventures of Robinson Crusoe (Defoe)"
    4300  = "Ulysses (Joyce)"
    4517  = "My Antonia (Cather)"
    5200  = "Metamorphosis (Kafka)"
    5348  = "Narrative of the Life of Frederick Douglass (variant)"
    5740  = "Siddhartha (Hesse)"
    5765  = "Flatland (Abbott)"
    6130  = "The Iliad (Homer)"
    6593  = "The Wind in the Willows (Grahame)"
    6761  = "The Adventures of Tom Sawyer (Twain)"
    7370  = "Walden (Thoreau)"
    7849  = "The Return of Sherlock Holmes (Doyle)"
    8800  = "Don Quixote (Cervantes)"
    10148 = "Sons and Lovers (Lawrence)"
    10676 = "The Scarlet Letter (Hawthorne)"
    11231 = "The Jungle Book (Kipling)"
    15399 = "The Awakening (Chopin)"
    16328 = "Beowulf"
    16389 = "My Man Jeeves (Wodehouse)"
    18328 = "The Island of Doctor Moreau (Wells)"
    19337 = "The Red Badge of Courage (Crane)"
    19942 = "The Scarlet Pimpernel (Orczy)"
    20203 = "Anna Karenina (Tolstoy)"
    22381 = "The Thirty-Nine Steps (Buchan)"
    25344 = "The Enchanted April (von Arnim)"
    27827 = "The Brothers Karamazov (Dostoevsky)"
    28054 = "The Brothers Karamazov v2 (Dostoevsky)"
    30254 = "The Portrait of a Lady (James)"
    36    = "The War of the Worlds (Wells) - alternate"
    42671 = "The Importance of Being Earnest (Wilde)"
}

function Get-CatalogIds {
    param(
        [int]$Count,
        [System.Collections.Generic.HashSet[string]]$ExcludeIds,
        [string]$CacheFile
    )

    if (-not (Test-Path $CacheFile)) {
        $catalogUrl = "https://www.gutenberg.org/cache/epub/feeds/pg_catalog.csv.gz"
        $gzPath     = $CacheFile + ".gz"

        Write-Host "Downloading Gutenberg catalogue..." -ForegroundColor Cyan
        try {
            Invoke-WebRequest -Uri $catalogUrl -OutFile $gzPath -UseBasicParsing `
                -UserAgent "Mozilla/5.0 (compatible; BenchmarkDataBot/1.0)"
        }
        catch {
            Write-Warning "Failed to download catalogue: $_"
            return @()
        }

        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $inStream  = [System.IO.File]::OpenRead($gzPath)
            $outStream = [System.IO.File]::Create($CacheFile)
            $gzip      = [System.IO.Compression.GZipStream]::new(
                $inStream, [System.IO.Compression.CompressionMode]::Decompress)
            $gzip.CopyTo($outStream)
        }
        finally {
            if ($gzip)      { $gzip.Dispose() }
            if ($outStream) { $outStream.Dispose() }
            if ($inStream)  { $inStream.Dispose() }
            if (Test-Path $gzPath) { Remove-Item $gzPath -Force }
        }
    }

    $rows = Import-Csv -Path $CacheFile
    $ids  = $rows |
        Where-Object { $_.'Language' -eq 'en' -and $_.'Text#' -match '^\d+$' } |
        Sort-Object  { [int]$_.'Text#' } |
        Where-Object { -not $ExcludeIds.Contains($_.'Text#') } |
        Select-Object -First $Count |
        ForEach-Object { [int]$_.'Text#' }

    return $ids
}

# Build the working list from the seed and (if needed) the catalogue.
$booksToDownload = [ordered]@{}

$seedIds = [System.Collections.Generic.List[int]]::new()
foreach ($id in $seedBooks.Keys) { $seedIds.Add([int]$id) }

if ($BookCount -le $seedIds.Count) {
    for ($i = 0; $i -lt $BookCount; $i++) {
        $id = $seedIds[$i]
        $booksToDownload[$id] = $seedBooks[$id]
    }
}
else {
    foreach ($id in $seedBooks.Keys) {
        $booksToDownload[$id] = $seedBooks[$id]
    }

    $needed    = $BookCount - $seedBooks.Count
    $excludeSet = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($id in $seedBooks.Keys) { $excludeSet.Add([string]$id) | Out-Null }

    $extraIds = Get-CatalogIds -Count $needed -ExcludeIds $excludeSet -CacheFile $catalogCachePath
    foreach ($id in $extraIds) {
        $booksToDownload[$id] = "Gutenberg Book #$id"
    }
}

$total     = $booksToDownload.Count
$succeeded = 0
$failed    = 0

Write-Host "Downloading $total books to: $OutputDir" -ForegroundColor Green
Write-Host ""

$index = 0
foreach ($id in $booksToDownload.Keys) {
    $index++
    $title      = $booksToDownload[$id]
    $outputPath = Join-Path $OutputDir "$id.txt"

    if (Test-Path $outputPath) {
        Write-Host "[$index/$total] Skipping $id ($title) - already exists" -ForegroundColor DarkGray
        $succeeded++
        continue
    }

    $urls = @(
        "https://www.gutenberg.org/files/$id/$id-0.txt",
        "https://www.gutenberg.org/files/$id/$id.txt",
        "https://www.gutenberg.org/cache/epub/$id/pg$id.txt"
    )

    $downloaded = $false
    foreach ($url in $urls) {
        try {
            Write-Host ("[{0}/{1}] Fetching {2}: {3}" -f $index, $total, $id, $title) -ForegroundColor Cyan
            Invoke-WebRequest -Uri $url -OutFile $outputPath -UseBasicParsing `
                -UserAgent "Mozilla/5.0 (compatible; BenchmarkDataBot/1.0)"
            Write-Host "  Saved $outputPath" -ForegroundColor Green
            $downloaded = $true
            $succeeded++
            break
        }
        catch {
            # Try next URL
        }
    }

    if (-not $downloaded) {
        Write-Host "  Failed to download $id ($title)" -ForegroundColor Red
        $failed++
    }

    Start-Sleep -Seconds 1
}

Write-Host ""
Write-Host "Complete: $succeeded downloaded, $failed failed." -ForegroundColor Yellow
Write-Host "Data in: $OutputDir"
