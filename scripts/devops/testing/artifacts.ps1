$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-TestJson {
    param([object]$Value)

    return ($Value | ConvertTo-Json -Depth 30)
}

function Write-AtomicTextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        [void][System.IO.Directory]::CreateDirectory($parent)
    }

    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText($temporaryPath, $Content, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    } catch {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
        throw
    }
}

function Write-AtomicJsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    Write-AtomicTextFile -Path $Path -Content (ConvertTo-TestJson $Value)
}

function Get-TestRunId {
    $utc = [DateTime]::UtcNow
    $processId = [Environment]::ProcessId
    $entropy = [Guid]::NewGuid().ToString('N').Substring(0, 6)
    return "$($utc.ToString('yyyyMMdd-HHmmss-fff'))-$processId-$entropy"
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
        [string]$RepoRoot = (Get-RepoRoot)
    )

    $artifactsEnabled = [bool]$Options.ArtifactsEnabled
    $runId = if ($artifactsEnabled) { Get-TestRunId } else { '' }
    $runDirectory = if ($artifactsEnabled) {
        Join-Path $RepoRoot "artifacts/test/runs/$runId"
    } else {
        ''
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
        [void][System.IO.Directory]::CreateDirectory($runDirectory)
        $context.EnvironmentPath = Join-Path $runDirectory 'environment.json'
        $context.ManifestPath = Join-Path $runDirectory 'manifest.json'
        $context.StatePath = Join-Path $runDirectory 'state.json'

        $environment = Get-TestEnvironmentSnapshot -RepoRoot $RepoRoot -CommandLine $CommandLine
        Write-AtomicJsonFile -Path $context.EnvironmentPath -Value $environment

        $manifest = [ordered]@{
            schemaVersion = 1
            runId = $runId
            commandLine = $CommandLine
            startTimeUtc = $context.StartTimeUtc.ToString('O')
            endTimeUtc = $null
            durationMs = 0
            gitCommit = $environment.gitCommit
            gitBranch = $environment.gitBranch
            gitDirty = $environment.gitDirty
            os = $environment.os
            architecture = $environment.architecture
            sdkVersion = $environment.sdkVersion
            configuration = $Options.Configuration
            requestedFramework = $Options.RequestedFramework
            runtimeIdentifier = $Options.RuntimeIdentifier
            count = [int]$Options.Count
            flaky = [bool]$Options.Flaky
            diagnostics = [bool]$Options.Diagnostics
            failFast = [bool]$Options.FailFast
            ci = [bool]$Options.Ci
            selectedTargets = @($Targets | ForEach-Object { ConvertTo-TestTargetDocument $_ })
            artifactPaths = [ordered]@{
                environment = 'environment.json'
                state = 'state.json'
                summaryMarkdown = 'summary.md'
                summaryJson = 'summary.json'
                timingsCsv = 'timings.csv'
            }
        }
        Write-AtomicJsonFile -Path $context.ManifestPath -Value $manifest
        Write-TestRunCheckpoint -Context $context -Status 'Running'
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

    $iterationDirectory = Join-Path $Context.RunDirectory ("iteration-{0:D3}" -f $Iteration)
    $targetDirectory = Join-Path $iterationDirectory $Target.ArtifactName
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
        [object]$Summary = $null
    )

    if (-not $Context.ArtifactsEnabled) {
        return
    }

    $Context.EndTimeUtc = [DateTime]::UtcNow
    $durationMs = ($Context.EndTimeUtc - $Context.StartTimeUtc).TotalMilliseconds
    $manifest = [ordered]@{
        schemaVersion = 1
        runId = $Context.RunId
        commandLine = $Context.CommandLine
        startTimeUtc = $Context.StartTimeUtc.ToString('O')
        endTimeUtc = $Context.EndTimeUtc.ToString('O')
        durationMs = [Math]::Round($durationMs, 3)
        gitCommit = ''
        gitBranch = ''
        gitDirty = $false
        os = ''
        architecture = ''
        sdkVersion = ''
        configuration = $Context.Options.Configuration
        requestedFramework = $Context.Options.RequestedFramework
        runtimeIdentifier = $Context.Options.RuntimeIdentifier
        count = [int]$Context.Options.Count
        flaky = [bool]$Context.Options.Flaky
        diagnostics = [bool]$Context.Options.Diagnostics
        failFast = [bool]$Context.Options.FailFast
        ci = [bool]$Context.Options.Ci
        selectedTargets = @($Context.Targets | ForEach-Object { ConvertTo-TestTargetDocument $_ })
        artifactPaths = [ordered]@{
            environment = Get-TestArtifactRelativePath -Context $Context -Path $Context.EnvironmentPath
            state = Get-TestArtifactRelativePath -Context $Context -Path $Context.StatePath
            summaryMarkdown = 'summary.md'
            summaryJson = 'summary.json'
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
        $manifest.summary = [ordered]@{
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

    try {
        if (Test-Path $Context.EnvironmentPath) {
            $environment = Get-Content -LiteralPath $Context.EnvironmentPath -Raw | ConvertFrom-Json
            $manifest.gitCommit = $environment.gitCommit
            $manifest.gitBranch = $environment.gitBranch
            $manifest.gitDirty = [bool]$environment.gitDirty
            $manifest.os = $environment.os
            $manifest.architecture = $environment.architecture
            $manifest.sdkVersion = $environment.sdkVersion
        }
    } catch {
        [void]$Context.ReportErrors.Add("Manifest environment update failed: $($_.Exception.Message)")
    }

    Write-AtomicJsonFile -Path $Context.ManifestPath -Value $manifest
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
