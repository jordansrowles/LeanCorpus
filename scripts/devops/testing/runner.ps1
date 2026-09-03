$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-CommandLineText {
    param(
        [string]$Command,
        [string[]]$Arguments = @()
    )

    $parts = [System.Collections.Generic.List[string]]::new()
    [void]$parts.Add($Command)
    foreach ($argument in @($Arguments)) {
        $value = [string]$argument
        if ($value -match '[\s"]') {
            [void]$parts.Add('"' + $value.Replace('"', '\"') + '"')
        } else {
            [void]$parts.Add($value)
        }
    }
    return ($parts -join ' ')
}

function ConvertTo-TestCount {
    param(
        [object]$Value,
        [string]$ParameterName = 'count'
    )

    $count = 0
    $parsed = [int]::TryParse(
        [string]$Value,
        [Globalization.NumberStyles]::Integer,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$count)
    if (-not $parsed -or $count -lt 1) {
        throw "--$ParameterName must be a positive integer."
    }

    return $count
}

function ConvertTo-ProcessTimeout {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -eq 'off') {
        return [TimeSpan]::Zero
    }

    $match = [regex]::Match($Value.Trim(), '^(?<number>\d+(?:\.\d+)?)(?<unit>ms|s|m|h|d)$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        throw "--timeout must use a value such as 30s, 2m, or 500ms, or off."
    }

    $number = [double]::Parse($match.Groups['number'].Value, [Globalization.CultureInfo]::InvariantCulture)
    switch ($match.Groups['unit'].Value.ToLowerInvariant()) {
        'ms' { return [TimeSpan]::FromMilliseconds($number) }
        's'  { return [TimeSpan]::FromSeconds($number) }
        'm'  { return [TimeSpan]::FromMinutes($number) }
        'h'  { return [TimeSpan]::FromHours($number) }
        'd'  { return [TimeSpan]::FromDays($number) }
    }

    throw "Unsupported process timeout unit in '$Value'."
}

function Invoke-TestPipeline {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Targets,
        [Parameter(Mandatory = $true)]
        [object]$Options,
        [Parameter(Mandatory = $true)]
        [string]$CommandLine,
        [string]$DisplayName = 'Test run',
        [string]$RepoRoot = (Get-RepoRoot)
    )

    $context = New-TestRunContext -Options $Options -Targets $Targets `
        -CommandLine $CommandLine -RepoRoot $RepoRoot
    $summary = $null
    $pipelineError = $null
    $reportError = $false

    Write-Heading $DisplayName
    Write-Host "  Targets:       $($Targets.Count)"
    Write-Host "  Iterations:    $($Options.Count)"
    Write-Host "  Configuration: $($Options.Configuration)"
    if ($Options.RequestedFramework) {
        Write-Host "  Framework:     $($Options.RequestedFramework)"
    }
    if ($Options.Flaky) {
        Write-Host '  Mode:          flaky diagnostics preset'
    }
    if ($Options.Ci) {
        Write-Host '  Mode:          CI prepared-output execution'
    }
    Write-Host ''

    try {
        if ($Options.ResolutionDuration -gt [TimeSpan]::Zero) {
            Add-TestStageTiming -Context $context -Name 'Resolution' -Duration $Options.ResolutionDuration
        }

        $preparation = Prepare-TestTargets -Targets $Targets -Options $Options -RepoRoot $RepoRoot
        if ($preparation.RestoreDuration -gt [TimeSpan]::Zero) {
            Add-TestStageTiming -Context $context -Name 'Restore' -Duration $preparation.RestoreDuration
        }
        if ($preparation.BuildDuration -gt [TimeSpan]::Zero) {
            Add-TestStageTiming -Context $context -Name 'Build' -Duration $preparation.BuildDuration
        }
        if ($preparation.AotPublishDuration -gt [TimeSpan]::Zero) {
            Add-TestStageTiming -Context $context -Name 'AOTPublish' -Duration $preparation.AotPublishDuration
        }

        $executionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $executionNumber = 0
        $stopScheduling = $false
        for ($iteration = 1; $iteration -le $Options.Count -and -not $stopScheduling; $iteration++) {
            foreach ($preparedTarget in @($preparation.Targets)) {
                if ($stopScheduling) {
                    break
                }

                $executionNumber++
                try {
                    $execution = Invoke-TestTarget -PreparedTarget $preparedTarget -Iteration $iteration `
                        -Context $context -ExecutionNumber $executionNumber `
                        -ExecutionCount ($Targets.Count * $Options.Count)
                } catch {
                    $message = "Target '$($preparedTarget.Target.Key)' iteration $iteration failed in the execution infrastructure: $($_.Exception.Message)"
                    [void]$context.InfrastructureErrors.Add($message)
                    $execution = New-InfrastructureExecutionResult -Target $preparedTarget.Target `
                        -Iteration $iteration -ErrorMessage $message
                    Write-Failure "  [$executionNumber/$($Targets.Count * $Options.Count)] $message"
                    if ($context.ArtifactsEnabled) {
                        try {
                            $errorDirectory = Get-TestTargetArtifactDirectory -Context $context `
                                -Iteration $iteration -Target $preparedTarget.Target
                            Write-AtomicTextFile -Path (Join-Path $errorDirectory 'infrastructure-error.txt') -Content $message
                        } catch {
                            [void]$context.ReportErrors.Add("Could not preserve execution error: $($_.Exception.Message)")
                        }
                    }
                }

                [void]$context.ExecutionResults.Add($execution)
                foreach ($coverageError in @(Copy-TestCoverageResults -Context $context -Execution $execution)) {
                    [void]$context.ReportErrors.Add($coverageError)
                    Write-Warn "  $coverageError"
                }
                if ($execution.ResultParsingDuration -gt [TimeSpan]::Zero) {
                    $context.ResultParsingDuration = $context.ResultParsingDuration + $execution.ResultParsingDuration
                }
                try {
                    Write-TestRunCheckpoint -Context $context -Status 'Running'
                } catch {
                    [void]$context.ReportErrors.Add("Checkpoint update failed after $($preparedTarget.Target.Key) iteration $($iteration): $($_.Exception.Message)")
                    Write-Warn "  Checkpoint update failed: $($_.Exception.Message)"
                }

                if ($Options.FailFast -and $execution.Outcome -ne 'Passed') {
                    $stopScheduling = $true
                    Write-Warn '  Fail-fast stopped scheduling new target executions.'
                }
            }
        }
        $executionStopwatch.Stop()
        if ($executionStopwatch.Elapsed -gt [TimeSpan]::Zero) {
            Add-TestStageTiming -Context $context -Name 'TestExecution' -Duration $executionStopwatch.Elapsed
        }
        if ($context.ResultParsingDuration -gt [TimeSpan]::Zero) {
            Add-TestStageTiming -Context $context -Name 'ResultParsing' -Duration $context.ResultParsingDuration
        }
    } catch {
        $pipelineError = $_.Exception
        [void]$context.InfrastructureErrors.Add("Test pipeline failed: $($_.Exception.Message)")
        Write-Failure "Test pipeline failed: $($_.Exception.Message)"
    } finally {
        $context.EndTimeUtc = [DateTime]::UtcNow
        try {
            $summary = New-TestRunSummary -Context $context -Targets $Targets `
                -ExecutionResults @($context.ExecutionResults)
        } catch {
            $reportError = $true
            [void]$context.ReportErrors.Add("Summary calculation failed: $($_.Exception.Message)")
            Write-Failure "Summary calculation failed: $($_.Exception.Message)"
        }

        if ($context.ArtifactsEnabled) {
            if ($null -ne $summary) {
                try {
                    Write-TestRunReports -Context $context -Summary $summary
                } catch {
                    $reportError = $true
                    [void]$context.ReportErrors.Add("Report generation failed: $($_.Exception.Message)")
                    Write-Failure "Report generation failed: $($_.Exception.Message)"
                }
            }
            try {
                if ($null -ne $summary) {
                    Update-TestRunManifest -Context $context -Summary $summary
                }
                $finalStatus = if ($null -eq $pipelineError) { 'Completed' } else { 'Failed' }
                Write-TestRunCheckpoint -Context $context -Status $finalStatus
            } catch {
                $reportError = $true
                [void]$context.ReportErrors.Add("Final artefact update failed: $($_.Exception.Message)")
                Write-Failure "Final artefact update failed: $($_.Exception.Message)"
            }
        }
    }

    if ($null -eq $summary) {
        return 1
    }

    Write-Host ''
    Write-Heading 'Test results'
    Write-Host "  Passed:        $($summary.PassedTargetExecutions)/$($summary.RequestedTargetExecutions) target executions"
    Write-Host "  Failed:        $($summary.FailedTargetExecutions)"
    Write-Host "  Crashed:       $($summary.CrashedTargetExecutions)"
    Write-Host "  Hung:          $($summary.HungTargetExecutions)"
    Write-Host "  Incomplete:    $($summary.IncompleteTargetExecutions)"
    Write-Host "  Infrastructure: $($summary.InfrastructureErrorExecutions)"
    Write-Host ("  Failure rate:  {0:P1}" -f $summary.FailureRate)
    if ($summary.FailingIterations.Count -gt 0) {
        Write-Host "  Failed runs:   $($summary.FailingIterations -join ', ')"
    }
    if ($context.ArtifactsEnabled) {
        Write-Host "  Report:        $(Join-Path $context.RunDirectory 'summary.md')"
    }

    if ($null -ne $pipelineError -or $reportError -or $context.ReportErrors.Count -gt 0 -or -not $summary.Succeeded) {
        return 1
    }

    return 0
}
