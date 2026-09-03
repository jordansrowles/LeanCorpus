$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsAot {
    param([string[]]$Arguments = @())

    try {
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $repoRoot = Get-RepoRoot
        $frameworkWasSpecified = $parsed.Has('Framework')
        $framework = [string]$parsed.Get('Framework', '')
        $runtimeIdentifier = [string]$parsed.Get('RuntimeIdentifier', '')
        $configuration = [string]$parsed.Get('Configuration', 'Release')
        $flaky = $parsed.Has('Flaky')
        $countWasSpecified = $parsed.Has('Count')
        $countValue = if ($countWasSpecified) {
            $parsed.Get('Count', '')
        } elseif ($flaky) {
            30
        } else {
            1
        }
        $count = ConvertTo-TestCount -Value $countValue
        $diagnostics = $parsed.Has('Diagnostics')
        $failFast = $parsed.Has('FailFast')
        $ci = $parsed.Has('Ci')
        $timeoutValue = if ($parsed.Has('Timeout')) {
            [string]$parsed.Get('Timeout', 'off')
        } else {
            'off'
        }
        $processTimeout = ConvertTo-ProcessTimeout -Value $timeoutValue
        $resolutionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $targets = @(
            Resolve-TestTargets -Suite 'aot' -Framework $framework -FrameworkExplicit $frameworkWasSpecified `
                -Configuration $configuration -RuntimeIdentifier $runtimeIdentifier `
                -Filter ([string]$parsed.Get('Filter', '')) -Ci $ci `
                -AdditionalArguments @($parsed.PassThrough) -RepoRoot $repoRoot
        )
        $resolutionStopwatch.Stop()

        $options = [pscustomobject]@{
            Count = $count
            Flaky = $flaky
            FailFast = $failFast
            Diagnostics = $diagnostics
            Ci = $ci
            CollectCoverage = $false
            ArtifactsEnabled = $count -gt 1 -or $flaky -or $diagnostics -or $ci
            Configuration = $configuration
            RequestedFramework = if ($frameworkWasSpecified) { $framework } else { '' }
            RuntimeIdentifier = $runtimeIdentifier
            Area = [string]$parsed.Get('Area', '')
            Category = [string]$parsed.Get('Category', '')
            Filter = [string]$parsed.Get('Filter', '')
            Verbosity = [string]$parsed.Get('Verbosity', '')
            HangTimeout = 'off'
            ProcessTimeout = $processTimeout
            ResolutionDuration = $resolutionStopwatch.Elapsed
            PassThrough = @($parsed.PassThrough)
        }

        $commandLine = ConvertTo-CommandLineText -Command './devops aot' -Arguments $Arguments
        return Invoke-TestPipeline -Targets $targets -Options $options -CommandLine $commandLine `
            -DisplayName 'AOT test run' -RepoRoot $repoRoot
    } catch {
        Write-Failure "AOT command failed: $($_.Exception.Message)"
        return 1
    }
}
