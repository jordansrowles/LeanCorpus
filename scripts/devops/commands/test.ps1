$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Show-TestSuites {
    param([hashtable]$TestSuites = (Get-TestSuiteRegistry))

    Write-Host ''
    Write-Host '  Available test suites (-Suite):'
    Write-Host ''
    foreach ($key in Get-OrderedTestSuiteKeys -TestSuites $TestSuites) {
        $suite = $TestSuites[$key]
        $frameworks = @(Get-TestSuiteFrameworks -Suite $suite -SuiteKey $key)
        $runner = Get-TestSuiteRunnerKind -Suite $suite -SuiteKey $key
        Write-Host ("    {0,-20} {1} ({2}; {3})" -f $key, $suite.Name, $runner, ($frameworks -join ', '))
    }
    Write-Host '    all                  All test suites'
    Write-Host '    affected             Test suites for dirty source areas'
    Write-Host ''
}

function Invoke-DevOpsTest {
    param([string[]]$Arguments = @())

    try {
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $repoRoot = Get-RepoRoot
        $testSuites = Get-TestSuiteRegistry

        $suite = $parsed.Get('Suite', '')
        if (-not $suite -and $parsed.Positionals.Count -gt 0) {
            $suite = $parsed.Positionals[0]
        }
        if (-not $suite) {
            $suite = 'all'
        }
        $suite = ([string]$suite).ToLowerInvariant()

        if ($parsed.Has('List')) {
            Show-TestSuites -TestSuites $testSuites
            return 0
        }

        $countWasSpecified = $parsed.Has('Count')
        $flaky = $parsed.Has('Flaky')
        $countValue = if ($countWasSpecified) {
            $parsed.Get('Count', '')
        } elseif ($flaky) {
            30
        } else {
            1
        }
        $count = ConvertTo-TestCount -Value $countValue

        $frameworkWasSpecified = $parsed.Has('Framework')
        $framework = [string]$parsed.Get('Framework', (Get-DefaultFramework))
        $configuration = [string]$parsed.Get('Configuration', 'Release')
        $runtimeIdentifier = [string]$parsed.Get('RuntimeIdentifier', '')
        $area = [string]$parsed.Get('Area', '')
        $category = [string]$parsed.Get('Category', '')
        $filter = [string]$parsed.Get('Filter', '')
        $hangTimeout = [string]$parsed.Get('HangTimeout', '100s')
        if ([string]::IsNullOrWhiteSpace($hangTimeout)) {
            throw '--HangTimeout requires a value, or use off to disable hang dumps.'
        }

        $timeoutValue = if ($parsed.Has('Timeout')) {
            [string]$parsed.Get('Timeout', 'off')
        } elseif ($parsed.Has('ProcessTimeout')) {
            [string]$parsed.Get('ProcessTimeout', 'off')
        } else {
            'off'
        }
        $processTimeout = ConvertTo-ProcessTimeout -Value $timeoutValue

        $diagnostics = $parsed.Has('Diagnostics')
        $failFast = $parsed.Has('FailFast')
        $ci = $parsed.Has('Ci')
        $collectCoverage = $parsed.Has('CollectCoverage')
        $verbosity = [string]$parsed.Get('Verbosity', '')
        $artifactsEnabled = $count -gt 1 -or $flaky -or $diagnostics -or $ci -or $collectCoverage

        $resolutionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $targets = @(
            Resolve-TestTargets -Suite $suite -Framework $framework -FrameworkExplicit $frameworkWasSpecified `
                -Configuration $configuration -RuntimeIdentifier $runtimeIdentifier -Area $area `
                -Category $category -Filter $filter -Ci $ci -CollectCoverage $collectCoverage `
                -AdditionalArguments @($parsed.PassThrough) -RepoRoot $repoRoot -TestSuites $testSuites
        )
        $resolutionStopwatch.Stop()

        $options = [pscustomobject]@{
            Count = $count
            Flaky = $flaky
            FailFast = $failFast
            Diagnostics = $diagnostics
            Ci = $ci
            CollectCoverage = $collectCoverage
            ArtifactsEnabled = $artifactsEnabled
            Configuration = $configuration
            RequestedFramework = if ($frameworkWasSpecified) { $framework } else { '' }
            RuntimeIdentifier = $runtimeIdentifier
            Area = $area
            Category = $category
            Filter = $filter
            Verbosity = $verbosity
            HangTimeout = $hangTimeout.ToLowerInvariant()
            ProcessTimeout = $processTimeout
            ResolutionDuration = $resolutionStopwatch.Elapsed
            PassThrough = @($parsed.PassThrough)
        }

        $displayName = if ($flaky) { 'Flaky test run' } else { 'Test run' }
        $commandLine = ConvertTo-CommandLineText -Command './devops test' -Arguments $Arguments
        return Invoke-TestPipeline -Targets $targets -Options $options -CommandLine $commandLine `
            -DisplayName $displayName -RepoRoot $repoRoot
    } catch {
        Write-Failure "Test command failed: $($_.Exception.Message)"
        return 1
    }
}
