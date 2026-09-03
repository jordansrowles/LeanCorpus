$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-TrxText {
    param([System.Xml.XmlNode]$Node)

    if ($null -eq $Node) {
        return ''
    }

    return ([string]$Node.InnerText).Trim()
}

function Get-TrxDuration {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [TimeSpan]::Zero
    }

    $duration = [TimeSpan]::Zero
    if ([TimeSpan]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [ref]$duration)) {
        return $duration
    }

    return [TimeSpan]::Zero
}

function ConvertTo-NeutralTestOutcome {
    param([string]$Outcome)

    $normalisedOutcome = if ($null -eq $Outcome) { '' } else { $Outcome.ToLowerInvariant() }
    switch ($normalisedOutcome) {
        'passed'     { return 'Passed' }
        'failed'     { return 'Failed' }
        'error'      { return 'Error' }
        'timeout'    { return 'Timeout' }
        'skipped'    { return 'Skipped' }
        'notexecuted' { return 'NotExecuted' }
        'inconclusive' { return 'Inconclusive' }
        default      { return if ($Outcome) { $Outcome } else { 'Unknown' } }
    }
}

function New-TestIdentity {
    param(
        [string]$TestId,
        [string]$ClassName,
        [string]$MethodName,
        [string]$DisplayName
    )

    if ($TestId) {
        return [pscustomobject]@{
            Value = "id:$TestId"
            Strength = 'StableReportId'
        }
    }

    if ($ClassName -or $MethodName -or $DisplayName) {
        return [pscustomobject]@{
            Value = "case:$ClassName|$MethodName|$DisplayName"
            Strength = if ($ClassName -and $MethodName -and $DisplayName) { 'ClassMethodDisplay' } else { 'WeakFallback' }
        }
    }

    return [pscustomobject]@{
        Value = "display:$DisplayName"
        Strength = 'WeakFallback'
    }
}

function Read-MtpResults {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [int]$Iteration = 0,
        [string]$TargetKey = '',
        [string]$Suite = '',
        [string]$Framework = ''
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            Exists = $false
            Valid = $false
            Status = 'Missing'
            Path = $Path
            Tests = @()
            Error = "TRX result file was not found: $Path"
        }
    }

    try {
        $xml = [System.Xml.XmlDocument]::new()
        $xml.PreserveWhitespace = $true
        $xml.Load($Path)

        $definitions = @{}
        foreach ($definition in @($xml.SelectNodes("//*[local-name()='UnitTest']"))) {
            $definitionId = $definition.GetAttribute('id')
            if ($definitionId) {
                $definitions[$definitionId] = $definition
            }
        }

        $tests = [System.Collections.Generic.List[object]]::new()
        foreach ($resultNode in @($xml.SelectNodes("//*[local-name()='UnitTestResult']"))) {
            $testId = $resultNode.GetAttribute('testId')
            $displayName = $resultNode.GetAttribute('testName')
            $className = ''
            $methodName = ''
            if ($definitions.ContainsKey($testId)) {
                $methodNode = $definitions[$testId].SelectSingleNode(".//*[local-name()='TestMethod']")
                if ($null -ne $methodNode) {
                    $className = $methodNode.GetAttribute('className')
                    $methodName = $methodNode.GetAttribute('name')
                }
            }

            $errorNode = $resultNode.SelectSingleNode(".//*[local-name()='ErrorInfo']")
            $messageNode = if ($null -ne $errorNode) {
                $errorNode.SelectSingleNode("./*[local-name()='Message']")
            } else {
                $null
            }
            $stackNode = if ($null -ne $errorNode) {
                $errorNode.SelectSingleNode("./*[local-name()='StackTrace']")
            } else {
                $null
            }
            $identity = New-TestIdentity -TestId $testId -ClassName $className `
                -MethodName $methodName -DisplayName $displayName
            $duration = Get-TrxDuration -Value ($resultNode.GetAttribute('duration'))
            [void]$tests.Add([pscustomobject]@{
                Iteration = $Iteration
                TargetKey = $TargetKey
                Suite = $Suite
                Framework = $Framework
                TestId = $testId
                TestName = $displayName
                ClassName = $className
                MethodName = $methodName
                DisplayName = $displayName
                Identity = $identity.Value
                IdentityStrength = $identity.Strength
                Outcome = ConvertTo-NeutralTestOutcome ($resultNode.GetAttribute('outcome'))
                Duration = $duration
                DurationMs = [Math]::Round($duration.TotalMilliseconds, 3)
                ErrorMessage = Get-TrxText $messageNode
                StackTrace = Get-TrxText $stackNode
            })
        }

        return [pscustomobject]@{
            Exists = $true
            Valid = $true
            Status = 'Parsed'
            Path = $Path
            Tests = @($tests.ToArray())
            Error = ''
        }
    } catch {
        return [pscustomobject]@{
            Exists = $true
            Valid = $false
            Status = 'Malformed'
            Path = $Path
            Tests = @()
            Error = "TRX parsing failed for '$Path': $($_.Exception.Message)"
        }
    }
}

function Test-CrashExitCode {
    param([object]$ExitCode)

    if ($null -eq $ExitCode -or [string]::IsNullOrWhiteSpace([string]$ExitCode)) {
        return $false
    }

    $code = [int]$ExitCode
    return $code -in @(
        6,
        11,
        134,
        137,
        139,
        3221225477,
        3221225786,
        3221226505
    )
}

function Get-TestExecutionOutcome {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Target,
        [Parameter(Mandatory = $true)]
        [object]$ProcessResult,
        [Parameter(Mandatory = $true)]
        [object]$ResultData,
        [string[]]$DiagnosticPaths = @()
    )

    if ($ProcessResult.CancellationRequested) {
        return 'Cancelled'
    }
    if ($ProcessResult.TimedOut) {
        return 'Hung'
    }

    $exitCode = $ProcessResult.ExitCode
    if ($Target.RunnerKind -eq 'Mtp') {
        if ($ResultData.Status -eq 'NotRequested') {
            if ($null -eq $exitCode) {
                return 'Incomplete'
            }
            if ([int]$exitCode -eq 0) {
                return 'Passed'
            }
            if (Test-CrashExitCode $exitCode) {
                return 'Crashed'
            }
            return 'Failed'
        }

        if ($ResultData.Valid) {
            $failedTests = @($ResultData.Tests | Where-Object {
                $_.Outcome -in @('Failed', 'Error', 'Timeout')
            })
            if ($null -eq $exitCode -or [int]$exitCode -ne 0) {
                if ($failedTests.Count -gt 0) {
                    return 'Failed'
                }
                return 'Incomplete'
            }
            if ($ResultData.Tests.Count -eq 0) {
                return 'Incomplete'
            }
            return 'Passed'
        }

        foreach ($path in @($DiagnosticPaths)) {
            $name = [System.IO.Path]::GetFileName($path)
            if ($name -match '(?i)hang') {
                return 'Hung'
            }
            if ($name -match '(?i)crash') {
                return 'Crashed'
            }
        }
        if (Test-CrashExitCode $exitCode) {
            return 'Crashed'
        }
        if ($null -eq $exitCode -or [int]$exitCode -eq 0) {
            return 'Incomplete'
        }
        return 'Incomplete'
    }

    if ($null -eq $exitCode -or [int]$exitCode -eq 0) {
        return 'Passed'
    }
    if (Test-CrashExitCode $exitCode) {
        return 'Crashed'
    }
    return 'Failed'
}

function Get-ExecutionDiagnosticPaths {
    param([string]$ArtifactDirectory)

    if (-not $ArtifactDirectory -or -not (Test-Path -LiteralPath $ArtifactDirectory)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $ArtifactDirectory -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in @('.dmp', '.diag', '.nettrace', '.gcdump', '.log') } |
            ForEach-Object { $_.FullName }
    )
}

function Get-NearestRankPercentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return 0.0
    }

    $sorted = @($Values | Sort-Object)
    $rank = [int][Math]::Ceiling($sorted.Count * $Percentile)
    if ($rank -lt 1) { $rank = 1 }
    if ($rank -gt $sorted.Count) { $rank = $sorted.Count }
    return [double]$sorted[$rank - 1]
}

function New-TestRunSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [object[]]$Targets,
        [object[]]$ExecutionResults = @()
    )

    $executions = @($ExecutionResults)
    $requestedIterations = [int]$Context.Options.Count
    $targetCount = @($Targets).Count
    $requestedExecutions = $requestedIterations * $targetCount
    $scheduledExecutions = $executions.Count
    $completedExecutions = @($executions | Where-Object { $_.Completed }).Count

    $outcomeNames = @('Passed', 'Failed', 'Crashed', 'Hung', 'Cancelled', 'Incomplete', 'InfrastructureError')
    $outcomeCounts = [ordered]@{}
    foreach ($name in $outcomeNames) {
        $outcomeCounts[$name] = @($executions | Where-Object { $_.Outcome -eq $name }).Count
    }

    $nonPassing = @($executions | Where-Object { $_.Outcome -ne 'Passed' }).Count
    $failureRate = if ($requestedExecutions -gt 0) {
        [Math]::Round(($nonPassing / [double]$requestedExecutions), 6)
    } else {
        0.0
    }

    $durations = @($executions | Where-Object { $null -ne $_.DurationMs } |
        ForEach-Object { [double]$_.DurationMs })
    $completedIterationSet = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($iteration in 1..$requestedIterations) {
        $iterationResults = @($executions | Where-Object { [int]$_.Iteration -eq $iteration })
        if ($iterationResults.Count -eq $targetCount -and $iterationResults.Count -gt 0 -and
            @($iterationResults | Where-Object { -not $_.Completed }).Count -eq 0) {
            [void]$completedIterationSet.Add($iteration)
        }
    }

    $observationMap = @{}
    $targetExecutionCounts = @{}
    foreach ($execution in $executions) {
        $targetKey = [string]$execution.Target.Key
        if (-not $targetExecutionCounts.ContainsKey($targetKey)) {
            $targetExecutionCounts[$targetKey] = 0
        }
        $targetExecutionCounts[$targetKey]++

        foreach ($test in @($execution.TestResults)) {
            $observationKey = "$targetKey|$($test.Identity)"
            if (-not $observationMap.ContainsKey($observationKey)) {
                $observationMap[$observationKey] = [pscustomobject]@{
                    TargetKey = $targetKey
                    Suite = $execution.Target.Suite
                    Framework = $execution.Target.Framework
                    TestId = $test.TestId
                    TestName = $test.TestName
                    ClassName = $test.ClassName
                    MethodName = $test.MethodName
                    Identity = $test.Identity
                    IdentityStrength = $test.IdentityStrength
                    Observations = [System.Collections.Generic.List[object]]::new()
                }
            }
            [void]$observationMap[$observationKey].Observations.Add([pscustomobject]@{
                Iteration = [int]$execution.Iteration
                Outcome = $test.Outcome
                DurationMs = [double]$test.DurationMs
                ErrorMessage = $test.ErrorMessage
                StackTrace = $test.StackTrace
            })
        }
    }

    $perTest = [System.Collections.Generic.List[object]]::new()
    foreach ($observation in @($observationMap.Values | Sort-Object TargetKey, TestName, Identity)) {
        $observations = @($observation.Observations)
        $expected = if ($targetExecutionCounts.ContainsKey($observation.TargetKey)) {
            [int]$targetExecutionCounts[$observation.TargetKey]
        } else {
            0
        }
        $hasNonTerminalTestOutcome = @($observations | Where-Object {
            $_.Outcome -notin @('Passed', 'Failed', 'Error', 'Timeout')
        }).Count -gt 0
        $isIncomplete = $observations.Count -lt $expected -or $hasNonTerminalTestOutcome
        $failedObservations = @($observations | Where-Object { $_.Outcome -in @('Failed', 'Error', 'Timeout') })
        $passedObservations = @($observations | Where-Object { $_.Outcome -eq 'Passed' })
        $classification = if ($isIncomplete) {
            'Incomplete'
        } elseif ($failedObservations.Count -gt 0 -and $passedObservations.Count -gt 0) {
            'Intermittent failure'
        } elseif ($failedObservations.Count -eq $observations.Count) {
            'Always fails'
        } elseif ($passedObservations.Count -eq $observations.Count) {
            'Always passes'
        } else {
            'Incomplete'
        }
        [void]$perTest.Add([pscustomobject]@{
            TargetKey = $observation.TargetKey
            Suite = $observation.Suite
            Framework = $observation.Framework
            TestId = $observation.TestId
            TestName = $observation.TestName
            ClassName = $observation.ClassName
            MethodName = $observation.MethodName
            Identity = $observation.Identity
            IdentityStrength = $observation.IdentityStrength
            ObservationCount = $observations.Count
            ExpectedObservationCount = $expected
            PassedCount = $passedObservations.Count
            FailedCount = $failedObservations.Count
            FailedIterations = @($failedObservations | ForEach-Object { [int]$_.Iteration } | Sort-Object)
            Classification = $classification
            MaxDurationMs = if ($observations.Count -gt 0) {
                [Math]::Round((@($observations | ForEach-Object { [double]$_.DurationMs } | Measure-Object -Maximum).Maximum), 3)
            } else {
                0.0
            }
        })
    }

    $targetDocuments = @($executions | ForEach-Object {
        ConvertTo-ExecutionDocument -Context $Context -Execution $_
    })
    $testDocuments = @($perTest.ToArray())
    $failingIterations = @($executions | Where-Object { $_.Outcome -ne 'Passed' } |
        ForEach-Object { [int]$_.Iteration } | Sort-Object -Unique)
    $diagnosticPaths = @($executions | ForEach-Object { @($_.DiagnosticPaths) } | Where-Object { $_ })
    $totalDuration = if ($Context.EndTimeUtc) {
        ($Context.EndTimeUtc - $Context.StartTimeUtc).TotalMilliseconds
    } else {
        ([DateTime]::UtcNow - $Context.StartTimeUtc).TotalMilliseconds
    }

    return [pscustomobject]@{
        Succeeded = $requestedExecutions -gt 0 -and $scheduledExecutions -eq $requestedExecutions -and $nonPassing -eq 0
        RequestedIterations = $requestedIterations
        CompletedIterations = $completedIterationSet.Count
        RequestedTargetExecutions = $requestedExecutions
        ScheduledTargetExecutions = $scheduledExecutions
        CompletedTargetExecutions = $completedExecutions
        PassedTargetExecutions = $outcomeCounts.Passed
        FailedTargetExecutions = $outcomeCounts.Failed
        CrashedTargetExecutions = $outcomeCounts.Crashed
        HungTargetExecutions = $outcomeCounts.Hung
        CancelledTargetExecutions = $outcomeCounts.Cancelled
        IncompleteTargetExecutions = $outcomeCounts.Incomplete
        InfrastructureErrorExecutions = $outcomeCounts.InfrastructureError
        FailureRate = $failureRate
        FailingIterations = $failingIterations
        MinimumDurationMs = Get-NearestRankPercentile -Values $durations -Percentile 0.0
        MedianDurationMs = Get-NearestRankPercentile -Values $durations -Percentile 0.5
        P95DurationMs = Get-NearestRankPercentile -Values $durations -Percentile 0.95
        MaximumDurationMs = if ($durations.Count -gt 0) { [double](@($durations | Measure-Object -Maximum).Maximum) } else { 0.0 }
        TotalDurationMs = [Math]::Round($totalDuration, 3)
        OutcomeCounts = $outcomeCounts
        TargetResults = $targetDocuments
        Tests = $testDocuments
        IntermittentFailures = @($testDocuments | Where-Object { $_.Classification -eq 'Intermittent failure' })
        AlwaysFailingTests = @($testDocuments | Where-Object { $_.Classification -eq 'Always fails' })
        IncompleteTests = @($testDocuments | Where-Object { $_.Classification -eq 'Incomplete' })
        SlowestTests = @($testDocuments | Sort-Object MaxDurationMs -Descending | Select-Object -First 10)
        CrashedTargets = @($targetDocuments | Where-Object { $_.outcome -eq 'Crashed' })
        HungTargets = @($targetDocuments | Where-Object { $_.outcome -eq 'Hung' })
        CancelledTargets = @($targetDocuments | Where-Object { $_.outcome -eq 'Cancelled' })
        IncompleteTargets = @($targetDocuments | Where-Object { $_.outcome -eq 'Incomplete' })
        InfrastructureErrors = @($Context.InfrastructureErrors)
        DiagnosticArtifactPaths = $diagnosticPaths
    }
}
