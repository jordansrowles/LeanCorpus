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

function Start-ProcessOutputRead {
    param(
        [Parameter(Mandatory = $true)]
        [object]$State
    )

    if ($State.Completed) {
        return
    }

    try {
        $State.Task = $State.Reader.ReadAsync($State.Buffer, 0, $State.Buffer.Length)
    } catch {
        $State.Completed = $true
        $State.Error = $_.Exception.Message
    }
}

function Receive-ProcessOutputChunk {
    param(
        [Parameter(Mandatory = $true)]
        [object]$State
    )

    if ($State.Completed -or $null -eq $State.Task -or -not $State.Task.IsCompleted) {
        return $false
    }

    try {
        $count = $State.Task.GetAwaiter().GetResult()
        if ($count -le 0) {
            $State.Completed = $true
            return $true
        }

        $text = [string]::new($State.Buffer, 0, $count)
        if ($null -ne $State.Writer) {
            $State.Writer.Write($text)
            $State.Writer.Flush()
        }
        if ($State.Mirror) {
            if ($State.ForegroundColor) {
                Write-Host $text -NoNewline -ForegroundColor $State.ForegroundColor
            } else {
                Write-Host $text -NoNewline
            }
        }
        Start-ProcessOutputRead -State $State
        return $true
    } catch {
        $State.Completed = $true
        $State.Error = $_.Exception.Message
        if ($null -ne $State.Writer) {
            try {
                $State.Writer.WriteLine("Output capture failed: $($State.Error)")
                $State.Writer.Flush()
            } catch {
                # Preserve the process result even if the capture stream is unavailable.
            }
        }
        return $true
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
    $stdoutWriter = $null
    $stderrWriter = $null
    $stdoutState = $null
    $stderrState = $null

    if ($CaptureOutput) {
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        try {
            if ($StdOutPath) {
                $stdoutWriter = [System.IO.StreamWriter]::new($StdOutPath, $false, $utf8)
                $stdoutWriter.AutoFlush = $true
            }
            if ($StdErrPath) {
                $stderrWriter = [System.IO.StreamWriter]::new($StdErrPath, $false, $utf8)
                $stderrWriter.AutoFlush = $true
            }
        } catch {
            if ($null -ne $stdoutWriter) {
                $stdoutWriter.Dispose()
                $stdoutWriter = $null
            }
            if ($null -ne $stderrWriter) {
                $stderrWriter.Dispose()
                $stderrWriter = $null
            }
            throw
        }
    }

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
            $stdoutState = [pscustomobject]@{
                Reader = $process.StandardOutput
                Buffer = [char[]]::new(8192)
                Task = $null
                Writer = $stdoutWriter
                Mirror = $MirrorOutput
                ForegroundColor = $null
                Completed = $false
                Error = ''
            }
            $stderrState = [pscustomobject]@{
                Reader = $process.StandardError
                Buffer = [char[]]::new(8192)
                Task = $null
                Writer = $stderrWriter
                Mirror = $MirrorOutput
                ForegroundColor = 'DarkYellow'
                Completed = $false
                Error = ''
            }
            Start-ProcessOutputRead -State $stdoutState
            Start-ProcessOutputRead -State $stderrState
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
            $outputReceived = $false
            if ($CaptureOutput) {
                $outputReceived = (Receive-ProcessOutputChunk -State $stdoutState) -or $outputReceived
                $outputReceived = (Receive-ProcessOutputChunk -State $stderrState) -or $outputReceived
            }
            if (-not $outputReceived) {
                [void]$process.WaitForExit(50)
            }
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

        if ($processStarted) {
            try {
                # Wait until the child has closed both redirected streams.
                $process.WaitForExit()
            } catch {
                # The process may have exited between termination and stream draining.
            }
        }

        if ($CaptureOutput -and $null -ne $stdoutState -and $null -ne $stderrState) {
            $drainDeadline = [DateTime]::UtcNow.AddSeconds(5)
            while ((-not $stdoutState.Completed -or -not $stderrState.Completed) -and
                [DateTime]::UtcNow -lt $drainDeadline) {
                $outputReceived = $false
                if (-not $stdoutState.Completed) {
                    $outputReceived = (Receive-ProcessOutputChunk -State $stdoutState) -or $outputReceived
                }
                if (-not $stderrState.Completed) {
                    $outputReceived = (Receive-ProcessOutputChunk -State $stderrState) -or $outputReceived
                }
                if (-not $outputReceived) {
                    Start-Sleep -Milliseconds 10
                }
            }
            $stdoutState.Completed = $true
            $stderrState.Completed = $true
        }

        if ($CaptureOutput) {
            foreach ($writer in @($stdoutWriter, $stderrWriter)) {
                if ($null -ne $writer) {
                    try {
                        $writer.Flush()
                    } catch {
                        # Preserve the process result even if a final flush cannot complete.
                    }
                    try {
                        $writer.Dispose()
                    } catch {
                        # Preserve the process result even if stream disposal cannot complete.
                    }
                }
            }
            $stdoutWriter = $null
            $stderrWriter = $null
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
