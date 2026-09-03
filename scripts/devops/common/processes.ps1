$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DotNet {
    param(
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $argsLog = ($Arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '

    if ($WorkingDirectory) {
        Write-Host "  dotnet $argsLog" -ForegroundColor DarkGray
        Push-Location $WorkingDirectory
        try {
            dotnet @Arguments
        } finally {
            Pop-Location
        }
    } else {
        Write-Host "  dotnet $argsLog" -ForegroundColor DarkGray
        dotnet @Arguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exit code ${LASTEXITCODE}: $argsLog"
    }
}

function Invoke-ExternalCommand {
    param(
        [string]$Executable,
        [string[]]$Arguments
    )

    $argsLog = ($Arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    Write-Host "  $Executable $argsLog" -ForegroundColor DarkGray

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable exit code ${LASTEXITCODE}: $argsLog"
    }
}

function ConvertTo-ProcessArgumentLog {
    param([string[]]$Arguments = @())

    return (($Arguments | ForEach-Object {
        $value = [string]$_
        if ($value -match '[\s"]') {
            '"' + $value.Replace('"', '\"') + '"'
        } else {
            $value
        }
    }) -join ' ')
}

function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        $Process.Kill($true)
    } catch [System.Management.Automation.MethodException] {
        $Process.Kill()
    } catch [System.NotSupportedException] {
        $Process.Kill()
    }

    try {
        [void]$Process.WaitForExit(5000)
    } catch {
        # The process may have exited between Kill and WaitForExit.
    }
}

function Invoke-ProcessWithLifecycle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = (Get-Location).Path,
        [string]$StdOutPath = '',
        [string]$StdErrPath = '',
        [bool]$CaptureOutput = $false,
        [bool]$MirrorOutput = $false,
        [TimeSpan]$Timeout = ([TimeSpan]::Zero),
        [int]$ProgressIntervalSeconds = 30,
        [bool]$KillProcessTreeOnTimeout = $true,
        [scriptblock]$OnStarted,
        [scriptblock]$OnProgress,
        [scriptblock]$OnExited
    )

    $argsLog = ConvertTo-ProcessArgumentLog $Arguments
    Write-Info "  $FileName $argsLog"

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $CaptureOutput
    $startInfo.RedirectStandardError = $CaptureOutput
    foreach ($argument in @($Arguments)) {
        [void]$startInfo.ArgumentList.Add([string]$argument)
    }

    if ($CaptureOutput) {
        foreach ($path in @($StdOutPath, $StdErrPath)) {
            if ($path) {
                $parent = Split-Path -Parent $path
                if ($parent) {
                    [void][System.IO.Directory]::CreateDirectory($parent)
                }
            }
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startTimeUtc = [DateTime]::UtcNow
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $processStarted = $false
    $timedOut = $false
    $wasKilled = $false
    $cancellationRequested = $false
    $exitCode = $null
    $stdoutTask = $null
    $stderrTask = $null
    $stdoutText = ''
    $stderrText = ''

    try {
        try {
            $processStarted = $process.Start()
        } catch {
            throw "Unable to start '$FileName': $($_.Exception.Message)"
        }
        if (-not $processStarted) {
            throw "Unable to start '$FileName'."
        }

        if ($CaptureOutput) {
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
        }

        if ($null -ne $OnStarted) {
            & $OnStarted $process | Out-Null
        }

        $progressInterval = if ($ProgressIntervalSeconds -gt 0) {
            [TimeSpan]::FromSeconds($ProgressIntervalSeconds)
        } else {
            [TimeSpan]::MaxValue
        }
        $nextProgress = $stopwatch.Elapsed.Add($progressInterval)

        while (-not $process.HasExited) {
            [void]$process.WaitForExit(250)
            if (-not $process.HasExited -and $Timeout -gt [TimeSpan]::Zero -and $stopwatch.Elapsed -ge $Timeout) {
                $timedOut = $true
                $wasKilled = $true
                if ($KillProcessTreeOnTimeout) {
                    Stop-ProcessTree -Process $process
                } else {
                    $process.Kill()
                    [void]$process.WaitForExit(5000)
                }
            }

            if (-not $process.HasExited -and $stopwatch.Elapsed -ge $nextProgress) {
                if ($null -ne $OnProgress) {
                    & $OnProgress $process $stopwatch.Elapsed | Out-Null
                }
                $nextProgress = $nextProgress.Add($progressInterval)
            }
        }

        $process.WaitForExit()
        $exitCode = $process.ExitCode
    } catch [System.Management.Automation.PipelineStoppedException] {
        $cancellationRequested = $true
        if ($processStarted -and -not $process.HasExited) {
            $wasKilled = $true
            Stop-ProcessTree -Process $process
        }
    } catch [System.OperationCanceledException] {
        $cancellationRequested = $true
        if ($processStarted -and -not $process.HasExited) {
            $wasKilled = $true
            Stop-ProcessTree -Process $process
        }
    } finally {
        if ($processStarted -and -not $process.HasExited) {
            $wasKilled = $true
            Stop-ProcessTree -Process $process
        }

        if ($CaptureOutput) {
            try {
                if ($null -ne $stdoutTask) {
                    $stdoutText = $stdoutTask.GetAwaiter().GetResult()
                }
                if ($null -ne $stderrTask) {
                    $stderrText = $stderrTask.GetAwaiter().GetResult()
                }
            } catch {
                $stderrText = ($stderrText + "`nOutput capture failed: " + $_.Exception.Message).Trim()
            }

            if ($StdOutPath) {
                [System.IO.File]::WriteAllText($StdOutPath, $stdoutText, [System.Text.UTF8Encoding]::new($false))
            }
            if ($StdErrPath) {
                [System.IO.File]::WriteAllText($StdErrPath, $stderrText, [System.Text.UTF8Encoding]::new($false))
            }

            if ($MirrorOutput) {
                if ($stdoutText) {
                    Write-Host $stdoutText.TrimEnd()
                }
                if ($stderrText) {
                    Write-Host $stderrText.TrimEnd() -ForegroundColor DarkYellow
                }
            }
        }

        $stopwatch.Stop()
        if ($null -ne $process -and $processStarted -and $process.HasExited -and $null -eq $exitCode) {
            $exitCode = $process.ExitCode
        }
    }

    $endTimeUtc = [DateTime]::UtcNow
    $result = [pscustomobject]@{
        FileName = $FileName
        Arguments = @($Arguments)
        ProcessId = if ($processStarted) { $process.Id } else { $null }
        ExitCode = $exitCode
        StartTimeUtc = $startTimeUtc
        EndTimeUtc = $endTimeUtc
        Elapsed = $stopwatch.Elapsed
        StdOutPath = $StdOutPath
        StdErrPath = $StdErrPath
        TimedOut = $timedOut
        WasKilled = $wasKilled
        CancellationRequested = $cancellationRequested
    }

    if ($null -ne $OnExited) {
        & $OnExited $result | Out-Null
    }

    $process.Dispose()
    return $result
}
