$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Format-TestDuration {
    param([double]$Milliseconds)

    if ($Milliseconds -lt 1000) {
        return ("{0:0.###} ms" -f $Milliseconds)
    }
    return ("{0:0.###} s" -f ($Milliseconds / 1000.0))
}

function ConvertTo-TestReportTestDocument {
    param([object]$Test)

    return [ordered]@{
        targetKey = $Test.TargetKey
        suite = $Test.Suite
        framework = $Test.Framework
        testId = $Test.TestId
        testName = $Test.TestName
        className = $Test.ClassName
        methodName = $Test.MethodName
        identity = $Test.Identity
        identityStrength = $Test.IdentityStrength
        observationCount = [int]$Test.ObservationCount
        expectedObservationCount = [int]$Test.ExpectedObservationCount
        passedCount = [int]$Test.PassedCount
        failedCount = [int]$Test.FailedCount
        failedIterations = @($Test.FailedIterations)
        classification = $Test.Classification
        maxDurationMs = [double]$Test.MaxDurationMs
    }
}

function Get-TestEnvironmentDocument {
    param([object]$Context)

    if (-not $Context.EnvironmentPath -or -not (Test-Path -LiteralPath $Context.EnvironmentPath)) {
        return [ordered]@{}
    }

    $environment = Get-Content -LiteralPath $Context.EnvironmentPath -Raw | ConvertFrom-Json
    return [ordered]@{
        os = $environment.os
        architecture = $environment.architecture
        processArchitecture = $environment.processArchitecture
        processorName = $environment.processorName
        logicalProcessorCount = $environment.logicalProcessorCount
        availableMemoryBytes = $environment.availableMemoryBytes
        sdkVersion = $environment.sdkVersion
        gitCommit = $environment.gitCommit
        gitBranch = $environment.gitBranch
        gitDirty = [bool]$environment.gitDirty
        environment = $environment.environment
    }
}

function ConvertTo-TestPreparationTimingDocument {
    param([object]$Timing)

    return [ordered]@{
        stage = $Timing.Stage
        operation = $Timing.Operation
        workItem = $Timing.WorkItem
        targetKeys = @($Timing.TargetKeys)
        durationMs = [double]$Timing.DurationMs
    }
}

function New-TestReportDocument {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    return [ordered]@{
        schemaVersion = 1
        runId = $Context.RunId
        commandLine = $Context.CommandLine
        startTimeUtc = $Context.StartTimeUtc.ToString('O')
        endTimeUtc = if ($Context.EndTimeUtc) { $Context.EndTimeUtc.ToString('O') } else { $null }
        environment = Get-TestEnvironmentDocument -Context $Context
        options = [ordered]@{
            configuration = $Context.Options.Configuration
            requestedFramework = $Context.Options.RequestedFramework
            runtimeIdentifier = $Context.Options.RuntimeIdentifier
            count = [int]$Context.Options.Count
            flaky = [bool]$Context.Options.Flaky
            diagnostics = [bool]$Context.Options.Diagnostics
            failFast = [bool]$Context.Options.FailFast
            ci = [bool]$Context.Options.Ci
            collectCoverage = [bool]$Context.Options.CollectCoverage
        }
        selectedTargets = @($Context.Targets | ForEach-Object { ConvertTo-TestTargetDocument $_ })
        stageTimings = @($Context.StageTimings)
        preparationTimings = @($Context.PreparationTimings | ForEach-Object {
            ConvertTo-TestPreparationTimingDocument $_
        })
        summary = [ordered]@{
            succeeded = [bool]$Summary.Succeeded
            requestedIterations = [int]$Summary.RequestedIterations
            completedIterations = [int]$Summary.CompletedIterations
            requestedTargetExecutions = [int]$Summary.RequestedTargetExecutions
            scheduledTargetExecutions = [int]$Summary.ScheduledTargetExecutions
            completedTargetExecutions = [int]$Summary.CompletedTargetExecutions
            passedTargetExecutions = [int]$Summary.PassedTargetExecutions
            failedTargetExecutions = [int]$Summary.FailedTargetExecutions
            crashedTargetExecutions = [int]$Summary.CrashedTargetExecutions
            hungTargetExecutions = [int]$Summary.HungTargetExecutions
            cancelledTargetExecutions = [int]$Summary.CancelledTargetExecutions
            incompleteTargetExecutions = [int]$Summary.IncompleteTargetExecutions
            infrastructureErrorExecutions = [int]$Summary.InfrastructureErrorExecutions
            failureRate = [double]$Summary.FailureRate
            failingIterations = @($Summary.FailingIterations)
            minimumDurationMs = [double]$Summary.MinimumDurationMs
            medianDurationMs = [double]$Summary.MedianDurationMs
            p95DurationMs = [double]$Summary.P95DurationMs
            maximumDurationMs = [double]$Summary.MaximumDurationMs
            totalDurationMs = [double]$Summary.TotalDurationMs
            outcomeCounts = $Summary.OutcomeCounts
        }
        targetResults = @($Summary.TargetResults)
        tests = @($Summary.Tests | ForEach-Object { ConvertTo-TestReportTestDocument $_ })
        intermittentFailures = @($Summary.IntermittentFailures | ForEach-Object { ConvertTo-TestReportTestDocument $_ })
        alwaysFailingTests = @($Summary.AlwaysFailingTests | ForEach-Object { ConvertTo-TestReportTestDocument $_ })
        incompleteTests = @($Summary.IncompleteTests | ForEach-Object { ConvertTo-TestReportTestDocument $_ })
        slowestTests = @($Summary.SlowestTests | ForEach-Object { ConvertTo-TestReportTestDocument $_ })
        crashedTargets = @($Summary.CrashedTargets)
        hungTargets = @($Summary.HungTargets)
        cancelledTargets = @($Summary.CancelledTargets)
        incompleteTargets = @($Summary.IncompleteTargets)
        infrastructureErrors = @($Summary.InfrastructureErrors | ForEach-Object {
            ConvertTo-TestReportText -Context $Context -Value $_
        })
        diagnosticArtifactPaths = @($Summary.DiagnosticArtifactPaths | ForEach-Object {
            Get-TestArtifactRelativePath -Context $Context -Path $_
        } | Where-Object { $_ })
        reportErrors = @($Context.ReportErrors | ForEach-Object {
            ConvertTo-TestReportText -Context $Context -Value $_
        })
    }
}

function ConvertTo-MarkdownCell {
    param([object]$Value)

    if ($null -eq $Value) {
        return ''
    }

    return ([string]$Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function New-TestMarkdownReport {
    param(
        [object]$Context,
        [object]$Summary
    )

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine('# Test run summary')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('- Run ID: `' + (ConvertTo-MarkdownCell $Context.RunId) + '`')
    [void]$builder.AppendLine('- Command: `' + (ConvertTo-MarkdownCell $Context.CommandLine) + '`')
    [void]$builder.AppendLine("- Started: $($Context.StartTimeUtc.ToString('O'))")
    if ($Context.EndTimeUtc) {
        [void]$builder.AppendLine("- Finished: $($Context.EndTimeUtc.ToString('O'))")
    }
    [void]$builder.AppendLine("- Result: **$(if ($Summary.Succeeded) { 'Passed' } else { 'Not passed' })**")
    [void]$builder.AppendLine()

    [void]$builder.AppendLine('## Environment')
    [void]$builder.AppendLine()
    $environment = Get-TestEnvironmentDocument -Context $Context
    [void]$builder.AppendLine("- OS: $(ConvertTo-MarkdownCell $environment.os)")
    [void]$builder.AppendLine("- Architecture: $(ConvertTo-MarkdownCell $environment.architecture)")
    [void]$builder.AppendLine("- SDK: $(ConvertTo-MarkdownCell $environment.sdkVersion)")
    [void]$builder.AppendLine('- Commit: `' + (ConvertTo-MarkdownCell $environment.gitCommit) + '`')
    [void]$builder.AppendLine('- Branch: `' + (ConvertTo-MarkdownCell $environment.gitBranch) + '`')
    [void]$builder.AppendLine()

    [void]$builder.AppendLine('## Targets')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('| Target | Runner | Framework | RID | Filter |')
    [void]$builder.AppendLine('| --- | --- | --- | --- | --- |')
    foreach ($target in @($Context.Targets)) {
        [void]$builder.AppendLine("| $(ConvertTo-MarkdownCell $target.Key) | $(ConvertTo-MarkdownCell $target.RunnerKind) | $(ConvertTo-MarkdownCell $target.Framework) | $(ConvertTo-MarkdownCell $target.RuntimeIdentifier) | $(ConvertTo-MarkdownCell $target.Filter) |")
    }
    [void]$builder.AppendLine()

    [void]$builder.AppendLine('## Timing')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("- Requested iterations: $($Summary.RequestedIterations)")
    [void]$builder.AppendLine("- Completed iterations: $($Summary.CompletedIterations)")
    [void]$builder.AppendLine("- Scheduled target executions: $($Summary.ScheduledTargetExecutions)/$($Summary.RequestedTargetExecutions)")
    [void]$builder.AppendLine("- Completed target executions: $($Summary.CompletedTargetExecutions)")
    [void]$builder.AppendLine("- Failure rate: {0:P1}" -f $Summary.FailureRate)
    [void]$builder.AppendLine("- Target duration: min $(Format-TestDuration $Summary.MinimumDurationMs), median $(Format-TestDuration $Summary.MedianDurationMs), P95 $(Format-TestDuration $Summary.P95DurationMs), max $(Format-TestDuration $Summary.MaximumDurationMs)")
    [void]$builder.AppendLine("- Total duration: $(Format-TestDuration $Summary.TotalDurationMs)")
    [void]$builder.AppendLine()
    if ($Context.StageTimings.Count -gt 0) {
        [void]$builder.AppendLine('| Stage | Duration |')
        [void]$builder.AppendLine('| --- | ---: |')
        foreach ($stage in @($Context.StageTimings)) {
            [void]$builder.AppendLine("| $(ConvertTo-MarkdownCell $stage.name) | $(Format-TestDuration $stage.durationMs) |")
        }
        [void]$builder.AppendLine()
    }
    if ($Context.PreparationTimings.Count -gt 0) {
        [void]$builder.AppendLine('### Preparation details')
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('| Stage | Operation | Work item | Targets | Duration |')
        [void]$builder.AppendLine('| --- | --- | --- | --- | ---: |')
        foreach ($timing in @($Context.PreparationTimings)) {
            [void]$builder.AppendLine("| $(ConvertTo-MarkdownCell $timing.Stage) | $(ConvertTo-MarkdownCell $timing.Operation) | $(ConvertTo-MarkdownCell $timing.WorkItem) | $(ConvertTo-MarkdownCell (@($timing.TargetKeys) -join ', ')) | $(Format-TestDuration $timing.DurationMs) |")
        }
        [void]$builder.AppendLine()
    }

    [void]$builder.AppendLine('## Target outcomes')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('| Iteration | Target | Outcome | Duration |')
    [void]$builder.AppendLine('| ---: | --- | --- | ---: |')
    foreach ($result in @($Summary.TargetResults)) {
        [void]$builder.AppendLine("| $($result.iteration) | $(ConvertTo-MarkdownCell $result.targetKey) | $(ConvertTo-MarkdownCell $result.outcome) | $(Format-TestDuration $result.durationMs) |")
    }
    [void]$builder.AppendLine()

    [void]$builder.AppendLine('## Counts')
    [void]$builder.AppendLine()
    foreach ($name in @('Passed', 'Failed', 'Crashed', 'Hung', 'Cancelled', 'Incomplete', 'InfrastructureError')) {
        [void]$builder.AppendLine("- $($name): $($Summary.OutcomeCounts[$name])")
    }
    if (@($Summary.FailingIterations).Count -gt 0) {
        [void]$builder.AppendLine("- Failing iterations: $($Summary.FailingIterations -join ', ')")
    }
    [void]$builder.AppendLine()

    foreach ($section in @(
        @{ Title = 'Intermittent failures'; Items = @($Summary.IntermittentFailures); Property = 'Classification' },
        @{ Title = 'Always-failing tests'; Items = @($Summary.AlwaysFailingTests); Property = 'Classification' },
        @{ Title = 'Incomplete tests'; Items = @($Summary.IncompleteTests); Property = 'Classification' },
        @{ Title = 'Slowest tests'; Items = @($Summary.SlowestTests); Property = 'MaxDurationMs' }
    )) {
        [void]$builder.AppendLine("## $($section.Title)")
        [void]$builder.AppendLine()
        if ($section.Items.Count -eq 0) {
            [void]$builder.AppendLine('None.')
        } else {
            [void]$builder.AppendLine('| Test | Target | Classification | Detail |')
            [void]$builder.AppendLine('| --- | --- | --- | --- |')
            foreach ($test in $section.Items) {
                $detail = if ($section.Title -eq 'Slowest tests') {
                    Format-TestDuration $test.MaxDurationMs
                } else {
                    "$($test.FailedCount)/$($test.ObservationCount); runs $($test.FailedIterations -join ', ')"
                }
                [void]$builder.AppendLine("| $(ConvertTo-MarkdownCell $test.TestName) | $(ConvertTo-MarkdownCell $test.TargetKey) | $(ConvertTo-MarkdownCell $test.Classification) | $(ConvertTo-MarkdownCell $detail) |")
            }
        }
        [void]$builder.AppendLine()
    }

    foreach ($section in @(
        @{ Title = 'Crashed targets'; Items = @($Summary.CrashedTargets) },
        @{ Title = 'Hung targets'; Items = @($Summary.HungTargets) },
        @{ Title = 'Incomplete targets'; Items = @($Summary.IncompleteTargets) }
    )) {
        [void]$builder.AppendLine("## $($section.Title)")
        [void]$builder.AppendLine()
        if ($section.Items.Count -eq 0) {
            [void]$builder.AppendLine('None.')
        } else {
            foreach ($item in $section.Items) {
                [void]$builder.AppendLine('- iteration ' + $item.iteration + ': `' + (ConvertTo-MarkdownCell $item.targetKey) + '`')
            }
        }
        [void]$builder.AppendLine()
    }

    [void]$builder.AppendLine('## Diagnostics and artefacts')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('- Run directory: `artifacts/test/runs/' + (ConvertTo-MarkdownCell $Context.RunId) + '`')
    foreach ($path in @($Summary.DiagnosticArtifactPaths)) {
        [void]$builder.AppendLine('- `' + (ConvertTo-MarkdownCell (Get-TestArtifactRelativePath -Context $Context -Path $path)) + '`')
    }
    if (@($Summary.InfrastructureErrors).Count -gt 0) {
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('### Infrastructure errors')
        foreach ($errorText in @($Summary.InfrastructureErrors)) {
            [void]$builder.AppendLine("- $(ConvertTo-MarkdownCell (ConvertTo-TestReportText -Context $Context -Value $errorText))")
        }
    }
    if (@($Context.ReportErrors).Count -gt 0) {
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('### Report errors')
        foreach ($errorText in @($Context.ReportErrors)) {
            [void]$builder.AppendLine("- $(ConvertTo-MarkdownCell (ConvertTo-TestReportText -Context $Context -Value $errorText))")
        }
    }

    return $builder.ToString()
}

function New-TestTimingRows {
    param(
        [object]$Context
    )

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($timing in @($Context.PreparationTimings)) {
        [void]$rows.Add([pscustomobject][ordered]@{
            recordType = 'Preparation'
            iteration = ''
            suite = ''
            framework = ''
            target = @($timing.TargetKeys) -join ', '
            test = $timing.Operation
            outcome = 'Completed'
            durationMs = [double]$timing.DurationMs
        })
    }
    foreach ($execution in @($Context.ExecutionResults)) {
        $target = $execution.Target
        [void]$rows.Add([pscustomobject][ordered]@{
            recordType = 'Target'
            iteration = [int]$execution.Iteration
            suite = $target.Suite
            framework = $target.Framework
            target = $target.Key
            test = ''
            outcome = $execution.Outcome
            durationMs = [double]$execution.DurationMs
        })
        foreach ($test in @($execution.TestResults)) {
            [void]$rows.Add([pscustomobject][ordered]@{
                recordType = 'Test'
                iteration = [int]$execution.Iteration
                suite = $target.Suite
                framework = $target.Framework
                target = $target.Key
                test = $test.TestName
                outcome = $test.Outcome
                durationMs = [double]$test.DurationMs
            })
        }
    }

    return @($rows.ToArray())
}

function Write-TestRunReports {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    if (-not $Context.ArtifactsEnabled) {
        return
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $reportDocument = New-TestReportDocument -Context $Context -Summary $Summary

    try {
        Write-AtomicJsonFile -Path (Join-Path $Context.RunDirectory 'summary.json') -Value $reportDocument
    } catch {
        [void]$errors.Add("summary.json: $($_.Exception.Message)")
    }

    try {
        Write-AtomicTextFile -Path (Join-Path $Context.RunDirectory 'summary.md') -Content (New-TestMarkdownReport -Context $Context -Summary $Summary)
    } catch {
        [void]$errors.Add("summary.md: $($_.Exception.Message)")
    }

    try {
        $csv = @(New-TestTimingRows -Context $Context | ConvertTo-Csv -NoTypeInformation) -join "`n"
        Write-AtomicTextFile -Path (Join-Path $Context.RunDirectory 'timings.csv') -Content ($csv + "`n")
    } catch {
        [void]$errors.Add("timings.csv: $($_.Exception.Message)")
    }

    foreach ($errorText in $errors) {
        [void]$Context.ReportErrors.Add($errorText)
    }
    if ($errors.Count -gt 0) {
        throw "One or more test reports could not be written: $($errors -join '; ')"
    }

    Write-Success "Report: $(Join-Path $Context.RunDirectory 'summary.md')"
}
