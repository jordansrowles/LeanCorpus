$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-TestProcessWithProgress {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [string]$SuiteName,
        [int]$SuiteNumber,
        [int]$SuiteCount
    )

    Write-Info "  [$SuiteNumber/$SuiteCount] $SuiteName..."

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = $null
    $processStarted = $false
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $FileName
        $startInfo.WorkingDirectory = (Get-Location).Path
        $startInfo.UseShellExecute = $false
        foreach ($argument in $Arguments) {
            [void]$startInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        $processStarted = $process.Start()
        if (-not $processStarted) {
            throw "Unable to start $FileName for $SuiteName."
        }

        $progressInterval = [System.TimeSpan]::FromSeconds(30)
        $nextProgress = $stopwatch.Elapsed.Add($progressInterval)
        while (-not $process.HasExited) {
            [void]$process.WaitForExit(1000)
            if (-not $process.HasExited -and $stopwatch.Elapsed -ge $nextProgress) {
                $elapsed = $stopwatch.Elapsed.ToString('hh\:mm\:ss')
                Write-Host "  [$SuiteNumber/$SuiteCount] $SuiteName still running ($elapsed elapsed)..." -ForegroundColor DarkGray
                $nextProgress = $nextProgress.Add($progressInterval)
            }
        }
        $process.WaitForExit()
        $exitCode = $process.ExitCode
    } finally {
        $stopwatch.Stop()
        if ($processStarted -and -not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
        if ($null -ne $process) {
            $process.Dispose()
        }
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Elapsed  = $stopwatch.Elapsed
    }
}

function Invoke-DevOpsTest {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $configuration = $parsed.Get('Configuration', 'Release')
    $runtimeIdentifier = $parsed.Get('RuntimeIdentifier', '')
    $suite = $parsed.Get('Suite', '')
    if (-not $suite -and $parsed.Positionals.Count -gt 0) { $suite = $parsed.Positionals[0] }
    if (-not $suite) { $suite = 'all' }
    $filter = $parsed.Get('Filter', '')
    $area = $parsed.Get('Area', '')
    $category = $parsed.Get('Category', '')
    $hangTimeout = $parsed.Get('HangTimeout', '100s')
    $verbosity = $parsed.Get('Verbosity', '')
    $list = $parsed.Has('List')
    $repoRoot = Get-RepoRoot

    $testSuites = Import-PowerShellDataFile "$PSScriptRoot/../config/test-suites.psd1"

    if ($list) {
        Write-Host ''
        Write-Host '  Available test suites (-Suite):'
        Write-Host ''
        foreach ($key in $testSuites.Keys) {
            Write-Host "    $($key.PadRight(18)) $($testSuites[$key].Name)"
        }
        Write-Host '    all                 All test suites'
        Write-Host '    affected            Test suites for dirty source areas'
        Write-Host ''
        exit 0
    }

    if ($suite -eq 'affected') {
        Invoke-AffectedTests -Framework $framework -Configuration $configuration -Area $area -Category $category -Filter $filter -Verbosity $verbosity
        exit 0
    }

    $traitFilter = Build-TraitFilter -Area $area -Category $category
    if ($traitFilter) {
        $filter = if ($filter) { "$filter&$traitFilter" } else { $traitFilter }
    }

    [string[]]$toRun = if ($suite -eq 'all') { @('core', 'text', 'sourcegen', 'architecture', 'server-abstractions', 'server-core', 'server-integration', 'aot') } else { @($suite) }
    $testArgs = @('--configuration', $configuration, '--framework', $framework, '--no-restore')
    if ($filter) { $testArgs += @('--filter', $filter) }

    if ($verbosity) {
        $testArgs += @('--logger', "console;verbosity=$verbosity")
        Write-Host "  Verbosity:     $verbosity"
    }

    Write-Heading "Test runner - $($toRun.Count) suite(s)"
    Write-Host "  Framework:     $framework"
    $fixedFrameworks = @($toRun |
        Where-Object { $testSuites.ContainsKey($_) -and $testSuites[$_].ContainsKey('Framework') } |
        ForEach-Object { $testSuites[$_].Framework } |
        Sort-Object -Unique)
    if ($fixedFrameworks.Count -gt 0) {
        Write-Host "  Fixed suites:  $($fixedFrameworks -join ', ')"
    }
    Write-Host "  Configuration: $configuration"
    if ($area)     { Write-Host "  Area:          $area" }
    if ($category) { Write-Host "  Category:      $category" }
    if ($filter)   { Write-Host "  Filter:        $filter" }
    if ($toRun -contains 'integration') { Write-Host "  Hang timeout:  $hangTimeout" }
    Write-Host ''

    $failed = @()
    $suiteNumber = 0
    $suiteCount = $toRun.Count
    foreach ($key in $toRun) {
        $suiteNumber++
        if (-not $testSuites.ContainsKey($key)) {
            Write-Failure "  [$suiteNumber/$suiteCount] Unknown test suite '$key'."
            $failed += $key
            continue
        }
        $ts = $testSuites[$key]
        if ($ts.ContainsKey('Command') -and $ts.Command -eq 'aot') {
            $processFileName = 'pwsh'
            $processArguments = @('-NoProfile', '-File', (Join-Path $repoRoot 'devops.ps1'), 'aot')
            if ($runtimeIdentifier) {
                $processArguments += @('-RuntimeIdentifier', $runtimeIdentifier)
            }
        } else {
            $projectPath = Join-Path $repoRoot $ts.Project
            $suiteArgs = @($testArgs)
            $suiteFramework = if ($ts.ContainsKey('Framework')) { $ts.Framework } else { $framework }
            if ($ts.ContainsKey('Framework')) {
                $suiteArgs = @('--configuration', $configuration, '--framework', $suiteFramework, '--no-restore')
                if ($filter) { $suiteArgs += @('--filter', $filter) }
                if ($verbosity) { $suiteArgs += @('--logger', "console;verbosity=$verbosity") }
            }
            if ($hangTimeout -ne 'off' -and $key -ne 'architecture') {
                $suiteArgs += @('--hangdump', '--hangdump-timeout', $hangTimeout)
            }
            $processFileName = 'dotnet'
            $processArguments = @('test', $projectPath) + $suiteArgs
        }
        $result = Invoke-TestProcessWithProgress -FileName $processFileName -Arguments $processArguments `
            -SuiteName $ts.Name -SuiteNumber $suiteNumber -SuiteCount $suiteCount
        $elapsed = $result.Elapsed.ToString('hh\:mm\:ss')
        if ($result.ExitCode -ne 0) {
            Write-Failure "  [$suiteNumber/$suiteCount] $($ts.Name) - FAILED ($elapsed)"
            $failed += $ts.Name
        } else {
            Write-Success "  [$suiteNumber/$suiteCount] $($ts.Name) - passed ($elapsed)"
        }
    }
    Write-Host ''
    if ($failed.Count -gt 0) {
        Write-Error "Failed suites: $($failed -join ', ')"
        exit 1
    }
    Write-Success 'All test suites passed.'
    exit 0
}

function Build-TraitFilter {
    param([string]$Area, [string]$Category)

    $parts = @()
    if ($Area) {
        $areas = @($Area -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($areas.Count -gt 0) {
            $parts += '(' + (($areas | ForEach-Object { "Area=$_" }) -join '|') + ')'
        }
    }
    if ($Category) {
        $cats = @($Category -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($cats.Count -gt 0) {
            $parts += '(' + (($cats | ForEach-Object { "Category=$_" }) -join '|') + ')'
        }
    }
    return ($parts -join '&')
}

function Invoke-AffectedTests {
    param(
        [string]$Framework,
        [string]$Configuration,
        [string]$Area,
        [string]$Category,
        [string]$Filter,
        [string]$Verbosity
    )

    $repoRoot = Get-RepoRoot
    $testSuites = Import-PowerShellDataFile "$PSScriptRoot/../config/test-suites.psd1"
    $codeAreas = Import-PowerShellDataFile "$PSScriptRoot/../config/code-areas.psd1"

    $dirty = @(Get-DirtyFiles $repoRoot)
    Write-Heading 'Affected test runner'
    Write-Host "  Dirty files:   $($dirty.Count)"
    Write-Host ''

    # suite key -> ordered set of TestArea names
    $targets = @{}
    foreach ($file in $dirty) {
        $normalised = $file.Replace('\', '/')
        foreach ($entryName in $codeAreas.Keys) {
            $entry = $codeAreas[$entryName]
            foreach ($glob in $entry.Globs) {
                if (Test-GlobMatch -Path $normalised -Glob $glob) {
                    foreach ($target in $entry.Targets) {
                        $suiteKey, $areaName = $target -split ':', 2
                        if (-not $targets.ContainsKey($suiteKey)) {
                            $targets[$suiteKey] = [System.Collections.Generic.HashSet[string]]::new()
                        }
                        [void]$targets[$suiteKey].Add($areaName)
                    }
                }
            }
        }
    }

    if ($targets.Count -eq 0) {
        Write-Failure 'No code-area mapping matched the dirty files. Refusing to run zero tests.'
        exit 1
    }

    $failed = @()
    $suiteKeys = @($targets.Keys | Sort-Object)
    $suiteNumber = 0
    $suiteCount = $suiteKeys.Count
    foreach ($suiteKey in $suiteKeys) {
        $suiteNumber++
        if (-not $testSuites.ContainsKey($suiteKey)) {
            Write-Failure "  [$suiteNumber/$suiteCount] Code-area mapping references unknown suite '$suiteKey'."
            $failed += $suiteKey
            continue
        }
        $areas = @($targets[$suiteKey]) | Sort-Object
        $areaFilter = '(' + (($areas | ForEach-Object { "Area=$_" }) -join '|') + ')'
        if ($Area) {
            $userArea = Build-TraitFilter -Area $Area -Category $Category
            if ($userArea) { $areaFilter = "$areaFilter&$userArea" }
        } elseif ($Category) {
            $areaFilter = "$areaFilter&$(Build-TraitFilter -Area '' -Category $Category)"
        }
        if ($Filter) { $areaFilter = "$Filter&$areaFilter" }

        $ts = $testSuites[$suiteKey]
        $projectPath = Join-Path $repoRoot $ts.Project
        $suiteFramework = if ($ts.ContainsKey('Framework')) { $ts.Framework } else { $Framework }
        $suiteArgs = @('--configuration', $Configuration, '--framework', $suiteFramework, '--no-restore', '--filter', $areaFilter)
        if ($Verbosity) { $suiteArgs += @('--logger', "console;verbosity=$Verbosity") }

        $suiteName = "$($ts.Name) (Area=$($areas -join ','))"
        $result = Invoke-TestProcessWithProgress -FileName 'dotnet' -Arguments (@('test', $projectPath) + $suiteArgs) `
            -SuiteName $suiteName -SuiteNumber $suiteNumber -SuiteCount $suiteCount
        $elapsed = $result.Elapsed.ToString('hh\:mm\:ss')
        if ($result.ExitCode -ne 0) {
            Write-Failure "  [$suiteNumber/$suiteCount] $suiteName - FAILED ($elapsed)"
            $failed += $ts.Name
        } else {
            Write-Success "  [$suiteNumber/$suiteCount] $suiteName - passed ($elapsed)"
        }
    }

    Write-Host ''
    if ($failed.Count -gt 0) {
        Write-Error "Failed suites: $($failed -join ', ')"
        exit 1
    }
    Write-Success 'All affected test suites passed.'
    exit 0
}

function Get-DirtyFiles {
    param([string]$RepoRoot)

    $files = @()
    $files += @(& git -C $RepoRoot diff --name-only 2>$null)
    $files += @(& git -C $RepoRoot diff --cached --name-only 2>$null)
    $files += @(& git -C $RepoRoot ls-files --others --exclude-standard 2>$null)
    return @($files | Where-Object { $_ } | Sort-Object -Unique)
}

function Test-GlobMatch {
    param([string]$Path, [string]$Glob)

    $pattern = ''
    $i = 0
    while ($i -lt $Glob.Length) {
        $c = $Glob[$i]
        if ($c -eq '*') {
            if (($i + 1) -lt $Glob.Length -and $Glob[$i + 1] -eq '*') {
                $pattern += '.*'
                $i++
            } else {
                $pattern += '[^/]*'
            }
        } elseif ($c -eq '?') {
            $pattern += '[^/]'
        } else {
            $pattern += [regex]::Escape([string]$c)
        }
        $i++
    }
    return $Path -match ("^$pattern$")
}
