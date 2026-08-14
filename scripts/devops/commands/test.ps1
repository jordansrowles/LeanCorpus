$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsTest {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $configuration = $parsed.Get('Configuration', 'Release')
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

    [string[]]$toRun = if ($suite -eq 'all') { @('core', 'text', 'sourcegen', 'architecture') } else { @($suite) }
    $testArgs = @('--configuration', $configuration, '--framework', $framework, '--no-restore')
    if ($filter) { $testArgs += @('--filter', $filter) }

    if ($verbosity) {
        $testArgs += @('--logger', "console;verbosity=$verbosity")
        Write-Host "  Verbosity:     $verbosity"
    }

    Write-Heading "Test runner - $($toRun.Count) suite(s)"
    Write-Host "  Framework:     $framework"
    Write-Host "  Configuration: $configuration"
    if ($area)     { Write-Host "  Area:          $area" }
    if ($category) { Write-Host "  Category:      $category" }
    if ($filter)   { Write-Host "  Filter:        $filter" }
    if ($toRun -contains 'integration') { Write-Host "  Hang timeout:  $hangTimeout" }
    Write-Host ''

    $failed = @()
    foreach ($key in $toRun) {
        if (-not $testSuites.ContainsKey($key)) {
            Write-Failure "Unknown test suite '$key'."
            $failed += $key
            continue
        }
        $ts = $testSuites[$key]
        $projectPath = Join-Path $repoRoot $ts.Project
        $suiteArgs = @($testArgs)
        if ($key -eq 'integration' -and $hangTimeout -ne 'off') {
            $suiteArgs += @('--blame-hang', '--blame-hang-timeout', $hangTimeout, '--blame-hang-dump-type', 'none')
        }
        Write-Info "  $($ts.Name)..."
        dotnet test $projectPath @suiteArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Failure "  $($ts.Name) - FAILED"
            $failed += $ts.Name
        } else {
            Write-Success "  $($ts.Name) - passed"
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
    foreach ($suiteKey in $targets.Keys | Sort-Object) {
        if (-not $testSuites.ContainsKey($suiteKey)) {
            Write-Failure "Code-area mapping references unknown suite '$suiteKey'."
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
        $suiteArgs = @('--configuration', $Configuration, '--framework', $Framework, '--no-restore', '--filter', $areaFilter)
        if ($Verbosity) { $suiteArgs += @('--logger', "console;verbosity=$Verbosity") }

        Write-Info "  $($ts.Name) (Area=$($areas -join ','))..."
        dotnet test $projectPath @suiteArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Failure "  $($ts.Name) - FAILED"
            $failed += $ts.Name
        } else {
            Write-Success "  $($ts.Name) - passed"
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
