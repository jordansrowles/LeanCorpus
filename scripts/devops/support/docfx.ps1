$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Clear-ApiMetadata {
    param([string]$ApiDir)
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
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnet
    $startInfo.WorkingDirectory = Get-RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('tool', 'run', 'docfx', '--', $Command, $ConfigPath, '--log', $LogPath, '--logLevel', 'warning')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Could not start the repository-pinned DocFX tool through dotnet.'
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $process.WaitForExit(30000)) {
        $minutes = [Math]::Floor($stopwatch.Elapsed.TotalMinutes)
        Write-Info ("  DocFX {0} still running ({1:00}:{2:00})..." -f $Command, $minutes, $stopwatch.Elapsed.Seconds)
    }
    $process.WaitForExit()
    $stopwatch.Stop()
    $exitCode = $process.ExitCode

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($stdoutPath, $stdoutTask.GetAwaiter().GetResult(), $utf8NoBom)
    [System.IO.File]::WriteAllText($stderrPath, $stderrTask.GetAwaiter().GetResult(), $utf8NoBom)
    $process.Dispose()

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
    param([string]$ApiDir)
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

function ConvertTo-DocsYamlString {
    param([string]$Value)
    return "'$($Value.Replace("'", "''"))'"
}

function Get-DocsMarkdownPages {
    param(
        [string]$Directory,
        [switch]$IncludeIndexPages
    )

    $root = [System.IO.Path]::GetFullPath($Directory)
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.md' | Sort-Object FullName) {
        if ($file.Name -eq '_template.md' -or ($file.Name -eq 'index.md' -and -not $IncludeIndexPages)) { continue }
        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $heading = Get-Content -LiteralPath $file.FullName -Encoding UTF8 |
            Where-Object { $_ -match '^#\s+(.+?)\s*$' } |
            Select-Object -First 1
        $title = if ($heading -match '^#\s+(.+?)\s*$') { $Matches[1].Trim() } else { $file.BaseName }
        $encodedPath = ($relative -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }
        [pscustomobject]@{
            RelativePath = $relative
            Link = $encodedPath -join '/'
            Title = $title
            FileName = $file.Name
        }
    }
}

function New-DocsSectionCatalog {
    param(
        [string]$Directory,
        [string]$Title,
        [string]$Description,
        [string]$IndexFile = 'index.md',
        [switch]$IncludeIndexPages,
        [switch]$SeparateServerChangelog
    )

    $Directory = [System.IO.Path]::GetFullPath($Directory)
    [void][System.IO.Directory]::CreateDirectory($Directory)
    $indexPath = Join-Path $Directory $IndexFile
    $tocPath = Join-Path $Directory 'toc.yml'
    if ((Test-Path -LiteralPath $indexPath) -or (Test-Path -LiteralPath $tocPath)) {
        throw "Generated $Title catalogue collides with an existing staged file in '$Directory'."
    }

    $pages = @(Get-DocsMarkdownPages -Directory $Directory -IncludeIndexPages:$IncludeIndexPages)
    $markdown = [System.Collections.Generic.List[string]]::new()
    $markdown.Add("# $Title")
    $markdown.Add('')
    $markdown.Add($Description)
    $markdown.Add('')
    $toc = [System.Collections.Generic.List[string]]::new()
    $toc.Add("- name: $(ConvertTo-DocsYamlString $Title)")
    $toc.Add('  href: index.md')
    $toc.Add('  items:')

    $groups = if ($SeparateServerChangelog) {
        @(
            [pscustomobject]@{ Name = 'LeanCorpus and Rowles.Text'; Pages = @($pages | Where-Object { -not $_.RelativePath.StartsWith('server/', [StringComparison]::OrdinalIgnoreCase) -and -not $_.RelativePath.StartsWith('devops/', [StringComparison]::OrdinalIgnoreCase) }) }
            [pscustomobject]@{ Name = 'Community Server'; Pages = @($pages | Where-Object { $_.RelativePath.StartsWith('server/', [StringComparison]::OrdinalIgnoreCase) }) }
            [pscustomobject]@{ Name = 'LeanCorpus DevOps'; Pages = @($pages | Where-Object { $_.RelativePath.StartsWith('devops/', [StringComparison]::OrdinalIgnoreCase) }) }
        )
    } else {
        @([pscustomobject]@{ Name = $Title; Pages = $pages })
    }

    foreach ($group in $groups) {
        if ($group.Pages.Count -eq 0) { continue }
        if ($SeparateServerChangelog) {
            $markdown.Add("## $($group.Name)")
            $markdown.Add('')
            $toc.Add("    - name: $(ConvertTo-DocsYamlString $group.Name)")
            $toc.Add('      items:')
        }

        $orderedPages = if ($Title -eq 'Changelog') {
            @($group.Pages | Sort-Object {
                if ($_.FileName -match '^(?<version>\d+)\.(?<minor>\d+)\.(?<patch>\d+)') {
                    '{0:D4}.{1:D4}.{2:D4}' -f [int]$Matches.version, [int]$Matches.minor, [int]$Matches.patch
                } elseif ($_.FileName -match '^unreleased\.md$') {
                    '9999.9999.9999'
                } else {
                    '0000.0000.0000'
                }
            } -Descending)
        } else {
            @($group.Pages | Sort-Object Title, RelativePath)
        }

        foreach ($page in $orderedPages) {
            $markdown.Add("- [$($page.Title)]($($page.Link))")
            $pageIndent = if ($SeparateServerChangelog) { '        ' } else { '    ' }
            $toc.Add("${pageIndent}- name: $(ConvertTo-DocsYamlString $page.Title)")
            $toc.Add("${pageIndent}  href: $(ConvertTo-DocsYamlString $page.RelativePath)")
        }
        $markdown.Add('')
    }

    Set-GeneratedContent -Path $indexPath -Value $markdown
    Set-GeneratedContent -Path $tocPath -Value $toc
    Write-Info "Generated $Title index and TOC for $($pages.Count) pages."
}

function Get-EvaluatedProjectVersion {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath
    )

    $fullProjectPath = Join-Path $RepoRoot ($ProjectPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $fullProjectPath -PathType Leaf)) {
        throw "Version source project not found: $fullProjectPath"
    }
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    Push-Location $RepoRoot
    try {
        $output = @(& $dotnet.Source msbuild $fullProjectPath '-getProperty:Version' '-nologo' 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($exitCode -ne 0) {
        throw "Could not evaluate Version from '$ProjectPath': $($output -join [Environment]::NewLine)"
    }

    $versions = @($output | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -match '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$' })
    if ($versions.Count -ne 1) {
        throw "Expected one evaluated SemVer Version from '$ProjectPath'; received: $($output -join [Environment]::NewLine)"
    }
    return $versions[0]
}

function Write-DocsSiteMetadata {
    param([string]$RepoRoot)

    $projectPath = 'src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj'
    $version = Get-EvaluatedProjectVersion -RepoRoot $RepoRoot -ProjectPath $projectPath
    $devOpsProjectPath = 'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj'
    $devOpsProject = Join-Path $RepoRoot ($devOpsProjectPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $devOpsVersion = if (Test-Path -LiteralPath $devOpsProject -PathType Leaf) {
        Get-EvaluatedProjectVersion -RepoRoot $RepoRoot -ProjectPath $devOpsProjectPath
    } else {
        $null
    }

    $commit = (& git -C $RepoRoot rev-parse --verify HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Could not resolve the current Git commit for documentation provenance.'
    }
    $commit = $commit.ToLowerInvariant()
    $dirtyOutput = @(& git -C $RepoRoot status --porcelain --untracked-files=no 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Git status for documentation provenance.' }
    $dirty = $dirtyOutput.Count -gt 0

    $provenance = [ordered]@{
        schemaVersion = 1
        productVersion = $version
        devOpsVersion = $devOpsVersion
        repository = 'https://github.com/jordansrowles/LeanCorpus'
        commit = $commit
        commitUrl = "https://github.com/jordansrowles/LeanCorpus/commit/$commit"
        workingTreeDirty = $dirty
    }
    $metadata = [ordered]@{
        _appName = "LeanCorpus $version"
        _appTitle = "LeanCorpus $version"
        _appFooter = 'LeanCorpus: a fast, embeddable full-text search engine for .NET'
        _enableSearch = $true
        categoryLayout = 'nested'
        productVersion = $version
        devOpsVersion = $devOpsVersion
        repositoryCommit = $commit
        repositoryUrl = 'https://github.com/jordansrowles/LeanCorpus'
        sourceRevisionUrl = "https://github.com/jordansrowles/LeanCorpus/commit/$commit"
    }

    $generatedRoot = Get-DocsArtifactPath -Name generated -RepoRoot $RepoRoot
    [void][System.IO.Directory]::CreateDirectory($generatedRoot)
    Set-GeneratedContent -Path (Join-Path $generatedRoot 'provenance.json') -Value ($provenance | ConvertTo-Json -Depth 5)
    Set-GeneratedContent -Path (Join-Path $generatedRoot 'site-metadata.json') -Value ($metadata | ConvertTo-Json -Depth 5)
    Write-Info "Documentation version $version is derived from $projectPath."
}

function Set-DocsContributionMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ContentDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $repoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $contentRoot = [System.IO.Path]::GetFullPath($ContentDirectory)
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $importsByDestination = @{}
    foreach ($entry in @($manifest.imports)) {
        $importsByDestination[[string]$entry.destination] = [string]$entry.source
    }

    $commit = (& git -C $repoRoot rev-parse --verify HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Could not resolve the current Git commit for documentation contribution links.'
    }
    $commit = $commit.ToLowerInvariant()
    $repoPrefix = 'https://github.com/jordansrowles/LeanCorpus/blob/'

    foreach ($file in Get-ChildItem -LiteralPath $contentRoot -Recurse -File -Filter '*.md') {
        $relativePath = [System.IO.Path]::GetRelativePath($contentRoot, $file.FullName).Replace('\', '/')
        $sourcePath = $null
        if ($importsByDestination.ContainsKey($relativePath)) {
            $sourcePath = $importsByDestination[$relativePath]
        } elseif ($relativePath -eq 'articles/ADRs/index.md') {
            $sourcePath = 'src/devops/Rowles.LeanCorpus.DevOps/Documentation/AdrIndexGenerator.cs'
        } elseif ($relativePath -eq 'changelog/index.md') {
            $sourcePath = 'src/devops/Rowles.LeanCorpus.DevOps/Documentation/DocumentationSectionCatalogWriter.cs'
        } elseif ($relativePath.StartsWith('changelog/', [StringComparison]::OrdinalIgnoreCase)) {
            $sourcePath = "changelog/$($relativePath.Substring('changelog/'.Length))"
        } elseif ($relativePath.StartsWith('benchmarks/', [StringComparison]::OrdinalIgnoreCase)) {
            $sourcePath = 'src/devops/Rowles.LeanCorpus.DevOps/Documentation/BenchmarkDocumentationGenerator.cs'
        } else {
            $sourcePath = "docs/content/$relativePath"
        }

        $sourceFile = Join-Path $repoRoot ($sourcePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            throw "Cannot map staged documentation page '$relativePath' to a repository source file: $sourcePath"
        }
        $encodedPath = (($sourcePath -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
        $docUrl = "$repoPrefix$commit/$encodedPath"

        $content = [System.IO.File]::ReadAllText($file.FullName)
        $frontMatter = [regex]::Match($content, '\A---\r?\n(?<yaml>[\s\S]*?)\r?\n---(?=\r?\n|$)')
        if ($frontMatter.Success) {
            $yaml = $frontMatter.Groups['yaml'].Value
            if ($yaml -match '(?m)^docurl\s*:') {
                $yaml = [regex]::Replace($yaml, '(?m)^docurl\s*:.*$', "docurl: '$docUrl'")
            } else {
                $yaml = "$yaml`ndocurl: '$docUrl'"
            }
            $replacement = "---`n$yaml`n---"
            $content = $content.Substring(0, $frontMatter.Index) + $replacement + $content.Substring($frontMatter.Index + $frontMatter.Length)
        } else {
            $content = "---`ndocurl: '$docUrl'`n---`n`n$content"
        }
        [System.IO.File]::WriteAllText($file.FullName, $content, [System.Text.UTF8Encoding]::new($false))
    }
    Write-Info "Attached canonical contribution URLs to staged Markdown pages at commit $commit."
}

function Copy-RepositoryDocumentation {
    param(
        [string]$RepoRoot,
        [string]$ContentDirectory,
        [string]$ManifestPath,
        [string]$DocsSourceDirectory
    )

    $RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $ContentDirectory = [System.IO.Path]::GetFullPath($ContentDirectory)
    $DocsSourceDirectory = [System.IO.Path]::GetFullPath($DocsSourceDirectory)
    $ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
    if (-not (Test-Path $ManifestPath -PathType Leaf)) {
        throw "Documentation import manifest not found: $ManifestPath"
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1 -or $null -eq $manifest.imports) {
        throw "Unsupported documentation import manifest: $ManifestPath"
    }
    $entries = @($manifest.imports)
    if ($entries.Count -eq 0) {
        throw 'The documentation import manifest contains no imports.'
    }

    $gitCommit = (& git -C $RepoRoot rev-parse --verify HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Could not resolve the current Git commit for imported-document source links.'
    }
    $gitCommit = $gitCommit.ToLowerInvariant()

    $destinationBySource = @{}
    $sourceByDestination = @{}
    foreach ($entry in $entries) {
        $sourceRelative = [string]$entry.source
        $destinationRelative = [string]$entry.destination
        if ([string]::IsNullOrWhiteSpace($sourceRelative) -or [string]::IsNullOrWhiteSpace($destinationRelative)) {
            throw 'Every documentation import must have a source and destination.'
        }
        if ([System.IO.Path]::IsPathRooted($destinationRelative) -or $destinationRelative -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Documentation import destination must stay inside the staged content tree: $destinationRelative"
        }

        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $sourceRelative))
        if (-not $sourcePath.StartsWith($RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path $sourcePath -PathType Leaf)) {
            throw "Repository documentation source not found inside the repository: $sourceRelative"
        }

        $normalisedSource = $sourcePath
        if ($destinationBySource.ContainsKey($normalisedSource)) {
            throw "Repository documentation source is mapped more than once: $sourceRelative"
        }
        $destinationPath = [System.IO.Path]::GetFullPath((Join-Path $ContentDirectory $destinationRelative))
        $contentPrefix = $ContentDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $destinationPath.StartsWith($contentPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Documentation import destination escapes the staged content tree: $destinationRelative"
        }
        if ($sourceByDestination.ContainsKey($destinationPath)) {
            throw "Repository documentation destination is mapped more than once: $destinationRelative"
        }
        if (Test-Path -LiteralPath $destinationPath) {
            throw "Repository documentation destination collides with staged content: $destinationRelative"
        }
        $destinationBySource[$normalisedSource] = $destinationRelative
        $sourceByDestination[$destinationPath] = $normalisedSource
    }

    foreach ($entry in $entries) {
        $sourceRelative = [string]$entry.source
        $destinationRelative = [string]$entry.destination
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $sourceRelative))
        $destinationPath = [System.IO.Path]::GetFullPath((Join-Path $ContentDirectory $destinationRelative))
        $destinationDirectory = Split-Path $destinationPath -Parent
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

        $content = [System.IO.File]::ReadAllText($sourcePath)

        $linkPattern = '(?<prefix>!?\[[^\]]*\]\()(?<target><[^>]+>|[^)\s]+)(?<suffix>\s*(?:"[^"]*"|''[^'']*'')?\))'
        $content = [regex]::Replace($content, $linkPattern, {
            param($match)

            $target = $match.Groups['target'].Value.Trim('<', '>')
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
            $mappedDestination = if ($destinationBySource.ContainsKey($resolvedTarget)) { $destinationBySource[$resolvedTarget] } else { $null }
            if (-not $mappedDestination -and (Test-Path $resolvedTarget -PathType Container)) {
                $readmePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedTarget 'README.md'))
                if ($destinationBySource.ContainsKey($readmePath)) { $mappedDestination = $destinationBySource[$readmePath] }
            }

            if ($mappedDestination) {
                $siteTarget = Join-Path $ContentDirectory $mappedDestination
                $rewritten = [System.IO.Path]::GetRelativePath($destinationDirectory, $siteTarget).Replace('\', '/')
                return "$($match.Groups['prefix'].Value)$rewritten$fragment$($match.Groups['suffix'].Value)"
            }

            $docsRootWithSeparator = $DocsSourceDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            if ($resolvedTarget.StartsWith($docsRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
                $docsRelative = [System.IO.Path]::GetRelativePath($DocsSourceDirectory, $resolvedTarget)
                $stagedTarget = Join-Path $ContentDirectory $docsRelative
                $rewritten = [System.IO.Path]::GetRelativePath($destinationDirectory, $stagedTarget).Replace('\', '/')
                return "$($match.Groups['prefix'].Value)$rewritten$fragment$($match.Groups['suffix'].Value)"
            }

            if (-not (Test-Path $resolvedTarget)) {
                throw "Unresolved repository documentation link '$target' in '$($entry.Source)'."
            }

            $repoRelative = [System.IO.Path]::GetRelativePath($RepoRoot, $resolvedTarget).Replace('\', '/')
            $githubKind = if (Test-Path $resolvedTarget -PathType Container) { 'tree' } else { 'blob' }
            $githubTarget = "https://github.com/jordansrowles/LeanCorpus/$githubKind/$gitCommit/$repoRelative$fragment"
            return "$($match.Groups['prefix'].Value)$githubTarget$($match.Groups['suffix'].Value)"
        })

        $sourceUrl = "https://github.com/jordansrowles/LeanCorpus/blob/$gitCommit/$sourceRelative"
        $notice = "> [!NOTE]`n> This page is generated from [$sourceRelative]($sourceUrl). Edit the repository file, not this copy.`n`n"
        Set-GeneratedContent -Path $destinationPath -Value ($notice + $content.TrimStart())
    }

    # Canonical articles are one directory deeper than the former docs/ root.
    # Resolve their repository links from the source tree, then map them to
    # staged pages or commit-specific GitHub links before DocFX sees them.
    $docsRootWithSeparator = $DocsSourceDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $repoRootWithSeparator = $RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $linkPattern = '(?<prefix>!?\[[^\]]*\]\()(?<target><[^>]+>|[^)\s]+)(?<suffix>\s*(?:"[^"]*"|''[^'']*'')?\))'
    foreach ($sourceFile in Get-ChildItem -LiteralPath $DocsSourceDirectory -Recurse -File -Filter '*.md') {
        $docsRelative = [System.IO.Path]::GetRelativePath($DocsSourceDirectory, $sourceFile.FullName)
        $destinationPath = [System.IO.Path]::GetFullPath((Join-Path $ContentDirectory $docsRelative))
        if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) { continue }
        $destinationDirectory = Split-Path $destinationPath -Parent
        $content = [System.IO.File]::ReadAllText($destinationPath)
        $content = [regex]::Replace($content, $linkPattern, {
            param($match)

            $target = $match.Groups['target'].Value.Trim('<', '>')
            if ($target.StartsWith('#') -or $target.StartsWith('/') -or $target.StartsWith('~/') -or $target -match '^[a-z][a-z0-9+.-]*:') {
                return $match.Value
            }

            $pathPart, $fragment = if ($target.Contains('#')) {
                $parts = $target.Split('#', 2)
                $parts[0], "#$($parts[1])"
            } else {
                $target, ''
            }
            if ([string]::IsNullOrWhiteSpace($pathPart)) { return $match.Value }

            $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $sourceFile.FullName -Parent) $pathPart))
            if ($resolvedTarget.StartsWith($docsRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
                $docsRelativeTarget = [System.IO.Path]::GetRelativePath($DocsSourceDirectory, $resolvedTarget)
                $stagedTarget = [System.IO.Path]::GetFullPath((Join-Path $ContentDirectory $docsRelativeTarget))
                if (Test-Path -LiteralPath $stagedTarget) {
                    $rewritten = [System.IO.Path]::GetRelativePath($destinationDirectory, $stagedTarget).Replace('\', '/')
                    return "$($match.Groups['prefix'].Value)$rewritten$fragment$($match.Groups['suffix'].Value)"
                }
                # Generated pages such as benchmarks/overview.md are added
                # later in the same prepare workflow; their mirrored relative
                # links remain valid in the staging tree.
                return $match.Value
            }

            $mappedDestination = if ($destinationBySource.ContainsKey($resolvedTarget)) { $destinationBySource[$resolvedTarget] } else { $null }
            if (-not $mappedDestination -and (Test-Path -LiteralPath $resolvedTarget -PathType Container)) {
                $readmePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedTarget 'README.md'))
                if ($destinationBySource.ContainsKey($readmePath)) { $mappedDestination = $destinationBySource[$readmePath] }
            }
            if ($mappedDestination) {
                $stagedTarget = [System.IO.Path]::GetFullPath((Join-Path $ContentDirectory $mappedDestination))
                $rewritten = [System.IO.Path]::GetRelativePath($destinationDirectory, $stagedTarget).Replace('\', '/')
                return "$($match.Groups['prefix'].Value)$rewritten$fragment$($match.Groups['suffix'].Value)"
            }

            if (-not $resolvedTarget.StartsWith($repoRootWithSeparator, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $resolvedTarget)) {
                throw "Unresolved canonical documentation link '$target' in '$($sourceFile.FullName)'."
            }

            $repoRelative = [System.IO.Path]::GetRelativePath($RepoRoot, $resolvedTarget).Replace('\', '/')
            $githubKind = if (Test-Path -LiteralPath $resolvedTarget -PathType Container) { 'tree' } else { 'blob' }
            $githubTarget = "https://github.com/jordansrowles/LeanCorpus/$githubKind/$gitCommit/$repoRelative$fragment"
            return "$($match.Groups['prefix'].Value)$githubTarget$($match.Groups['suffix'].Value)"
        })
        Set-GeneratedContent -Path $destinationPath -Value $content
    }

    Write-Info "Repository documentation staged: $($entries.Count) pages from commit $gitCommit."
}
