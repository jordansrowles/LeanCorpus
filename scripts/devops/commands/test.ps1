$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Show-TestSuites {
    param([hashtable]$TestSuites = (Get-TestSuiteRegistry))

    Write-Host ''
    Write-Host '  Available test suites (-Suite):'
    Write-Host ''
    foreach ($key in Get-OrderedTestSuiteKeys -TestSuites $TestSuites -IncludeExplicitOnly) {
        $suite = $TestSuites[$key]
        $frameworks = @(Get-TestSuiteFrameworks -Suite $suite -SuiteKey $key)
        $runner = Get-TestSuiteRunnerKind -Suite $suite -SuiteKey $key
        $explicitOnly = $suite.ContainsKey('IncludeInAll') -and -not [bool]$suite.IncludeInAll
        $scope = if ($explicitOnly) { 'explicit only' } else { 'all' }
        Write-Host ("    {0,-20} {1} ({2}; {3}; {4})" -f $key, $suite.Name, $runner, ($frameworks -join ', '), $scope)
    }
    Write-Host '    all                  All test suites'
    Write-Host '    affected             Test suites for selected source areas'
    Write-Host ''
}

function Invoke-DevOpsTest {
    param([string[]]$Arguments = @())

    $uiEventPath = [string]$env:LEANCORPUS_DEVOPS_UI_EVENTS_FILE
    try {
        if ($Arguments.Count -gt 0 -and $Arguments[0] -eq 'backup-restore-matrix') {
            $matrixArguments = if ($Arguments.Count -gt 1) { $Arguments[1..($Arguments.Count - 1)] } else { @() }
            return Invoke-BackupRestoreMatrix -Arguments $matrixArguments
        }
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $repoRoot = Get-RepoRoot
        $testSuites = Get-TestSuiteRegistry

        $suiteValue = [string]$parsed.Get('Suite', '')
        if (-not $suiteValue -and $parsed.Positionals.Count -gt 0) {
            $suiteValue = [string]$parsed.Positionals[0]
        }
        $suite = @(ConvertTo-TestArgumentList $suiteValue)
        if ($suite.Count -eq 0) {
            $suite = @('all')
        }
        $suite = @($suite | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
        $isAffectedSuite = $suite.Count -eq 1 -and $suite[0] -eq 'affected'

        $commit = ''
        $range = ''
        if ($parsed.Has('Commit') -and $parsed.Has('Range')) { throw '-Commit and -Range are mutually exclusive.' }
        foreach ($option in @('Commit', 'Range')) {
            if ($parsed.Has($option)) {
                if (-not $isAffectedSuite) { throw "-$option is valid only for the affected suite." }
                $value = $parsed.Get($option, '')
                if ($value -is [bool] -or [string]::IsNullOrWhiteSpace([string]$value)) {
                    throw "-$option requires a value."
                }
                if ($option -eq 'Commit') { $commit = [string]$value } else { $range = [string]$value }
            }
        }

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
        $technique = [string]$parsed.Get('Technique', '')
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
        $noRestore = $parsed.Has('NoRestore')
        $failFast = $parsed.Has('FailFast')
        $ci = $parsed.Has('Ci')
        $collectCoverage = $parsed.Has('CollectCoverage')
        $explicitMode = if ($parsed.Has('ExplicitOnly')) { 'only' } elseif ($parsed.Has('Explicit')) { 'on' } else { 'off' }
        $parallelProfile = [string]$parsed.Get('Profile', '')
        if (-not $parallelProfile) {
            $parallelProfile = if ($category -eq 'Unit') { 'unit' } elseif ($flaky) { 'stress' } else { 'integration' }
        }
        if ($parallelProfile -notin @('unit', 'integration', 'stress')) {
            throw "Unknown test profile '$parallelProfile'. Valid: unit, integration, stress."
        }
        $verbosity = [string]$parsed.Get('Verbosity', '')
        $artifactsEnabled = $count -gt 1 -or $flaky -or $diagnostics -or $ci -or $collectCoverage -or
            -not [string]::IsNullOrWhiteSpace($uiEventPath)

        $affectedSelection = $null
        $resolutionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $targets = @(
            Resolve-TestTargets -Suite $suite -Framework $framework -FrameworkExplicit $frameworkWasSpecified `
                -Configuration $configuration -RuntimeIdentifier $runtimeIdentifier -Area $area `
                -Category $category -Technique $technique -Filter $filter -Ci $ci -CollectCoverage $collectCoverage `
                -Commit $commit -Range $range -AffectedSelection ([ref]$affectedSelection) `
                -AdditionalArguments @($parsed.PassThrough) -RepoRoot $repoRoot -TestSuites $testSuites
        )
        $resolutionStopwatch.Stop()
        if ($null -ne $affectedSelection) {
            $sourceValue = "$($affectedSelection.suppliedCommit)$($affectedSelection.suppliedRange)".Trim()
            Write-Info "Affected source: $($affectedSelection.mode) $sourceValue"
            Write-Info "Changed files: $($affectedSelection.paths.Count)"
            Write-Info "Targets: $($affectedSelection.targets -join ', ')"
        }
        Write-DevOpsUiTestEvent -Path $uiEventPath -Name 'TargetsResolved' -Data @{
            targets = @($targets | ForEach-Object { $_.Key })
            affected = if ($null -ne $affectedSelection) { $affectedSelection } else { $null }
        }
        if ($parsed.Has('ExplainAffected')) {
            if (-not $isAffectedSuite) { throw '-ExplainAffected is valid only for -Suite affected.' }
            Write-Host "Affected source: $($affectedSelection.mode)"
            Write-Host "Changed files: $($affectedSelection.paths.Count)"
            Write-Host "Selected targets: $($affectedSelection.targets -join ', ')"
            return 0
        }

        $options = [pscustomobject]@{
            AffectedSelection = $affectedSelection
            Count = $count
            Flaky = $flaky
            FailFast = $failFast
            Diagnostics = $diagnostics
            Ci = $ci
            CollectCoverage = $collectCoverage
            ExplicitMode = $explicitMode
            ParallelProfile = $parallelProfile
            FailWarnings = $parsed.Has('FailWarnings')
            ArtifactsEnabled = $artifactsEnabled
            Configuration = $configuration
            NoRestore = $noRestore
            RequestedFramework = if ($frameworkWasSpecified) { $framework } else { '' }
            RuntimeIdentifier = $runtimeIdentifier
            Area = $area
            Category = $category
            Technique = $technique
            UiEventPath = $uiEventPath
            Filter = $filter
            Verbosity = $verbosity
            HangTimeout = $hangTimeout.ToLowerInvariant()
            ProcessTimeout = $processTimeout
            ResolutionDuration = $resolutionStopwatch.Elapsed
            PassThrough = @($parsed.PassThrough)
        }

        $displayName = if ($flaky) { 'Flaky test run' } else { 'Test run' }
        $commandLine = ConvertTo-CommandLineText -Command 'pwsh ./devops.ps1 test' -Arguments $Arguments
        return Invoke-TestPipeline -Targets $targets -Options $options -CommandLine $commandLine `
            -DisplayName $displayName -RepoRoot $repoRoot
    } catch {
        Write-DevOpsUiTestEvent -Path $uiEventPath -Name 'RunError' -Data @{ message = $_.Exception.Message }
        Write-Failure "Test command failed: $($_.Exception.Message)"
        return 1
    }
}
