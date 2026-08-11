$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsTest {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $configuration = $parsed.Get('Configuration', 'Release')
    $suite = $parsed.Get('Suite', 'all')
    $filter = $parsed.Get('Filter', '')
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
        Write-Host ''
        exit 0
    }

    [string[]]$toRun = if ($suite -eq 'all') { [string[]]($testSuites.Keys) } else { @($suite) }
    $testArgs = @('--configuration', $configuration, '--framework', $framework, '--no-restore')
    if ($filter) { $testArgs += @('--filter', $filter) }

    if ($verbosity) {
        $testArgs += @('--logger', "console;verbosity=$verbosity")
        Write-Host "  Verbosity:     $verbosity"
    }

    Write-Heading "Test runner - $($toRun.Count) suite(s)"
    Write-Host "  Framework:     $framework"
    Write-Host "  Configuration: $configuration"
    if ($filter) { Write-Host "  Filter:        $filter" }
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
