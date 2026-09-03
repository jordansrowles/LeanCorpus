$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-TestProjectPath {
    param(
        [object]$Target,
        [string]$RepoRoot
    )

    if ([System.IO.Path]::IsPathRooted([string]$Target.Project)) {
        return [string]$Target.Project
    }

    return Join-Path $RepoRoot $Target.Project
}

function Add-MtpOutputArguments {
    param(
        [System.Collections.Generic.List[string]]$Arguments,
        [string]$Verbosity
    )

    switch ($Verbosity.ToLowerInvariant()) {
        'quiet' {
            [void]$Arguments.Add('--output')
            [void]$Arguments.Add('Normal')
            [void]$Arguments.Add('--progress')
            [void]$Arguments.Add('off')
        }
        'detailed' {
            [void]$Arguments.Add('--output')
            [void]$Arguments.Add('Detailed')
        }
        default {
            if ($Verbosity) {
                [void]$Arguments.Add('--output')
                [void]$Arguments.Add('Normal')
            }
        }
    }
}

function Get-MtpTestArguments {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Target,
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$ArtifactDirectory
    )

    $arguments = [System.Collections.Generic.List[string]]::new()

    if ($Target.Filter) {
        [void]$arguments.Add('--filter')
        [void]$arguments.Add($Target.Filter)
    }

    Add-MtpOutputArguments -Arguments $arguments -Verbosity $Context.Options.Verbosity

    if ($Context.ArtifactsEnabled) {
        [void]$arguments.Add('--results-directory')
        [void]$arguments.Add($ArtifactDirectory)
        [void]$arguments.Add('--report-xunit-trx')
        [void]$arguments.Add('--report-xunit-trx-filename')
        [void]$arguments.Add('results.trx')
    }

    if ([bool]$Context.Options.CollectCoverage -and [bool]$Target.CoverageEligible) {
        [void]$arguments.Add('--coverlet')
        [void]$arguments.Add('--coverlet-output-format')
        [void]$arguments.Add('cobertura')
        [void]$arguments.Add('--coverlet-file-prefix')
        [void]$arguments.Add("$($Target.Suite)-$($Target.Framework)")
        [void]$arguments.Add('--coverlet-exclude-by-file')
        [void]$arguments.Add('**/obj/**/*.cs')
    }

    $capabilities = @($Target.Capabilities)
    if ($capabilities -contains 'HangDump' -and $Context.Options.HangTimeout -ne 'off') {
        [void]$arguments.Add('--hangdump')
        [void]$arguments.Add('--hangdump-timeout')
        [void]$arguments.Add($Context.Options.HangTimeout)
        [void]$arguments.Add('--hangdump-type-if-supported')
        [void]$arguments.Add('Mini')
        if ($ArtifactDirectory) {
            [void]$arguments.Add('--hangdump-filename')
            [void]$arguments.Add('hangdump.dmp')
        }
    }

    $captureCrashDump = $capabilities -contains 'CrashDump' -and
        ([bool]$Context.Options.Flaky -or [bool]$Context.Options.Diagnostics -or [bool]$Context.Options.Ci)
    if ($captureCrashDump) {
        [void]$arguments.Add('--crashdump')
        [void]$arguments.Add('--crashdump-type')
        [void]$arguments.Add('Mini')
        if ($ArtifactDirectory) {
            [void]$arguments.Add('--crashdump-filename')
            [void]$arguments.Add('crashdump.dmp')
        }
    }

    if ([bool]$Context.Options.Diagnostics -and $ArtifactDirectory) {
        $diagnosticsDirectory = Join-Path $ArtifactDirectory 'diagnostics'
        [void][System.IO.Directory]::CreateDirectory($diagnosticsDirectory)
        [void]$arguments.Add('--diagnostic')
        [void]$arguments.Add('--diagnostic-output-directory')
        [void]$arguments.Add($diagnosticsDirectory)
        [void]$arguments.Add('--diagnostic-file-prefix')
        [void]$arguments.Add('mtp-')
        [void]$arguments.Add('--diagnostic-verbosity')
        [void]$arguments.Add('Information')
    }

    foreach ($argument in @($Target.AdditionalArguments)) {
        if ($null -ne $argument -and -not [string]::IsNullOrEmpty([string]$argument)) {
            [void]$arguments.Add([string]$argument)
        }
    }

    return @($arguments.ToArray())
}

function New-EmptyMtpResultData {
    param([string]$Path = '')

    return [pscustomobject]@{
        Exists = $false
        Valid = $false
        Status = 'NotRequested'
        Path = $Path
        Tests = @()
        Error = ''
    }
}

function New-TestExecutionResult {
    param(
        [object]$Target,
        [int]$Iteration,
        [object]$ProcessResult,
        [object]$ResultData,
        [string]$ArtifactDirectory,
        [string]$StdOutPath,
        [string]$StdErrPath,
        [string]$TrxPath,
        [string[]]$DiagnosticPaths = @(),
        [TimeSpan]$ResultParsingDuration = ([TimeSpan]::Zero),
        [string]$Error = ''
    )

    $outcome = Get-TestExecutionOutcome -Target $Target -ProcessResult $ProcessResult `
        -ResultData $ResultData -DiagnosticPaths $DiagnosticPaths
    return [pscustomobject]@{
        Target = $Target
        Iteration = $Iteration
        ProcessId = $ProcessResult.ProcessId
        ExitCode = $ProcessResult.ExitCode
        StartTimeUtc = $ProcessResult.StartTimeUtc
        EndTimeUtc = $ProcessResult.EndTimeUtc
        DurationMs = [Math]::Round($ProcessResult.Elapsed.TotalMilliseconds, 3)
        Outcome = $outcome
        TimedOut = [bool]$ProcessResult.TimedOut
        WasKilled = [bool]$ProcessResult.WasKilled
        CancellationRequested = [bool]$ProcessResult.CancellationRequested
        Completed = $true
        ArtifactDirectory = $ArtifactDirectory
        StdOutPath = $StdOutPath
        StdErrPath = $StdErrPath
        TrxPath = $TrxPath
        DiagnosticPaths = @($DiagnosticPaths)
        ResultParsingDuration = $ResultParsingDuration
        ResultData = $ResultData
        TestResults = @($ResultData.Tests)
        Error = if ($Error) { $Error } else { [string]$ResultData.Error }
    }
}

function Invoke-TestTarget {
    param(
        [Parameter(Mandatory = $true)]
        [object]$PreparedTarget,
        [Parameter(Mandatory = $true)]
        [int]$Iteration,
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [int]$ExecutionNumber,
        [Parameter(Mandatory = $true)]
        [int]$ExecutionCount
    )

    $target = $PreparedTarget.Target
    $checkpoint = Start-TestTargetCheckpoint -Context $Context -Iteration $Iteration -Target $target
    $artifactDirectory = $checkpoint.ArtifactDirectory
    $stdoutPath = $checkpoint.StdOutPath
    $stderrPath = $checkpoint.StdErrPath
    $trxPath = $checkpoint.TrxPath

    Write-Info "  [$ExecutionNumber/$ExecutionCount] $($target.Key)..."

    if ($target.RunnerKind -eq 'Mtp') {
        $fileName = $PreparedTarget.ExecutablePath
        $arguments = Get-MtpTestArguments -Target $target -Context $Context -ArtifactDirectory $artifactDirectory
    } elseif ($target.RunnerKind -eq 'AotNative') {
        $fileName = $PreparedTarget.ExecutablePath
        $arguments = @($target.AdditionalArguments | Where-Object {
            $null -ne $_ -and -not [string]::IsNullOrEmpty([string]$_)
        })
    } else {
        throw "Unsupported runner kind '$($target.RunnerKind)' for target '$($target.Key)'."
    }

    $progressCallback = {
        param($process, $elapsed)
        $elapsedText = ([TimeSpan]$elapsed).ToString('hh\:mm\:ss')
        Write-Host "  [$ExecutionNumber/$ExecutionCount] $($target.Key) still running ($elapsedText elapsed)..." -ForegroundColor DarkGray
    }.GetNewClosure()

    $processResult = Invoke-ProcessWithLifecycle -FileName $fileName -Arguments $arguments `
        -WorkingDirectory $Context.RepoRoot -StdOutPath $stdoutPath -StdErrPath $stderrPath `
        -CaptureOutput $Context.ArtifactsEnabled -MirrorOutput $Context.ArtifactsEnabled `
        -Timeout $Context.Options.ProcessTimeout -OnProgress $progressCallback

    $resultParsingDuration = [TimeSpan]::Zero
    if ($target.RunnerKind -eq 'Mtp' -and $Context.ArtifactsEnabled) {
        $resultParsingStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $resultData = Read-MtpResults -Path $trxPath -Iteration $Iteration -TargetKey $target.Key `
            -Suite $target.Suite -Framework $target.Framework
        $resultParsingStopwatch.Stop()
        $resultParsingDuration = $resultParsingStopwatch.Elapsed
    } else {
        $resultData = New-EmptyMtpResultData -Path $trxPath
    }
    $diagnosticPaths = Get-ExecutionDiagnosticPaths -ArtifactDirectory $artifactDirectory
    $execution = New-TestExecutionResult -Target $target -Iteration $Iteration `
        -ProcessResult $processResult -ResultData $resultData `
        -ArtifactDirectory $artifactDirectory -StdOutPath $stdoutPath `
        -StdErrPath $stderrPath -TrxPath $trxPath -DiagnosticPaths $diagnosticPaths `
        -ResultParsingDuration $resultParsingDuration

    $elapsed = ([TimeSpan]$processResult.Elapsed).ToString('hh\:mm\:ss')
    if ($execution.Outcome -eq 'Passed') {
        Write-Success "  [$ExecutionNumber/$ExecutionCount] $($target.Key) - passed ($elapsed)"
    } else {
        Write-Failure "  [$ExecutionNumber/$ExecutionCount] $($target.Key) - $($execution.Outcome) ($elapsed)"
    }

    return $execution
}

function New-InfrastructureExecutionResult {
    param(
        [object]$Target,
        [int]$Iteration,
        [string]$ErrorMessage
    )

    return [pscustomobject]@{
        Target = $Target
        Iteration = $Iteration
        ProcessId = $null
        ExitCode = $null
        StartTimeUtc = [DateTime]::UtcNow
        EndTimeUtc = [DateTime]::UtcNow
        DurationMs = 0.0
        Outcome = 'InfrastructureError'
        TimedOut = $false
        WasKilled = $false
        CancellationRequested = $false
        Completed = $false
        ArtifactDirectory = ''
        StdOutPath = ''
        StdErrPath = ''
        TrxPath = ''
        DiagnosticPaths = @()
        ResultParsingDuration = [TimeSpan]::Zero
        ResultData = New-EmptyMtpResultData
        TestResults = @()
        Error = $ErrorMessage
    }
}
