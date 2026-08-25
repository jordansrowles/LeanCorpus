$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Repository health report: devops report [git|files|code] [options]
#
# Groups:
#   git    repository/commit-level statistics
#   files  per-file facts and history
#   code   source-code health
#
# Options:
#   -Top N       entries per list (default: 10)
#   -Path <glob> restrict file/code scans to a glob (e.g. src/core/**)
#   -Json        emit a single JSON object instead of terminal output
#   -Strict      exit non-zero on illegal names, severe god classes, or
#                AOT-hostile patterns
#
# Known limitations (documented, not bugs):
#   * History follows renames git detects (>=50% similarity); a move with larger
#     content changes is still treated as created at the move.
#   * God classes and untested-source are line-count / name-match heuristics.
# ---------------------------------------------------------------------------

# Tunable thresholds.
$Script:GodClassWarnLoc   = 400
$Script:GodClassSevereLoc = 800
$Script:LargeFileBytes    = 500KB
$Script:CoChangeMaxFiles  = 20     # skip monolithic commits in co-change pairs
$Script:CoChangeMinCount  = 2      # a pair must co-occur at least this often
$Script:RecentWindowDays  = 30

$Script:AotHostilePattern = '\b(dynamic|MakeGenericType|MakeGenericMethod|Assembly\.Load|Marshal\.|Activator\.CreateInstance|Type\.GetType|GetConstructor|GetMethod|Reflection\.Emit)\b'
$Script:ConventionalPattern = '^(feat|fix|chore|docs|test|refactor|perf|build|ci|style|revert)(\([^)]*\))?!?:'

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
function Invoke-DevOpsReport {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot
    $parsed = ConvertFrom-DevOpsArguments $Arguments

    $group = if ($parsed.Positionals.Count -gt 0) { $parsed.Positionals[0] } else { 'all' }
    $valid = @('all', 'git', 'files', 'code')
    if ($group -notin $valid) {
        Write-Error "Unknown report group '$group'. Valid: $($valid -join ', ')"
        exit 1
    }

    if (-not (Test-Path (Join-Path $repoRoot '.git'))) {
        Write-Error "Not a git repository at '$repoRoot'. Report requires git."
        exit 1
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error 'git not found on PATH.'
        exit 1
    }

    $config = [pscustomobject]@{
        RepoRoot = $repoRoot
        Group    = $group
        Top      = [int]($parsed.Get('Top', 10))
        Json     = $parsed.Has('Json')
        Strict   = $parsed.Has('Strict')
        Path     = $parsed.Get('Path', $null)
    }

    $needHistory = $group -in @('all', 'git', 'files')
    $needTree    = $group -in @('all', 'files')

    $history = if ($needHistory) { Get-GitHistory $repoRoot } else { $null }
    $tree    = if ($needTree)    { Get-TrackedFiles $repoRoot } else { $null }

    $report = [ordered]@{}
    if ($group -in @('all', 'git'))   { $report.Git   = Get-GitReport $config $history }
    if ($group -in @('all', 'files')) { $report.Files = Get-FilesReport $config $history $tree }
    if ($group -in @('all', 'code'))  { $report.Code  = Get-CodeReport $config }

    # Strict violations: illegal names, severe god classes, AOT-hostile patterns.
    $violations = 0
    $filesReport = $report['Files']
    $codeReport  = $report['Code']
    if ($null -ne $filesReport -and $filesReport['Illegal'].ViolationCount -gt 0) { $violations++ }
    if ($null -ne $codeReport) {
        if ($codeReport['GodClasses'].SevereCount -gt 0) { $violations++ }
        if ($codeReport['AotHygiene'].AotFiles.Count -gt 0) { $violations++ }
    }

    if ($config.Json) {
        $report | ConvertTo-Json -Depth 10
    } else {
        Show-Report $report $config
        if ($config.Strict -and $violations -gt 0) {
            Write-Failure "$violations strict violation(s) found (see above)."
        }
    }

    if ($config.Strict -and $violations -gt 0) { exit 1 }
    exit 0
}

# ---------------------------------------------------------------------------
# Shared data collection
# ---------------------------------------------------------------------------
function Get-GitHistory {
    param([string]$Root)

    $output = git -C $Root log --numstat -M --format="%x01%cI" 2>$null
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Files = @{}; CommitDates = @(); CoChange = @{} }
    }

    $files = @{}
    $coChange = @{}
    $commitDates = [System.Collections.Generic.List[string]]::new()
    $currentPaths = [System.Collections.Generic.List[string]]::new()
    $currentDate = $null
    $currentDt = [datetime]::MinValue
    $cutoff = (Get-Date).AddDays(-$Script:RecentWindowDays)
    $recentCommit = $false

    foreach ($line in $output) {
        if ($line.Length -eq 0) { continue }
        if ($line[0] -eq [char]1) {
            Update-CoChange -Paths $currentPaths -Pairs $coChange
            $currentDate = $line.Substring(1)
            $currentDt = [datetime]::Parse($currentDate)
            $recentCommit = $currentDt -ge $cutoff
            $commitDates.Add($currentDate)
            $currentPaths.Clear()
            continue
        }

        $parts = $line -split "`t"
        if ($parts.Count -lt 3) { continue }
        $added = $parts[0]
        $deleted = $parts[1]
        $path = ($parts[2..($parts.Count - 1)] -join "`t")
        $currentPaths.Add($path)

        if (-not $files.ContainsKey($path)) {
            $files[$path] = [pscustomobject]@{
                Count   = 0
                First   = $currentDt
                Last    = $currentDt
                Added   = 0
                Deleted = 0
                Recent  = 0
            }
        }
        $f = $files[$path]
        $f.Count++
        $f.First = $currentDt
        if ($added -match '^\d+$') { $f.Added += [int]$added }
        if ($deleted -match '^\d+$') { $f.Deleted += [int]$deleted }
        if ($recentCommit) { $f.Recent++ }
    }
    Update-CoChange -Paths $currentPaths -Pairs $coChange

    # Resolve renames so a file's full history follows it across renames, then
    # drop paths removed outright (the numstat pass is otherwise rename-blind).
    $renameMap = Get-RenameMap $Root
    $tracked = @{}
    foreach ($p in (git -C $Root ls-files 2>$null)) { if ($p) { $tracked[$p] = $true } }

    $merged = @{}
    foreach ($path in $files.Keys) {
        $canon = Resolve-Rename -Path $path -Map $renameMap
        if (-not $tracked.ContainsKey($canon)) { continue }
        $f = $files[$path]
        if (-not $merged.ContainsKey($canon)) {
            $merged[$canon] = [pscustomobject]@{
                Count = $f.Count; First = $f.First; Last = $f.Last
                Added = $f.Added; Deleted = $f.Deleted; Recent = $f.Recent
            }
        } else {
            $m = $merged[$canon]
            $m.Count += $f.Count
            $m.Added += $f.Added
            $m.Deleted += $f.Deleted
            $m.Recent += $f.Recent
            if ($f.First -lt $m.First) { $m.First = $f.First }
            if ($f.Last -gt $m.Last) { $m.Last = $f.Last }
        }
    }
    $files = $merged

    $filteredCo = @{}
    foreach ($k in $coChange.Keys) {
        $parts = $k -split '\|\|'
        $l = Resolve-Rename -Path $parts[0] -Map $renameMap
        $r = Resolve-Rename -Path $parts[1] -Map $renameMap
        if ($l -eq $r) { continue }
        if (-not $tracked.ContainsKey($l) -or -not $tracked.ContainsKey($r)) { continue }
        $nk = if ([string]::CompareOrdinal($l, $r) -lt 0) { "$l||$r" } else { "$r||$l" }
        if ($filteredCo.ContainsKey($nk)) { $filteredCo[$nk] += $coChange[$k] } else { $filteredCo[$nk] = $coChange[$k] }
    }
    return [pscustomobject]@{
        Files       = $files
        CommitDates = @($commitDates)
        CoChange    = $filteredCo
    }
}

function Update-CoChange {
    param(
        [System.Collections.Generic.List[string]]$Paths,
        [hashtable]$Pairs
    )
    if ($Paths.Count -lt 2 -or $Paths.Count -gt $Script:CoChangeMaxFiles) { return }
    for ($i = 0; $i -lt $Paths.Count; $i++) {
        for ($j = $i + 1; $j -lt $Paths.Count; $j++) {
            $a = $Paths[$i]
            $b = $Paths[$j]
            $key = if ([string]::CompareOrdinal($a, $b) -lt 0) { "$a||$b" } else { "$b||$a" }
            if ($Pairs.ContainsKey($key)) { $Pairs[$key]++ } else { $Pairs[$key] = 1 }
        }
    }
}

function Get-TrackedFiles {
    param([string]$Root)

    $out = git -C $Root ls-tree -r --format='%(objectsize)%x09%(path)' HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }

    $list = [System.Collections.Generic.List[object]]::new()
    foreach ($line in $out) {
        $i = $line.IndexOf("`t")
        if ($i -le 0) { continue }
        $size = [int64]($line.Substring(0, $i))
        $path = $line.Substring($i + 1)
        $list.Add([pscustomobject]@{ Path = $path; Size = $size })
    }
    return @($list)
}

function Get-CsAnalysis {
    param([string[]]$CsFiles, [string]$Root)

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($rel in $CsFiles) {
        $full = Join-Path $Root $rel
        if (-not (Test-Path $full)) { continue }

        $raw = [System.IO.File]::ReadAllBytes($full)
        $hasBom = ($raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF)
        $text = [System.Text.Encoding]::UTF8.GetString($raw)

        $loc = 0
        foreach ($line in ($text -split "`n")) {
            $t = $line.Trim()
            if ($t.Length -eq 0 -or $t -eq '{' -or $t -eq '}') { continue }
            if ($t.StartsWith('//') -or $t.StartsWith('/*') -or $t.StartsWith('*') -or $t.StartsWith('*/')) { continue }
            $loc++
        }

        $aotMatches = @()
        if ($text -match $Script:AotHostilePattern) {
            $aotMatches = @([regex]::Matches($text, $Script:AotHostilePattern) | ForEach-Object { $_.Value } | Sort-Object -Unique)
        }

        $results.Add([pscustomobject]@{
            Path           = $rel
            Lines          = $loc
            HasBom         = $hasBom
            HasCrlf        = ($text.IndexOf("`r`n") -ge 0)
            HasTrailingWs  = ($text -match '(?m)[ \t]+$')
            HasTabs        = ($text -match '(?m)^\t')
            AotMatches     = $aotMatches
        })
    }
    return @($results)
}

function Get-ProductionCs {
    param([string[]]$AllCs)

    $exclude = @('Tests', 'Benchmarks', 'Example', 'Examples', 'Profiling', 'AOTSmoke', 'SourceGen')
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $AllCs) {
        if ($f -notlike 'src/core/*' -and $f -notlike 'src/server/*') { continue }
        $skip = $false
        foreach ($seg in ($f -split '/')) {
            foreach ($ex in $exclude) {
                if ($seg.IndexOf($ex, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $skip = $true; break }
            }
            if ($skip) { break }
        }
        if (-not $skip) { $result.Add($f) }
    }
    return @($result)
}

# ---------------------------------------------------------------------------
# Git group
# ---------------------------------------------------------------------------
function Get-GitReport {
    param($Config, $History)

    $Root = $Config.RepoRoot
    $sections = [ordered]@{}
    $sections.Overview     = Get-GitOverview $Root
    $sections.Contributors = Get-GitContributors $Root $Config.Top
    $sections.Activity     = Get-GitActivity $History
    $sections.Hygiene      = Get-GitHygiene $Root
    $sections.Churn        = Get-GitChurn $History $Config.Top
    $sections.Recent       = Get-GitRecentFiles $History $Config.Top
    return $sections
}

function Get-GitOverview {
    param([string]$Root)

    $count   = (git -C $Root rev-list --count HEAD 2>$null | Select-Object -First 1)
    $first   = (git -C $Root log --reverse --format='%cI' 2>$null | Select-Object -First 1)
    $last    = (git -C $Root log -1 --format='%cI' 2>$null | Select-Object -First 1)
    $branch  = (git -C $Root rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1)
    $branches = @(git -C $Root branch --format='%(refname:short)' 2>$null | Where-Object { $_ })
    $tags = @(git -C $Root tag --list 2>$null | Where-Object { $_ })

    $firstStr = if ($first) { ([datetime]::Parse($first)).ToString('yyyy-MM-dd') } else { 'n/a' }
    $lastStr  = if ($last)  { ([datetime]::Parse($last)).ToString('yyyy-MM-dd') }  else { 'n/a' }

    [pscustomobject]@{
        Commits     = if ($count) { [int]$count } else { 0 }
        FirstCommit = $firstStr
        LastCommit  = $lastStr
        Branch      = $branch
        BranchCount = $branches.Count
        TagCount    = $tags.Count
    }
}

function Get-GitContributors {
    param([string]$Root, [int]$Top)

    $out = git -C $Root log --format='%ae%x09%an' 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }

    $byEmail = @{}
    foreach ($line in $out) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split "`t", 2
        $email = $parts[0].Trim()
        $name  = if ($parts.Count -gt 1) { $parts[1].Trim() } else { $email }
        if ($byEmail.ContainsKey($email)) {
            $byEmail[$email].Commits++
            $byEmail[$email].Name = $name
        } else {
            $byEmail[$email] = [pscustomobject]@{ Email = $email; Name = $name; Commits = 1 }
        }
    }
    return @($byEmail.Values) | Sort-Object Commits -Descending | Select-Object -First $Top
}

function Get-GitActivity {
    param($History)

    $buckets = [ordered]@{}
    foreach ($d in $History.CommitDates) {
        $dt = [datetime]::Parse($d)
        $key = $dt.ToString('yyyy-MM')
        if ($buckets.Contains($key)) { $buckets[$key]++ } else { $buckets[$key] = 1 }
    }
    $keys = @($buckets.Keys) | Sort-Object
    $rows = foreach ($k in $keys) {
        [pscustomobject]@{ Month = $k; Commits = $buckets[$k] }
    }
    return @($rows)
}

function Get-GitHygiene {
    param([string]$Root)

    $subjects = git -C $Root log --format='%s' 2>$null
    $total = 0
    $conventional = 0
    foreach ($s in $subjects) {
        if ([string]::IsNullOrWhiteSpace($s)) { continue }
        $total++
        if ($s -match $Script:ConventionalPattern) { $conventional++ }
    }

    $coAuthorCommits = @(git -C $Root log --grep='Co-authored-by' --format='%h' 2>$null | Where-Object { $_ })

    $pct = 0.0
    if ($total -gt 0) { $pct = [math]::Round(100.0 * $conventional / $total, 1) }

    [pscustomobject]@{
        TotalSubjects   = $total
        Conventional    = $conventional
        ConventionalPct = $pct
        CoAuthorCommits = $coAuthorCommits.Count
    }
}

function Get-GitChurn {
    param($History, [int]$Top)

    $files = $History.Files
    $rows = [System.Collections.Generic.List[object]]::new()
    $totalAdded = 0
    $totalDeleted = 0
    foreach ($path in $files.Keys) {
        if ($path -notlike 'src/*') { continue }
        $f = $files[$path]
        $totalAdded += $f.Added
        $totalDeleted += $f.Deleted
        $rows.Add([pscustomobject]@{ Path = $path; Added = $f.Added; Deleted = $f.Deleted })
    }

    [pscustomobject]@{
        TotalAdded   = $totalAdded
        TotalDeleted = $totalDeleted
        TopAdded     = @($rows) | Sort-Object Added -Descending | Select-Object -First $Top
        TopDeleted   = @($rows) | Sort-Object Deleted -Descending | Select-Object -First $Top
    }
}

function Get-GitRecentFiles {
    param($History, [int]$Top)

    $rows = foreach ($path in $History.Files.Keys) {
        $f = $History.Files[$path]
        if ($f.Recent -gt 0) { [pscustomobject]@{ Path = $path; Recent = $f.Recent } }
    }
    return @($rows) | Sort-Object Recent -Descending | Select-Object -First $Top
}

# ---------------------------------------------------------------------------
# Files group
# ---------------------------------------------------------------------------
function Get-FilesReport {
    param($Config, $History, $Tree)

    $sections = [ordered]@{}
    $sections.History  = Get-FileHistory $History $Config.Top
    $sections.CoChange = Get-CoChange $History $Config.Top

    $scopedTree = if ($Config.Path) { @($Tree | Where-Object { $_.Path -like $Config.Path }) } else { @($Tree) }
    $sections.ByType  = Get-FileCountByType $scopedTree
    $sections.Large   = Get-LargeFiles $scopedTree $Config.Top
    $sections.Illegal = Get-IllegalFileNames $scopedTree

    return $sections
}

function Get-FileHistory {
    param($History, [int]$Top)

    $files = $History.Files
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($path in $files.Keys) {
        $f = $files[$path]
        $rows.Add([pscustomobject]@{
            Path    = $path
            Touches = $f.Count
            First   = $f.First
            Last    = $f.Last
        })
    }
    $arr = @($rows)

    [pscustomobject]@{
        MostTouched  = $arr | Sort-Object Touches -Descending | Select-Object -First $Top
        LeastTouched = $arr | Where-Object { $_.Touches -gt 1 } | Sort-Object Touches | Select-Object -First $Top
        Newest       = $arr | Sort-Object Last -Descending | Select-Object -First $Top
        Oldest       = $arr | Sort-Object First | Select-Object -First $Top
        Stale        = $arr | Where-Object { $_.Touches -gt 1 } | Sort-Object Last | Select-Object -First $Top
        CreatedOnce  = $arr | Where-Object { $_.Touches -eq 1 } | Sort-Object First
    }
}

function Get-CoChange {
    param($History, [int]$Top)

    $rows = foreach ($key in $History.CoChange.Keys) {
        $count = $History.CoChange[$key]
        if ($count -ge $Script:CoChangeMinCount) {
            $parts = $key -split '\|\|'
            [pscustomobject]@{ Left = $parts[0]; Right = $parts[1]; Together = $count }
        }
    }
    return @($rows) | Sort-Object Together -Descending | Select-Object -First $Top
}

function Get-FileCountByType {
    param($Tree)

    $counts = @{}
    foreach ($item in $Tree) {
        $ext = [System.IO.Path]::GetExtension($item.Path)
        if ([string]::IsNullOrEmpty($ext)) { $ext = '(none)' } else { $ext = $ext.TrimStart('.').ToLowerInvariant() }
        if ($counts.ContainsKey($ext)) { $counts[$ext]++ } else { $counts[$ext] = 1 }
    }
    $rows = foreach ($k in $counts.Keys) { [pscustomobject]@{ Extension = $k; Count = $counts[$k] } }
    return @($rows) | Sort-Object Count -Descending
}

function Get-LargeFiles {
    param($Tree, [int]$Top)

    $large = @($Tree | Where-Object { $_.Size -gt $Script:LargeFileBytes } | Sort-Object Size -Descending)
    [pscustomobject]@{
        ThresholdBytes = $Script:LargeFileBytes
        Count          = $large.Count
        Files          = @($large | Select-Object -First $Top)
    }
}

function Get-IllegalFileNames {
    param($Tree)

    $deviceNames = @('CON', 'PRN', 'AUX', 'NUL', 'COM0', 'LPT0')
    foreach ($n in 1..9) { $deviceNames += @("COM$n", "LPT$n") }
    $deviceSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($d in $deviceNames) { [void]$deviceSet.Add($d) }

    $reservedCharFiles = [System.Collections.Generic.List[string]]::new()
    $deviceFiles       = [System.Collections.Generic.List[string]]::new()
    $trailingFiles     = [System.Collections.Generic.List[string]]::new()
    $controlFiles      = [System.Collections.Generic.List[string]]::new()
    $lowerIndex        = @{}

    foreach ($item in $Tree) {
        $path = $item.Path
        $name = [System.IO.Path]::GetFileName($path)

        if ($name -match '[<>:"\\|?*]') { $reservedCharFiles.Add($path) }
        if ($name -match '[\x00-\x1f]') { $controlFiles.Add($path) }
        if ($name -match '[. ]$')       { $trailingFiles.Add($path) }

        $stem = $name
        $dotIdx = $name.IndexOf('.')
        if ($dotIdx -gt 0) { $stem = $name.Substring(0, $dotIdx) }
        if ($deviceSet.Contains($stem)) { $deviceFiles.Add($path) }

        $lk = $path.ToLowerInvariant()
        if (-not $lowerIndex.ContainsKey($lk)) { $lowerIndex[$lk] = [System.Collections.Generic.List[string]]::new() }
        $lowerIndex[$lk].Add($path)
    }

    $collisions = [System.Collections.Generic.List[object]]::new()
    foreach ($k in $lowerIndex.Keys) {
        $paths = $lowerIndex[$k]
        if ($paths.Count -gt 1) { $collisions.Add([pscustomobject]@{ Key = $k; Paths = @($paths) }) }
    }

    [pscustomobject]@{
        ReservedChars   = @($reservedCharFiles)
        DeviceNames     = @($deviceFiles)
        TrailingDotSpace = @($trailingFiles)
        ControlChars    = @($controlFiles)
        Collisions      = @($collisions)
        ViolationCount  = @($reservedCharFiles).Count + @($deviceFiles).Count + @($trailingFiles).Count + @($controlFiles).Count + @($collisions).Count
    }
}

# ---------------------------------------------------------------------------
# Code group
# ---------------------------------------------------------------------------
function Get-CodeReport {
    param($Config)

    $Root = $Config.RepoRoot
    $allCs = @(git -C $Root ls-files '*.cs' 2>$null)
    if ($LASTEXITCODE -ne 0) { $allCs = @() }

    $scopedCs = if ($Config.Path) { @($allCs | Where-Object { $_ -like $Config.Path }) } else { @($allCs) }

    $production = Get-ProductionCs $scopedCs
    $analysis = Get-CsAnalysis $scopedCs $Root

    $productionSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($p in $production) { [void]$productionSet.Add($p) }
    $prodAnalysis = @($analysis | Where-Object { $productionSet.Contains($_.Path) })

    $sections = [ordered]@{}
    $sections.GodClasses  = Get-GodClasses $prodAnalysis $Config.Top
    $sections.Dependencies = Get-DependencyGraph $Root
    $sections.Untested    = Get-UntestedSource $Root $Config.Top
    $sections.AotHygiene  = Get-AotHygiene $prodAnalysis $analysis
    return $sections
}

function Get-GodClasses {
    param([object[]]$Analysis, [int]$Top)

    $baseIndex = @{}
    foreach ($a in $Analysis) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($a.Path)
        if (-not $baseIndex.ContainsKey($base)) { $baseIndex[$base] = $a.Path }
    }

    $locByGroup = @{}
    $groupFiles = @{}
    foreach ($a in $Analysis) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($a.Path)
        $group = $base
        $dot = $base.IndexOf('.')
        if ($dot -gt 0) {
            $prefix = $base.Substring(0, $dot)
            if ($baseIndex.ContainsKey($prefix)) {
                $dir = [System.IO.Path]::GetDirectoryName($a.Path)
                $prefixDir = [System.IO.Path]::GetDirectoryName($baseIndex[$prefix])
                if ($dir -eq $prefixDir) { $group = $prefix }
            }
        }
        if ($locByGroup.ContainsKey($group)) { $locByGroup[$group] += $a.Lines } else { $locByGroup[$group] = $a.Lines }
        if (-not $groupFiles.ContainsKey($group)) { $groupFiles[$group] = [System.Collections.Generic.List[string]]::new() }
        $groupFiles[$group].Add($a.Path)
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($g in $locByGroup.Keys) {
        $loc = $locByGroup[$g]
        $sev = 'ok'
        if ($loc -gt $Script:GodClassSevereLoc) { $sev = 'severe' }
        elseif ($loc -gt $Script:GodClassWarnLoc) { $sev = 'warn' }
        $rows.Add([pscustomobject]@{
            Type     = $g
            Lines    = $loc
            Files    = @($groupFiles[$g])
            Severity = $sev
        })
    }
    $arr = @($rows)

    [pscustomobject]@{
        WarnThreshold   = $Script:GodClassWarnLoc
        SevereThreshold = $Script:GodClassSevereLoc
        Flagged         = @($arr | Where-Object { $_.Severity -ne 'ok' } | Sort-Object Lines -Descending)
        SevereCount     = @($arr | Where-Object { $_.Severity -eq 'severe' }).Count
        Top             = @($arr | Sort-Object Lines -Descending | Select-Object -First $Top)
    }
}

function Get-DependencyGraph {
    param([string]$Root)

    $csproj = @(git -C $Root ls-files '*.csproj' 2>$null)
    if ($LASTEXITCODE -ne 0 -or $csproj.Count -eq 0) { return $null }

    $projects = [System.Collections.Generic.List[object]]::new()
    $nameIndex = @{}
    $projRefRegex = '<ProjectReference[^>]*Include="([^"]+)"'
    $pkgRefRegex = '<PackageReference[^>]*Include="([^"]+)"'

    foreach ($rel in $csproj) {
        $full = Join-Path $Root $rel
        if (-not (Test-Path $full)) { continue }
        $name = [System.IO.Path]::GetFileNameWithoutExtension($full)
        $content = Get-Content $full -Raw

        $refs = [System.Collections.Generic.List[string]]::new()
        foreach ($m in [regex]::Matches($content, $projRefRegex)) {
            $inc = $m.Groups[1].Value.Replace('\', '/')
            $refs.Add([System.IO.Path]::GetFileNameWithoutExtension($inc))
        }
        $pkgCount = [regex]::Matches($content, $pkgRefRegex).Count

        $proj = [pscustomobject]@{
            Name         = $name
            Refs         = @($refs)
            PackageCount = $pkgCount
            Inbound      = 0
        }
        $projects.Add($proj)
        $nameIndex[$name] = $proj
    }

    # Internal adjacency + inbound counts.
    $adj = @{}
    foreach ($p in $projects) {
        $internal = @($p.Refs | Where-Object { $nameIndex.ContainsKey($_) })
        $adj[$p.Name] = $internal
        foreach ($r in $internal) { $nameIndex[$r].Inbound++ }
    }

    # Cycle detection (Kahn's algorithm).
    $indegree = @{}
    foreach ($p in $projects) { $indegree[$p.Name] = 0 }
    foreach ($p in $projects) {
        foreach ($r in $adj[$p.Name]) { $indegree[$r]++ }
    }
    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($p in $projects) { if ($indegree[$p.Name] -eq 0) { $queue.Enqueue($p.Name) } }
    while ($queue.Count -gt 0) {
        $n = $queue.Dequeue()
        foreach ($r in $adj[$n]) {
            $indegree[$r]--
            if ($indegree[$r] -eq 0) { $queue.Enqueue($r) }
        }
    }
    $cycleNodes = @($projects | Where-Object { $indegree[$_.Name] -gt 0 } | ForEach-Object { $_.Name })

    [pscustomobject]@{
        ProjectCount     = $projects.Count
        TotalPackageRefs = ($projects | Measure-Object PackageCount -Sum).Sum
        Projects         = @($projects | Sort-Object Name)
        Cycles           = $cycleNodes
    }
}

function Get-UntestedSource {
    param([string]$Root, [int]$Top)

    $all = @(git -C $Root ls-files '*.cs' 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }

    $testStems = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $production = [System.Collections.Generic.List[string]]::new()

    foreach ($f in $all) {
        $isTest = $false
        foreach ($seg in ($f -split '/')) {
            if ($seg.IndexOf('Tests', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $seg.IndexOf('Benchmarks', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $isTest = $true
                break
            }
        }
        if ($isTest) {
            [void]$testStems.Add([System.IO.Path]::GetFileNameWithoutExtension($f))
        } elseif ($f -like 'src/core/*' -or $f -like 'src/server/*') {
            $production.Add($f)
        }
    }

    $untested = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $production) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($f)
        $candidates = @($base)
        $dot = $base.IndexOf('.')
        if ($dot -gt 0) { $candidates += $base.Substring(0, $dot) }
        $covered = $false
        foreach ($c in $candidates) {
            if ($testStems.Contains("${c}Tests") -or $testStems.Contains("${c}Test")) { $covered = $true; break }
        }
        if (-not $covered) { $untested.Add($f) }
    }

    [pscustomobject]@{
        ProductionCount = $production.Count
        TestFileCount   = $testStems.Count
        UntestedCount   = $untested.Count
        Untested        = @($untested) | Sort-Object | Select-Object -First $Top
    }
}

function Get-AotHygiene {
    param([object[]]$ProductionAnalysis, [object[]]$AllAnalysis)

    [pscustomobject]@{
        AotFiles                 = @($ProductionAnalysis | Where-Object { $_.AotMatches.Count -gt 0 })
        BomFiles                 = @($AllAnalysis | Where-Object { $_.HasBom })
        CrlfFiles                = @($AllAnalysis | Where-Object { $_.HasCrlf })
        TrailingWhitespaceFiles  = @($AllAnalysis | Where-Object { $_.HasTrailingWs })
        TabIndentFiles           = @($AllAnalysis | Where-Object { $_.HasTabs })
    }
}

# ---------------------------------------------------------------------------
# Terminal display
# ---------------------------------------------------------------------------
function Show-Report {
    param($Report, $Config)

    Write-Heading 'LeanCorpus repository report'
    Write-Host ''

    if ($null -ne $Report['Git'])   { Show-GitReport $Report['Git'] $Config }
    if ($null -ne $Report['Files']) { Show-FilesReport $Report['Files'] $Config }
    if ($null -ne $Report['Code'])  { Show-CodeReport $Report['Code'] $Config }
}

function Show-GitReport {
    param($Git, $Config)

    Write-Heading 'Git statistics'
    Write-Host ''

    $o = $Git.Overview
    Write-Heading 'Overview'
    Write-Info "  commits        : $($o.Commits)"
    Write-Info "  first commit   : $($o.FirstCommit)"
    Write-Info "  last commit    : $($o.LastCommit)"
    Write-Info "  branch         : $($o.Branch) ($($o.BranchCount) local branches, $($o.TagCount) tags)"
    Write-Host ''

    Write-Heading 'Contributors'
    foreach ($c in $Git.Contributors) {
        Write-Info ("  {0,-6} {1} <{2}>" -f $c.Commits, $c.Name, $c.Email)
    }
    Write-Host ''

    Write-Heading 'Commit activity (per month)'
    $max = 1
    if ($Git.Activity.Count -gt 0) { $max = ($Git.Activity | Measure-Object Commits -Maximum).Maximum }
    if ($null -eq $max -or $max -lt 1) { $max = 1 }
    foreach ($r in $Git.Activity) {
        $bar = '#' * [math]::Ceiling(40 * $r.Commits / $max)
        Write-Info ("  {0}  {1,-40} {2}" -f $r.Month, $bar, $r.Commits)
    }
    Write-Host ''

    $h = $Git.Hygiene
    Write-Heading 'Commit hygiene'
    Write-Info "  conventional commits : $($h.Conventional) / $($h.TotalSubjects) ($($h.ConventionalPct)%)"
    if ($h.CoAuthorCommits -gt 0) { Write-Warn "  co-author trailers   : $($h.CoAuthorCommits) commit(s) (policy forbids)" }
    else { Write-Info '  co-author trailers   : 0' }
    Write-Host ''

    $ch = $Git.Churn
    Write-Heading 'Churn'
    Write-Info "  total additions : $($ch.TotalAdded)"
    Write-Info "  total deletions : $($ch.TotalDeleted)"
    Write-Info '  top additions:'
    foreach ($r in $ch.TopAdded) { Write-Info ("    {0,-8} {1}" -f $r.Added, $r.Path) }
    Write-Info '  top deletions:'
    foreach ($r in $ch.TopDeleted) { Write-Info ("    {0,-8} {1}" -f $r.Deleted, $r.Path) }
    Write-Host ''

    Write-Heading "Recent activity (last $($Script:RecentWindowDays) days)"
    if ($Git.Recent.Count -eq 0) { Write-Info '  none' }
    foreach ($r in $Git.Recent) { Write-Info ("  {0,-4} {1}" -f $r.Recent, $r.Path) }
    Write-Host ''
}

function Show-FilesReport {
    param($Files, $Config)

    Write-Heading 'Files'
    Write-Host ''

    $his = $Files.History
    Write-Heading 'File history'
    Write-Info '  most touched:'
    foreach ($r in $his.MostTouched) { Write-Info ("    {0,-5} {1}" -f $r.Touches, $r.Path) }
    Write-Info '  least touched (excluding single-touch):'
    foreach ($r in $his.LeastTouched) { Write-Info ("    {0,-5} {1}" -f $r.Touches, $r.Path) }
    Write-Info '  newest (most recently modified):'
    foreach ($r in $his.Newest) { Write-Info ("    {0}  {1}" -f $r.Last.ToString('yyyy-MM-dd'), $r.Path) }
    Write-Info '  oldest (earliest created; many tie at the initial commit):'
    foreach ($r in $his.Oldest) { Write-Info ("    {0}  {1}" -f $r.First.ToString('yyyy-MM-dd'), $r.Path) }
    Write-Info '  stale (longest since modified, excluding single-touch):'
    foreach ($r in $his.Stale) { Write-Info ("    {0}  {1}" -f $r.Last.ToString('yyyy-MM-dd'), $r.Path) }
    Write-Info "  created and never touched again: $($his.CreatedOnce.Count)"
    foreach ($r in ($his.CreatedOnce | Select-Object -First $Config.Top)) {
        Write-Info ("    {0}  {1}" -f $r.First.ToString('yyyy-MM-dd'), $r.Path)
    }
    Write-Host ''

    Write-Heading 'Co-change coupling'
    if ($Files.CoChange.Count -eq 0) { Write-Info "  no pairs co-committed >= $($Script:CoChangeMinCount) times" }
    foreach ($r in $Files.CoChange) { Write-Info ("  {0,-3} {1}  <=>  {2}" -f $r.Together, $r.Left, $r.Right) }
    Write-Host ''

    Write-Heading 'File count by type'
    foreach ($r in $Files.ByType) { Write-Info ("  {0,-14} {1}" -f $r.Extension, $r.Count) }
    Write-Host ''

    $lg = $Files.Large
    Write-Heading 'Large tracked files'
    $thKb = [math]::Round($lg.ThresholdBytes / 1KB)
    Write-Info "  files over $thKb KB: $($lg.Count)"
    foreach ($r in $lg.Files) { Write-Warn ("  {0,8:N0} KB  {1}" -f ($r.Size / 1KB), $r.Path) }
    Write-Host ''

    $il = $Files.Illegal
    Write-Heading 'Illegal file names (cross-platform)'
    if ($il.ViolationCount -eq 0) {
        Write-Success '  none found'
    } else {
        if ($il.ReservedChars.Count) { Write-Warn '  reserved chars:'; foreach ($p in $il.ReservedChars) { Write-Warn "    $p" } }
        if ($il.DeviceNames.Count) { Write-Warn '  reserved device names:'; foreach ($p in $il.DeviceNames) { Write-Warn "    $p" } }
        if ($il.TrailingDotSpace.Count) { Write-Warn '  trailing dot/space:'; foreach ($p in $il.TrailingDotSpace) { Write-Warn "    $p" } }
        if ($il.ControlChars.Count) { Write-Warn '  control chars:'; foreach ($p in $il.ControlChars) { Write-Warn "    $p" } }
        if ($il.Collisions.Count) { Write-Warn '  case-insensitive collisions:'; foreach ($c in $il.Collisions) { Write-Warn "    $($c.Paths -join ', ')" } }
    }
    Write-Host ''
}

function Show-CodeReport {
    param($Code, $Config)

    Write-Heading 'Code health'
    Write-Host ''

    $g = $Code.GodClasses
    Write-Heading 'God classes'
    Write-Info "  thresholds: warn > $($g.WarnThreshold), severe > $($g.SevereThreshold) LOC"
    foreach ($r in $g.Flagged) {
        if ($r.Severity -eq 'severe') { Write-Warn ("  [SEVERE] {0}  ({1} LOC, {2} file(s))" -f $r.Type, $r.Lines, $r.Files.Count) }
        else { Write-Warn ("  [warn]   {0}  ({1} LOC, {2} file(s))" -f $r.Type, $r.Lines, $r.Files.Count) }
    }
    if ($g.Flagged.Count -eq 0) { Write-Success '  none over thresholds' }
    Write-Host ''

    $d = $Code.Dependencies
    Write-Heading 'Project dependencies'
    if ($null -eq $d) {
        Write-Info '  no csproj files found'
    } else {
        Write-Info "  projects: $($d.ProjectCount), package references: $($d.TotalPackageRefs)"
        foreach ($p in $d.Projects) {
            $refs = if ($p.Refs.Count -gt 0) { ($p.Refs -join ', ') } else { '(none)' }
            Write-Info "  $($p.Name)"
            Write-Info "    refs  : $refs"
            Write-Info "    in    : $($p.Inbound) dependent(s), $($p.PackageCount) package(s)"
        }
        if ($d.Cycles.Count -gt 0) { Write-Warn "  cycles detected: $($d.Cycles -join ', ')" }
        else { Write-Success '  no reference cycles' }
    }
    Write-Host ''

    $u = $Code.Untested
    Write-Heading 'Untested source (heuristic)'
    if ($null -eq $u) {
        Write-Info '  unavailable'
    } else {
        Write-Info "  production files: $($u.ProductionCount), test files: $($u.TestFileCount), untested candidates: $($u.UntestedCount)"
        foreach ($f in $u.Untested) { Write-Warn "  $f" }
        if ($u.UntestedCount -eq 0) { Write-Success '  none' }
    }
    Write-Host ''

    $a = $Code.AotHygiene
    Write-Heading 'AOT + hygiene green-checks'
    if ($a.AotFiles.Count -eq 0) { Write-Success '  AOT-hostile patterns : none' }
    else {
        Write-Warn '  AOT-hostile patterns :'
        foreach ($f in $a.AotFiles) { Write-Warn "    $($f.Path) -> $($f.AotMatches -join ', ')" }
    }
    if ($a.BomFiles.Count -eq 0) { Write-Success '  UTF-8 BOM            : none' }
    else { Write-Warn "  UTF-8 BOM            : $($a.BomFiles.Count) file(s)"; foreach ($f in $a.BomFiles) { Write-Warn "    $($f.Path)" } }
    if ($a.CrlfFiles.Count -eq 0) { Write-Success '  CRLF line endings    : none' }
    else { Write-Warn "  CRLF line endings    : $($a.CrlfFiles.Count) file(s)"; foreach ($f in $a.CrlfFiles) { Write-Warn "    $($f.Path)" } }
    if ($a.TrailingWhitespaceFiles.Count -eq 0) { Write-Success '  trailing whitespace  : none' }
    else { Write-Warn "  trailing whitespace  : $($a.TrailingWhitespaceFiles.Count) file(s)"; foreach ($f in $a.TrailingWhitespaceFiles) { Write-Warn "    $($f.Path)" } }
    if ($a.TabIndentFiles.Count -eq 0) { Write-Success '  tab indentation      : none' }
    else { Write-Warn "  tab indentation      : $($a.TabIndentFiles.Count) file(s)"; foreach ($f in $a.TabIndentFiles) { Write-Warn "    $($f.Path)" } }
    Write-Host ''
}

function Get-RenameMap {
    param([string]$Root)

    $map = @{}
    $out = git -C $Root log --diff-filter=R --name-status -M --format='' 2>$null
    if ($LASTEXITCODE -ne 0) { return $map }
    foreach ($line in $out) {
        if (-not $line.StartsWith('R')) { continue }
        $parts = $line -split "`t"
        if ($parts.Count -lt 3) { continue }
        $map[$parts[1]] = $parts[2]
    }
    return $map
}

function Resolve-Rename {
    param([string]$Path, [hashtable]$Map)

    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cur = $Path
    while ($Map.ContainsKey($cur)) {
        if (-not $seen.Add($cur)) { break }
        $cur = $Map[$cur]
    }
    return $cur
}
