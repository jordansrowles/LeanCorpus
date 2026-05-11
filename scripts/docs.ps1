<#
.SYNOPSIS
    Sets up and builds the LeanCorpus documentation site using DocFX.

.DESCRIPTION
    Ensures the docfx global tool is installed, then generates API metadata
    and builds the static site into ./docs/site.

.PARAMETER Serve
    After building, start a local web server at http://localhost:8080.

.PARAMETER MetadataOnly
    Only generate API metadata (docs/api/*.yml); skip the site build.

.PARAMETER SkipBenchmarks
    Skip benchmark doc generation (faster builds when bench data has not changed).

.EXAMPLE
    .\scripts\docs.ps1
    Builds the documentation site into ./docs/site.

.EXAMPLE
    .\scripts\docs.ps1 -Serve
    Builds the site and serves it locally on http://localhost:8080.

.EXAMPLE
    .\scripts\docs.ps1 -MetadataOnly
    Generates API YAML metadata without building the full site.

.EXAMPLE
    .\scripts\docs.ps1 -SkipBenchmarks
    Builds the site without regenerating benchmark pages.

.EXAMPLE
    .\scripts\docs.ps1 -SkipCoverage
    Builds the site without regenerating the coverage report.
#>
param(
    [switch]$Serve,
    [switch]$MetadataOnly,
    [switch]$SkipBenchmarks,
    [switch]$SkipCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$docsDir  = Join-Path $repoRoot "docs"
$docfxJson = Join-Path $docsDir "docfx.json"

function Clear-ApiMetadata {
    $apiDir = Join-Path $docsDir "api"
    if (-not (Test-Path $apiDir)) {
        New-Item -ItemType Directory -Path $apiDir | Out-Null
        return
    }

    Get-ChildItem $apiDir -Filter '*.yml' -File | Remove-Item -Force
    $tocPath = Join-Path $apiDir 'toc.yml'
    if (Test-Path $tocPath) {
        Remove-Item $tocPath -Force
    }
}

function Set-GeneratedContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Value
    )

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Set-Content -Path $Path -Value $Value -Encoding utf8
            return
        } catch [System.IO.IOException] {
            if ($attempt -eq 5) { throw }
            Start-Sleep -Milliseconds (100 * $attempt)
        }
    }
}

function Add-InternalApiBadges {
    $apiDir = Join-Path $docsDir "api"
    if (-not (Test-Path $apiDir)) { return }

    $lock = [char]::ConvertFromUtf32(0x1F512)
    $internalUids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }

        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.AddRange([string[]](Get-Content $file.FullName))
        $currentUid = $null

        foreach ($line in $lines) {
            if ($line -match '^- uid: (.+)$') {
                $currentUid = $Matches[1]
                continue
            }

            if ($currentUid -and $line -match '^\s+content(?:\.vb)?: .*\b(internal|Friend)\b') {
                [void]$internalUids.Add($currentUid)
            }
        }
    }

    if ($internalUids.Count -eq 0) { return }

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }

        $lines = [string[]](Get-Content $file.FullName)
        $out = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $out.Add($lines[$i])

            if ($lines[$i] -match '^- uid: (.+)$' -and $internalUids.Contains($Matches[1])) {
                if ($i + 1 -ge $lines.Length -or $lines[$i + 1] -ne '  apiAccessOverride: internal') {
                    $out.Add('  apiAccessOverride: internal')
                }
            }
        }

        Set-GeneratedContent -Path $file.FullName -Value $out
    }

    $tocPath = Join-Path $apiDir 'toc.yml'
    if (-not (Test-Path $tocPath)) { return }

    $tocLines = [System.Collections.Generic.List[string]]::new()
    $tocLines.AddRange([string[]](Get-Content $tocPath))
    $currentUid = $null
    for ($i = 0; $i -lt $tocLines.Count; $i++) {
        if ($tocLines[$i] -match '^\s+- uid: (.+)$') {
            $currentUid = $Matches[1]
            continue
        }

        if ($currentUid -and $internalUids.Contains($currentUid) -and
            $tocLines[$i] -match '^(\s+name: )(.+)$' -and
            $tocLines[$i] -notmatch [regex]::Escape($lock)) {
            $tocLines[$i] = "$($Matches[1])$($Matches[2]) $lock"
        }
    }

    Set-GeneratedContent -Path $tocPath -Value $tocLines
}

function Remove-PrivateApiEntries {
    $apiDir = Join-Path $docsDir "api"
    if (-not (Test-Path $apiDir)) { return }

    $privateUids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $fileBlocks = @{}

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }

        $lines = [string[]](Get-Content $file.FullName)
        $prefix = [System.Collections.Generic.List[string]]::new()
        $blocks = [System.Collections.Generic.List[object]]::new()
        $references = [System.Collections.Generic.List[string]]::new()
        $current = $null
        $inReferences = $false

        foreach ($line in $lines) {
            if ($line -eq 'references:') {
                if ($current -is [System.Collections.Generic.List[string]]) {
                    $blocks.Add($current)
                    $current = $null
                }

                $inReferences = $true
                $references.Add($line)
                continue
            }

            if ($inReferences) {
                $references.Add($line)
                continue
            }

            if ($line -match '^- uid: (.+)$') {
                if ($current -is [System.Collections.Generic.List[string]]) {
                    $blocks.Add($current)
                }

                $current = [System.Collections.Generic.List[string]]::new()
                $current.Add($line)
                continue
            }

            if ($current -is [System.Collections.Generic.List[string]]) {
                $current.Add($line)
            } else {
                $prefix.Add($line)
            }
        }

        if ($current -is [System.Collections.Generic.List[string]]) {
            $blocks.Add($current)
        }

        foreach ($block in $blocks) {
            $uid = $null
            foreach ($line in $block) {
                if ($line -match '^- uid: (.+)$') {
                    $uid = $Matches[1]
                    continue
                }

                if ($uid -and $line -match '^\s+content(?:\.vb)?: .*\b(private|Private)\b') {
                    [void]$privateUids.Add($uid)
                }
            }
        }

        $fileBlocks[$file.FullName] = [pscustomobject]@{
            Prefix = $prefix
            Blocks = $blocks
            References = $references
        }
    }

    if ($privateUids.Count -eq 0) { return }

    foreach ($entry in $fileBlocks.GetEnumerator()) {
        $out = [System.Collections.Generic.List[string]]::new()
        $out.AddRange([string[]]$entry.Value.Prefix)

        foreach ($block in $entry.Value.Blocks) {
            $uid = $null
            foreach ($line in $block) {
                if ($line -match '^- uid: (.+)$') {
                    $uid = $Matches[1]
                    break
                }
            }

            if ($uid -and $privateUids.Contains($uid)) {
                continue
            }

            foreach ($line in $block) {
                if ($line -notmatch '^\s+- (.+)$' -or -not $privateUids.Contains($Matches[1])) {
                    $out.Add($line)
                }
            }
        }

        $out.AddRange([string[]]$entry.Value.References)
        Set-GeneratedContent -Path $entry.Key -Value $out
    }

    $tocPath = Join-Path $apiDir 'toc.yml'
    if (-not (Test-Path $tocPath)) { return }

    $tocLines = [string[]](Get-Content $tocPath)
    $outToc = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $tocLines.Length; $i++) {
        if ($tocLines[$i] -match '^(\s*)- uid: (.+)$' -and $privateUids.Contains($Matches[2])) {
            $indent = $Matches[1].Length
            $i++
            while ($i -lt $tocLines.Length) {
                if ($tocLines[$i] -match '^(\s*)- uid: ' -and $Matches[1].Length -le $indent) {
                    $i--
                    break
                }
                $i++
            }
            continue
        }

        $outToc.Add($tocLines[$i])
    }

    Set-GeneratedContent -Path $tocPath -Value $outToc
}

function Remove-ExternalInheritedMembers {
    $apiDir = Join-Path $docsDir "api"
    if (-not (Test-Path $apiDir)) { return }

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }

        $lines = [string[]](Get-Content $file.FullName)
        $out = [System.Collections.Generic.List[string]]::new()

        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -ne '  inheritedMembers:') {
                $out.Add($lines[$i])
                continue
            }

            $keptMembers = [System.Collections.Generic.List[string]]::new()
            $i++
            while ($i -lt $lines.Length -and $lines[$i] -match '^  - (.+)$') {
                if ($Matches[1].StartsWith('Rowles.LeanCorpus.', [System.StringComparison]::Ordinal)) {
                    $keptMembers.Add($lines[$i])
                }
                $i++
            }

            if ($keptMembers.Count -gt 0) {
                $out.Add('  inheritedMembers:')
                $out.AddRange([string[]]$keptMembers)
            }

            $i--
        }

        Set-GeneratedContent -Path $file.FullName -Value $out
    }
}

function Remove-ExternalInheritanceEntries {
    $apiDir = Join-Path $docsDir "api"
    if (-not (Test-Path $apiDir)) { return }

    $listNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    [void]$listNames.Add('inheritance')
    [void]$listNames.Add('implements')
    [void]$listNames.Add('derivedClasses')

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }

        $lines = [string[]](Get-Content $file.FullName)
        $out = [System.Collections.Generic.List[string]]::new()

        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -notmatch '^  ([A-Za-z]+):$' -or -not $listNames.Contains($Matches[1])) {
                $out.Add($lines[$i])
                continue
            }

            $listName = $Matches[1]
            $keptMembers = [System.Collections.Generic.List[string]]::new()
            $i++
            while ($i -lt $lines.Length -and $lines[$i] -match '^  - (.+)$') {
                if ($Matches[1].StartsWith('Rowles.LeanCorpus.', [System.StringComparison]::Ordinal)) {
                    $keptMembers.Add($lines[$i])
                }
                $i++
            }

            if ($keptMembers.Count -gt 0) {
                $out.Add("  ${listName}:")
                $out.AddRange([string[]]$keptMembers)
            }

            $i--
        }

        Set-GeneratedContent -Path $file.FullName -Value $out
    }
}

function Remove-ExternalReferenceEntries {
    $apiDir = Join-Path $docsDir "api"
    if (-not (Test-Path $apiDir)) { return }

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }

        $lines = [string[]](Get-Content $file.FullName)
        $out = [System.Collections.Generic.List[string]]::new()
        $inReferences = $false
        $currentBlock = $null
        $currentUid = $null
        $referencesAdded = $false

        foreach ($line in $lines) {
            if (-not $inReferences) {
                if ($line -eq 'references:') {
                    $inReferences = $true
                    continue
                }

                $out.Add($line)
                continue
            }

            if ($line -match '^- uid: (.+)$') {
                if ($currentBlock -is [System.Collections.Generic.List[string]] -and
                    $currentUid.StartsWith('Rowles.LeanCorpus.', [System.StringComparison]::Ordinal)) {
                    if (-not $referencesAdded) {
                        $out.Add('references:')
                        $referencesAdded = $true
                    }
                    $out.AddRange([string[]]$currentBlock)
                }

                $currentBlock = [System.Collections.Generic.List[string]]::new()
                $currentBlock.Add($line)
                $currentUid = $Matches[1]
                continue
            }

            if ($currentBlock -is [System.Collections.Generic.List[string]]) {
                $currentBlock.Add($line)
            }
        }

        if ($currentBlock -is [System.Collections.Generic.List[string]] -and
            $currentUid.StartsWith('Rowles.LeanCorpus.', [System.StringComparison]::Ordinal)) {
            if (-not $referencesAdded) {
                $out.Add('references:')
                $referencesAdded = $true
            }
            $out.AddRange([string[]]$currentBlock)
        }

        Set-GeneratedContent -Path $file.FullName -Value $out
    }
}

# ── Ensure docfx is available ─────────────────────────────────────────────────

if (-not (Get-Command docfx -ErrorAction SilentlyContinue)) {
    Write-Host "Installing docfx global tool..." -ForegroundColor Cyan
    dotnet tool install -g docfx
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to install docfx. Ensure the .NET SDK is on PATH."
        exit 1
    }
    Write-Host "docfx installed." -ForegroundColor Green
} else {
    $version = @(docfx --version 2>&1)[0]
    Write-Host "docfx found: $version" -ForegroundColor DarkGray
}

# ── Generate benchmark docs ───────────────────────────────────────────────────

if (-not $SkipBenchmarks) {
    Write-Host "Generating benchmark pages..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'generate-benchmark-docs.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Benchmark doc generation failed (exit $LASTEXITCODE). Continuing without benchmark pages."
    }
}

# ── Generate coverage report ──────────────────────────────────────────────────

if (-not $SkipCoverage) {
    $coverageResultsDir = Join-Path $repoRoot "coverage-results"
    $coverageOutDir     = Join-Path $docsDir "coverage"
    $xmlFiles = @(Get-ChildItem $coverageResultsDir -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)

    if ($xmlFiles.Count -eq 0) {
        Write-Warning "No coverage XML files found in $coverageResultsDir. Skipping coverage report. Run scripts\coverage.ps1 first."
    } else {
        if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
            Write-Host "Installing reportgenerator global tool..." -ForegroundColor Cyan
            dotnet tool install -g dotnet-reportgenerator-globaltool
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to install reportgenerator. Ensure the .NET SDK is on PATH."
                exit 1
            }
            Write-Host "reportgenerator installed." -ForegroundColor Green
        } else {
            $rgVersion = @(reportgenerator --version 2>&1)[0]
            Write-Host "reportgenerator found: $rgVersion" -ForegroundColor DarkGray
        }

        $reportPaths = ($xmlFiles | ForEach-Object { $_.FullName }) -join ';'
        Write-Host "Generating coverage report..." -ForegroundColor Cyan

        reportgenerator `
            "-reports:$reportPaths" `
            "-targetdir:$coverageOutDir" `
            "-reporttypes:Html" `
            "-title:LeanCorpus Coverage"

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Coverage report generation failed (exit $LASTEXITCODE). Continuing without coverage report."
        } else {
            Write-Host "Coverage report written to: $coverageOutDir" -ForegroundColor Green
        }
    }
}

# ── Build ─────────────────────────────────────────────────────────────────────

if ($MetadataOnly) {
    Write-Host "Generating API metadata..." -ForegroundColor Cyan
    Clear-ApiMetadata
    docfx metadata $docfxJson
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Remove-ExternalInheritedMembers
    Remove-ExternalInheritanceEntries
    Remove-PrivateApiEntries
    Remove-ExternalReferenceEntries
    Add-InternalApiBadges
    Write-Host "Metadata written to: $docsDir\api" -ForegroundColor Green
    exit 0
}

Write-Host "Generating API metadata..." -ForegroundColor Cyan
Clear-ApiMetadata
docfx metadata $docfxJson
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-ExternalInheritedMembers
Remove-ExternalInheritanceEntries
Remove-PrivateApiEntries
Remove-ExternalReferenceEntries
Add-InternalApiBadges

if ($Serve) {
    Write-Host "Building and serving docs on http://localhost:8080..." -ForegroundColor Cyan
    docfx build $docfxJson --serve
} else {
    Write-Host "Building documentation site..." -ForegroundColor Cyan
    docfx build $docfxJson
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Site written to: $docsDir\site" -ForegroundColor Green
}
