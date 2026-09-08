$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-TestRunId {
    param([string]$Framework = '')
    return New-ArtifactRunId -Kind test -Framework $Framework
}

function Get-TestEnvironmentSnapshot {
    param(
        [string]$RepoRoot,
        [string]$CommandLine
    )

    $relevantEnvironment = [ordered]@{}
    foreach ($name in @(
        'CI',
        'GITHUB_ACTIONS',
        'CHAOS_ITERATIONS',
        'DOTNET_ROOT',
        'DOTNET_CLI_TELEMETRY_OPTOUT',
        'DOTNET_NOLOGO',
        'COMPlus_ReadyToRun',
        'COMPlus_TieredPGO'
    )) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) {
            $relevantEnvironment[$name] = $value
        }
    }

    $dotnetInfo = ''
    try {
        $dotnetInfo = ((& dotnet --info 2>&1 | Out-String).Trim())
    } catch {
        $dotnetInfo = "dotnet --info failed: $($_.Exception.Message)"
    }

    $gitCommit = ''
    $gitBranch = ''
    $gitDirty = $false
    try {
        $gitCommit = ((git -C $RepoRoot rev-parse HEAD 2>$null) | Select-Object -First 1).Trim()
        $gitBranch = ((git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null) | Select-Object -First 1).Trim()
        $gitDirty = @((git -C $RepoRoot status --porcelain --untracked-files=all 2>$null)).Count -gt 0
    } catch {
        $gitDirty = $true
    }

    $processorName = [Environment]::GetEnvironmentVariable('PROCESSOR_IDENTIFIER')
    if (-not $processorName -and $IsLinux -and (Test-Path '/proc/cpuinfo')) {
        $processorMatch = Select-String -Path '/proc/cpuinfo' -Pattern '^model name\s*:\s*(.+)$' |
            Select-Object -First 1
        if ($null -ne $processorMatch) {
            $processorName = $processorMatch.Matches.Groups[1].Value
        }
    }

    $memory = [GC]::GetGCMemoryInfo()
    $osDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()

    return [ordered]@{
        capturedAtUtc = [DateTime]::UtcNow.ToString('O')
        os = $osDescription
        architecture = $architecture
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        processorName = $processorName
        logicalProcessorCount = [Environment]::ProcessorCount
        availableMemoryBytes = $memory.TotalAvailableMemoryBytes
        dotnetInfo = $dotnetInfo
        sdkVersion = ((& dotnet --version 2>$null | Select-Object -First 1) -as [string]).Trim()
        gitCommit = $gitCommit
        gitBranch = $gitBranch
        gitDirty = $gitDirty
        commandLine = $CommandLine
        environment = $relevantEnvironment
    }
}

function ConvertTo-TestTargetDocument {
    param([object]$Target)

    return [ordered]@{
        key = $Target.Key
        name = $Target.Name
        suite = $Target.Suite
        runnerKind = $Target.RunnerKind
        project = $Target.Project
        framework = $Target.Framework
        configuration = $Target.Configuration
        runtimeIdentifier = $Target.RuntimeIdentifier
        filter = $Target.Filter
        areas = @($Target.Areas)
        categories = @($Target.Categories)
        coverageEligible = [bool]$Target.CoverageEligible
        capabilities = @($Target.Capabilities)
        additionalArguments = @($Target.AdditionalArguments)
    }
}

function New-TestRunContext {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Options,
        [Parameter(Mandatory = $true)]
        [object[]]$Targets,
        [Parameter(Mandatory = $true)]
        [string]$CommandLine,
        [string]$RunId = '',
        [string]$RepoRoot = (Get-RepoRoot)
    )

    $artifactsEnabled = [bool]$Options.ArtifactsEnabled
    $runId = $RunId
    $runDirectory = ''
    if ($artifactsEnabled) {
        if ([string]::IsNullOrWhiteSpace($runId)) {
            $runId = Get-TestRunId -Framework $Options.RequestedFramework
        }
        $artifactRun = New-ArtifactRun -Kind test -Framework $Options.RequestedFramework `
            -Configuration $Options.Configuration -CommandLine $CommandLine -RepoRoot $RepoRoot -RunId $runId
        $runDirectory = $artifactRun.RunDirectory
    }

    $context = [pscustomobject]@{
        RunId = $runId
        RunDirectory = $runDirectory
        RepoRoot = $RepoRoot
        CommandLine = $CommandLine
        Options = $Options
        CoverageResultsDirectory = if ($Options.PSObject.Properties['CoverageResultsDirectory']) {
            [string]$Options.CoverageResultsDirectory
        } else {
            ''
        }
        Targets = @($Targets)
        ArtifactsEnabled = $artifactsEnabled
        StartTimeUtc = [DateTime]::UtcNow
        EndTimeUtc = $null
        StageTimings = [System.Collections.Generic.List[object]]::new()
        ResultParsingDuration = [TimeSpan]::Zero
        ExecutionResults = [System.Collections.Generic.List[object]]::new()
        PreparationTimings = [System.Collections.Generic.List[object]]::new()
        InfrastructureErrors = [System.Collections.Generic.List[string]]::new()
        ReportErrors = [System.Collections.Generic.List[string]]::new()
        CurrentExecution = $null
        EnvironmentPath = ''
        ManifestPath = ''
        StatePath = ''
    }

    if ($artifactsEnabled) {
        try {
            $context.EnvironmentPath = Join-Path $runDirectory 'environment.json'
            $context.ManifestPath = Join-Path $runDirectory 'run.json'
            $context.StatePath = Join-Path $runDirectory 'state.json'

            $environment = Get-TestEnvironmentSnapshot -RepoRoot $RepoRoot -CommandLine $CommandLine
            Write-AtomicJsonFile -Path $context.EnvironmentPath -Value $environment

            $initialValues = @{
                startTimeUtc = $context.StartTimeUtc.ToString('O')
                endTimeUtc = $null
                durationMs = 0
                gitCommit = $environment.gitCommit
                gitBranch = $environment.gitBranch
                gitDirty = $environment.gitDirty
                sdkVersion = $environment.sdkVersion
                requestedFramework = $Options.RequestedFramework
                runtimeIdentifier = $Options.RuntimeIdentifier
                count = [int]$Options.Count
                flaky = [bool]$Options.Flaky
                diagnostics = [bool]$Options.Diagnostics
                failFast = [bool]$Options.FailFast
                selectedTargets = @($Targets | ForEach-Object { ConvertTo-TestTargetDocument $_ })
                artifactPaths = [ordered]@{
                    environment = 'environment.json'
                    state = 'state.json'
                    reportMarkdown = 'report.md'
                    reportJson = 'report.json'
                    timingsCsv = 'timings.csv'
                }
            }
            Update-ArtifactRunManifest -RunDirectory $runDirectory -Values $initialValues
            Write-TestRunCheckpoint -Context $context -Status 'Running'
        } catch {
            try {
                Complete-ArtifactRun -RunDirectory $runDirectory -Status Failed `
                    -AdditionalValues @{ error = $_.Exception.Message }
            } catch {
                # Preserve the original setup failure if its evidence cannot be written.
            }
            throw
        }
    }

    return $context
}

function Add-TestStageTiming {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [TimeSpan]$Duration
    )

    [void]$Context.StageTimings.Add([ordered]@{
        name = $Name
        durationMs = [Math]::Round($Duration.TotalMilliseconds, 3)
    })
}

function Get-TestArtifactRelativePath {
    param(
        [object]$Context,
        [string]$Path
    )

    if (-not $Path -or -not $Context.RunDirectory) {
        return ''
    }

    return ([System.IO.Path]::GetRelativePath($Context.RunDirectory, $Path)).Replace('\', '/')
}

function ConvertTo-TestReportText {
    param(
        [object]$Context,
        [string]$Value
    )

    if (-not $Value) {
        return ''
    }
    if (-not $Context.RunDirectory) {
        return $Value
    }

    $text = $Value
    $runDirectory = ([System.IO.Path]::GetFullPath($Context.RunDirectory)).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    foreach ($prefix in @($runDirectory, $runDirectory.Replace('\', '/'), $runDirectory.Replace('/', '\'))) {
        if ($prefix) {
            $text = $text.Replace($prefix, '')
        }
    }
    return $text -replace '^[\\/]+', ''
}

function Get-TestTargetArtifactDirectory {
    param(
        [object]$Context,
        [int]$Iteration,
        [object]$Target
    )

    if (-not $Context.ArtifactsEnabled) {
        return ''
    }

    $targetDirectory = Join-Path (Join-Path $Context.RunDirectory 'targets') $Target.ArtifactName
    $targetDirectory = Join-Path $targetDirectory ("{0:D3}" -f $Iteration)
    [void][System.IO.Directory]::CreateDirectory($targetDirectory)
    [void][System.IO.Directory]::CreateDirectory((Join-Path $targetDirectory 'diagnostics'))
    return $targetDirectory
}

function Start-TestTargetCheckpoint {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [int]$Iteration,
        [Parameter(Mandatory = $true)]
        [object]$Target
    )

    if (-not $Context.ArtifactsEnabled) {
        return [pscustomobject]@{
            ArtifactDirectory = ''
            StdOutPath = ''
            StdErrPath = ''
            TrxPath = ''
        }
    }

    $artifactDirectory = Get-TestTargetArtifactDirectory -Context $Context -Iteration $Iteration -Target $Target
    $stdoutPath = Join-Path $artifactDirectory 'stdout.log'
    $stderrPath = Join-Path $artifactDirectory 'stderr.log'
    $trxPath = if ($Target.RunnerKind -eq 'Mtp') {
        Join-Path $artifactDirectory 'results.trx'
    } else {
        ''
    }
    $startTimeUtc = [DateTime]::UtcNow
    $Context.CurrentExecution = [pscustomobject]@{
        Iteration = $Iteration
        Target = $Target
        StartTimeUtc = $startTimeUtc
        ProcessId = $null
        ArtifactDirectory = $artifactDirectory
        StdOutPath = $stdoutPath
        StdErrPath = $stderrPath
        TrxPath = $trxPath
    }
    Write-TestRunCheckpoint -Context $Context -Status 'Running'

    return [pscustomobject]@{
        ArtifactDirectory = $artifactDirectory
        StdOutPath = $stdoutPath
        StdErrPath = $stderrPath
        TrxPath = $trxPath
    }
}

function Clear-TestTargetCheckpoint {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context
    )

    $Context.CurrentExecution = $null
}

function ConvertTo-ExecutionDocument {
    param(
        [object]$Context,
        [object]$Execution
    )

    $target = $Execution.Target
    $tests = @($Execution.TestResults)
    return [ordered]@{
        iteration = [int]$Execution.Iteration
        targetKey = $target.Key
        suite = $target.Suite
        framework = $target.Framework
        runnerKind = $target.RunnerKind
        target = $target.Key
        processId = $Execution.ProcessId
        exitCode = $Execution.ExitCode
        startTimeUtc = if ($Execution.StartTimeUtc) { $Execution.StartTimeUtc.ToString('O') } else { $null }
        endTimeUtc = if ($Execution.EndTimeUtc) { $Execution.EndTimeUtc.ToString('O') } else { $null }
        durationMs = [Math]::Round([double]$Execution.DurationMs, 3)
        outcome = $Execution.Outcome
        timedOut = [bool]$Execution.TimedOut
        wasKilled = [bool]$Execution.WasKilled
        cancellationRequested = [bool]$Execution.CancellationRequested
        artifactDirectory = Get-TestArtifactRelativePath -Context $Context -Path $Execution.ArtifactDirectory
        stdoutPath = Get-TestArtifactRelativePath -Context $Context -Path $Execution.StdOutPath
        stderrPath = Get-TestArtifactRelativePath -Context $Context -Path $Execution.StdErrPath
        trxPath = Get-TestArtifactRelativePath -Context $Context -Path $Execution.TrxPath
        diagnosticPaths = @($Execution.DiagnosticPaths | ForEach-Object {
            Get-TestArtifactRelativePath -Context $Context -Path $_
        } | Where-Object { $_ })
        testCount = $tests.Count
        failedTestCount = @($tests | Where-Object { $_.Outcome -in @('Failed', 'Error', 'Timeout') }).Count
        error = ConvertTo-TestReportText -Context $Context -Value $Execution.Error
    }
}

function ConvertTo-TestCheckpointDocument {
    param(
        [object]$Context,
        [string]$Status = 'Running'
    )

    $requested = [int]($Context.Targets.Count * $Context.Options.Count)
    $executions = @($Context.ExecutionResults)
    $currentExecution = if ($null -ne $Context.CurrentExecution) {
        $current = $Context.CurrentExecution
        [ordered]@{
            iteration = [int]$current.Iteration
            target = $current.Target.Key
            targetKey = $current.Target.Key
            status = 'Running'
            startTimeUtc = $current.StartTimeUtc.ToString('O')
            processId = $current.ProcessId
            artifactDirectory = Get-TestArtifactRelativePath -Context $Context -Path $current.ArtifactDirectory
            stdoutPath = Get-TestArtifactRelativePath -Context $Context -Path $current.StdOutPath
            stderrPath = Get-TestArtifactRelativePath -Context $Context -Path $current.StdErrPath
            trxPath = Get-TestArtifactRelativePath -Context $Context -Path $current.TrxPath
        }
    } else {
        $null
    }

    return [ordered]@{
        schemaVersion = 1
        runId = $Context.RunId
        status = $Status
        updatedAtUtc = [DateTime]::UtcNow.ToString('O')
        requestedTargetExecutions = $requested
        scheduledTargetExecutions = $executions.Count
        completedTargetExecutions = @($executions | Where-Object { $_.Completed }).Count
        currentExecution = $currentExecution
        targetResults = @($executions | ForEach-Object {
            ConvertTo-ExecutionDocument -Context $Context -Execution $_
        })
        infrastructureErrors = @($Context.InfrastructureErrors | ForEach-Object {
            ConvertTo-TestReportText -Context $Context -Value $_
        })
        reportErrors = @($Context.ReportErrors | ForEach-Object {
            ConvertTo-TestReportText -Context $Context -Value $_
        })
    }
}

function Write-TestRunCheckpoint {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [string]$Status = 'Running'
    )

    if (-not $Context.ArtifactsEnabled) {
        return
    }

    Write-AtomicJsonFile -Path $Context.StatePath -Value (ConvertTo-TestCheckpointDocument -Context $Context -Status $Status)
}

function Update-TestRunManifest {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [object]$Summary = $null,
        [ValidateSet('Passed', 'Failed', 'Incomplete', 'Cancelled')]
        [string]$Status = 'Failed'
    )

    if (-not $Context.ArtifactsEnabled) {
        return
    }

    if ($null -eq $Context.EndTimeUtc) {
        $Context.EndTimeUtc = [DateTime]::UtcNow
    }
    $durationMs = ($Context.EndTimeUtc - $Context.StartTimeUtc).TotalMilliseconds
    $environment = $null
    try {
        if (Test-Path $Context.EnvironmentPath) {
            $environment = Get-Content $Context.EnvironmentPath -Raw | ConvertFrom-Json
        }
    } catch {
        [void]$Context.ReportErrors.Add("Manifest environment read failed: $($_.Exception.Message)")
    }

    $values = @{
        startTimeUtc = $Context.StartTimeUtc.ToString('O')
        endTimeUtc = $Context.EndTimeUtc.ToString('O')
        durationMs = [Math]::Round($durationMs, 3)
        requestedFramework = $Context.Options.RequestedFramework
        runtimeIdentifier = $Context.Options.RuntimeIdentifier
        sdkVersion = if ($null -ne $environment) { $environment.sdkVersion } else { '' }
        count = [int]$Context.Options.Count
        flaky = [bool]$Context.Options.Flaky
        diagnostics = [bool]$Context.Options.Diagnostics
        failFast = [bool]$Context.Options.FailFast
        selectedTargets = @($Context.Targets | ForEach-Object { ConvertTo-TestTargetDocument $_ })
        artifactPaths = [ordered]@{
            environment = Get-TestArtifactRelativePath -Context $Context -Path $Context.EnvironmentPath
            state = Get-TestArtifactRelativePath -Context $Context -Path $Context.StatePath
            reportMarkdown = 'report.md'
            reportJson = 'report.json'
            timingsCsv = 'timings.csv'
        }
        stageTimings = @($Context.StageTimings)
        preparationTimings = @($Context.PreparationTimings | ForEach-Object {
            [ordered]@{
                stage = $_.Stage
                operation = $_.Operation
                workItem = $_.WorkItem
                targetKeys = @($_.TargetKeys)
                durationMs = [double]$_.DurationMs
            }
        })
    }

    if ($null -ne $Summary) {
        $values.summary = [ordered]@{
            succeeded = [bool]$Summary.Succeeded
            requestedIterations = [int]$Summary.RequestedIterations
            completedIterations = [int]$Summary.CompletedIterations
            requestedTargetExecutions = [int]$Summary.RequestedTargetExecutions
            scheduledTargetExecutions = [int]$Summary.ScheduledTargetExecutions
            completedTargetExecutions = [int]$Summary.CompletedTargetExecutions
            passedTargetExecutions = [int]$Summary.PassedTargetExecutions
            failedTargetExecutions = [int]$Summary.FailedTargetExecutions
            failureRate = [double]$Summary.FailureRate
        }
    }

    if ($null -ne $environment) {
        $values.gitCommit = $environment.gitCommit
        $values.gitBranch = $environment.gitBranch
        $values.gitDirty = [bool]$environment.gitDirty
    }

    Complete-ArtifactRun -RunDirectory $Context.RunDirectory -Status $Status -AdditionalValues $values
}

function Copy-TestCoverageResults {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [object]$Execution
    )

    if (-not $Context.CoverageResultsDirectory -or -not $Execution.ArtifactDirectory) {
        return @()
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $target = $Execution.Target
    if ($target.RunnerKind -ne 'Mtp') {
        return @()
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension([string]$target.Project)
    $destination = Join-Path $Context.CoverageResultsDirectory "$($target.Framework)/$projectName"
    try {
        [void][System.IO.Directory]::CreateDirectory($destination)
        $files = @(Get-ChildItem -LiteralPath $Execution.ArtifactDirectory -Recurse -File -Filter '*.coverage.cobertura.*.xml' -ErrorAction SilentlyContinue)
        foreach ($file in $files) {
            try {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name) -Force -ErrorAction Stop
            } catch {
                [void]$errors.Add("Coverage result '$($file.FullName)' could not be copied: $($_.Exception.Message)")
            }
        }
    } catch {
        [void]$errors.Add("Coverage output directory '$destination' could not be prepared: $($_.Exception.Message)")
    }

    return @($errors.ToArray())
}
