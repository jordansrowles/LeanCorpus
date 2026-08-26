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

function Invoke-DocfxWithDiagnostics {
    param(
        [ValidateSet('build', 'metadata')]
        [string]$Command,
        [string]$ConfigPath,
        [string]$LogPath
    )

    $logDirectory = Split-Path $LogPath -Parent
    if (-not (Test-Path $logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    }
    if (Test-Path $LogPath) {
        Remove-Item $LogPath -Force
    }

    $stdoutPath = "$LogPath.stdout"
    $stderrPath = "$LogPath.stderr"
    Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    Write-Info "Detailed DocFX diagnostics: $LogPath"
    $docfxPath = (Get-Command docfx -ErrorAction Stop).Source
    $process = Start-Process -FilePath $docfxPath `
        -ArgumentList @($Command, $ConfigPath, '--log', $LogPath, '--logLevel', 'warning') `
        -NoNewWindow `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $process.WaitForExit(30000)) {
        $minutes = [Math]::Floor($stopwatch.Elapsed.TotalMinutes)
        Write-Info ("  DocFX {0} still running ({1:00}:{2:00})..." -f $Command, $minutes, $stopwatch.Elapsed.Seconds)
    }
    $process.WaitForExit()
    $stopwatch.Stop()
    $exitCode = $process.ExitCode

    $consoleOutput = @()
    if (Test-Path $stdoutPath) { $consoleOutput += @(Get-Content $stdoutPath) }
    if (Test-Path $stderrPath) { $consoleOutput += @(Get-Content $stderrPath) }

    $records = @()
    if (Test-Path $LogPath) {
        $records = @(Get-Content $LogPath | ForEach-Object {
            try {
                $_ | ConvertFrom-Json
            } catch {
                # Ignore malformed diagnostic lines and retain the original log for inspection.
            }
        })
    }

    if ($exitCode -ne 0) {
        Write-Failure "DocFX $Command failed with exit code $exitCode."
        $consoleOutput | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
        Write-Info "Captured console output: $stdoutPath and $stderrPath"
        return $exitCode
    }

    Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    $warnings = @($records | Where-Object {
        $severity = $_.PSObject.Properties['severity']
        $severity -and $severity.Value -eq 'warning'
    })
    if ($warnings.Count -gt 0) {
        Write-Warn "DocFX $Command completed with $($warnings.Count) warning(s); detailed output was suppressed."
        $warningGroups = $warnings |
            Group-Object {
                $code = $_.PSObject.Properties['code']
                if ($code -and $code.Value) {
                    $code.Value
                } else {
                    $message = $_.PSObject.Properties['message']
                    if ($message -and $message.Value -match 'Duplicate source file') {
                        'DuplicateSourceFile'
                    } elseif ($message -and $message.Value -match '^Duplicate parameter') {
                        'DuplicateParameter'
                    } else {
                        'uncategorised'
                    }
                }
            } |
            Sort-Object Count -Descending
        foreach ($group in $warningGroups) {
            Write-Info ("  {0}: {1}" -f $group.Name, $group.Count)
        }
    } else {
        Write-Success "DocFX $Command completed without warnings."
    }

    return 0
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
    $existingFiles = @(Get-ChildItem -Path $dstDir -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'index.md' })
    foreach ($file in $existingFiles) {
        Remove-Item -LiteralPath $file.FullName -Force
    }

    $sourceFiles = @(Get-ChildItem -Path $srcDir -Recurse -File -Filter '*.md' |
        Where-Object { $_.Name -notin @('_template.md', '_vnext.md') })
    foreach ($file in $sourceFiles) {
        $relativePath = [System.IO.Path]::GetRelativePath($srcDir, $file.FullName)
        $destinationPath = Join-Path $dstDir $relativePath
        $destinationParent = Split-Path $destinationPath -Parent
        if (-not (Test-Path $destinationParent)) {
            New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
    Write-Info 'Changelog files copied.'
}

function Copy-RepositoryDocumentation {
    param(
        [string]$RepoRoot,
        [string]$DocsDir
    )

    $entries = @(
        @{ Source = 'README.md'; Destination = 'repository-overview.md' }
        @{ Source = 'CONTRIBUTING.md'; Destination = 'contributors/contributing.md' }
        @{ Source = 'docs/contributors/index.md'; Destination = 'contributors/index.md' }
        @{ Source = 'lexicons/README.md'; Destination = 'analysis/06-lexicons.md' }
        @{ Source = 'src/core/Rowles.Text/README.md'; Destination = 'analysis/08-rowles-text.md' }
        @{ Source = 'src/core/Rowles.Text/CONTRIBUTING.md'; Destination = 'contributors/rowles-text.md' }
        @{ Source = 'src/core/Rowles.LeanCorpus.SourceGen/README.md'; Destination = 'getting-started/04-source-generated-mapping.md' }
        @{ Source = 'src/core/Rowles.LeanCorpus.Compression.LZ4/README.md'; Destination = 'tips/compression/lz4.md' }
        @{ Source = 'src/core/Rowles.LeanCorpus.Compression.Snappy/README.md'; Destination = 'tips/compression/snappy.md' }
        @{ Source = 'src/core/Rowles.LeanCorpus.Compression.Zstandard/README.md'; Destination = 'tips/compression/zstandard.md' }
        @{ Source = 'src/examples/README.md'; Destination = 'examples/index.md' }
        @{ Source = 'src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch/README.md'; Destination = 'examples/linux-kernel-code-search.md' }
        @{ Source = 'src/devops/README.md'; Destination = 'contributors/devops-projects.md' }
        @{ Source = 'src/devops/CONTRIBUTING.md'; Destination = 'contributors/devops-and-tests.md' }
        @{ Source = 'src/server/README.md'; Destination = 'contributors/server-proof-of-concept.md' }
    )

    $stagingDir = Join-Path $DocsDir '.generated'
    if (Test-Path $stagingDir) {
        Remove-Item $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingDir | Out-Null

    $destinationBySource = @{}
    $sourceByDestination = @{}
    foreach ($entry in $entries) {
        $sourcePath = Join-Path $RepoRoot $entry.Source
        if (-not (Test-Path $sourcePath -PathType Leaf)) {
            throw "Repository documentation source not found: $($entry.Source)"
        }
        $normalisedSource = [System.IO.Path]::GetFullPath($sourcePath)
        if ($destinationBySource.ContainsKey($normalisedSource)) {
            throw "Repository documentation source is mapped more than once: $($entry.Source)"
        }
        if ($sourceByDestination.ContainsKey($entry.Destination)) {
            throw "Repository documentation destination is mapped more than once: $($entry.Destination)"
        }
        $destinationBySource[$normalisedSource] = $entry.Destination
        $sourceByDestination[$entry.Destination] = $normalisedSource
    }

    foreach ($entry in $entries) {
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $entry.Source))
        $destinationPath = Join-Path $stagingDir $entry.Destination
        $destinationDirectory = Split-Path $destinationPath -Parent
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

        $content = [System.IO.File]::ReadAllText($sourcePath)
        $content = [regex]::Replace($content, '(?m)^```mermaid\s*$', '```mermaid-latest')

        $linkPattern = '(?<prefix>!?\[[^\]]*\]\()(?<target>[^)]+)(?<suffix>\))'
        $content = [regex]::Replace($content, $linkPattern, {
            param($match)

            $target = $match.Groups['target'].Value
            if ($target.StartsWith('#') -or $target -match '^[a-z][a-z0-9+.-]*:') {
                return $match.Value
            }

            $pathPart, $fragment = if ($target.Contains('#')) {
                $parts = $target.Split('#', 2)
                $parts[0], "#$($parts[1])"
            } else {
                $target, ''
            }

            $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $sourcePath -Parent) $pathPart))
            $mappedDestination = $destinationBySource[$resolvedTarget]
            if (-not $mappedDestination -and (Test-Path $resolvedTarget -PathType Container)) {
                $readmePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedTarget 'README.md'))
                $mappedDestination = $destinationBySource[$readmePath]
            }

            if ($mappedDestination) {
                $siteTarget = Join-Path $stagingDir $mappedDestination
                $rewritten = [System.IO.Path]::GetRelativePath($destinationDirectory, $siteTarget).Replace('\', '/')
                return "$($match.Groups['prefix'].Value)$rewritten$fragment$($match.Groups['suffix'].Value)"
            }

            $docsRootWithSeparator = $DocsDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            if ($resolvedTarget.StartsWith($docsRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
                $rewritten = [System.IO.Path]::GetRelativePath($destinationDirectory, $resolvedTarget).Replace('\', '/')
                return "$($match.Groups['prefix'].Value)$rewritten$fragment$($match.Groups['suffix'].Value)"
            }

            if (-not (Test-Path $resolvedTarget)) {
                throw "Unresolved repository documentation link '$target' in '$($entry.Source)'."
            }

            $repoRelative = [System.IO.Path]::GetRelativePath($RepoRoot, $resolvedTarget).Replace('\', '/')
            $githubKind = if (Test-Path $resolvedTarget -PathType Container) { 'tree' } else { 'blob' }
            $githubTarget = "https://github.com/jordansrowles/LeanCorpus/$githubKind/main/$repoRelative$fragment"
            return "$($match.Groups['prefix'].Value)$githubTarget$($match.Groups['suffix'].Value)"
        })

        $sourceUrl = "https://github.com/jordansrowles/LeanCorpus/blob/main/$($entry.Source)"
        $notice = "> [!NOTE]`n> This page is generated from [$($entry.Source)]($sourceUrl). Edit the repository file, not this copy.`n`n"
        Set-GeneratedContent -Path $destinationPath -Value ($notice + $content.TrimStart())
    }

    Write-Info "Repository documentation copied: $($entries.Count) pages."
}
