$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Clear-ApiMetadata {
    param([string]$DocsDir)

    $apiDir = Join-Path $DocsDir 'api'
    if (-not (Test-Path $apiDir)) {
        New-Item -ItemType Directory -Path $apiDir | Out-Null
        return
    }
    Get-ChildItem $apiDir -Filter '*.yml' -File | Remove-Item -Force
    $tocPath = Join-Path $apiDir 'toc.yml'
    if (Test-Path $tocPath) { Remove-Item $tocPath -Force }
}

function Set-GeneratedContent {
    param([string]$Path, [object]$Value)

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

function Remove-ExternalInheritedMembers {
    param([string]$DocsDir)

    $apiDir = Join-Path $DocsDir 'api'
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
                if ($Matches[1].StartsWith('Rowles.LeanCorpus.', [StringComparison]::Ordinal)) {
                    $keptMembers.Add($lines[$i])
                }
                $i++
            }
            if ($keptMembers.Count -gt 0) {
                $out.Add('  inheritedMembers:')
                $out.AddRange($keptMembers)
            }
            $i--
        }
        Set-GeneratedContent -Path $file.FullName -Value $out
    }
}

function Copy-Changelog {
    param(
        [string]$RepoRoot,
        [string]$DocsDir
    )

    $srcDir = Join-Path $RepoRoot 'changelog'
    $dstDir = Join-Path $DocsDir 'changelog'
    if (-not (Test-Path $srcDir)) {
        Write-Info 'No changelog directory found, skipping.'
        return
    }
    if (-not (Test-Path $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir | Out-Null
    }
    Remove-Item (Join-Path $dstDir '*.md') -Force -ErrorAction SilentlyContinue -Exclude 'index.md'
    Copy-Item (Join-Path $srcDir '*.md') -Destination $dstDir -Force -Exclude '_template.md', '_vnext.md'
    Write-Info 'Changelog files copied.'
}
