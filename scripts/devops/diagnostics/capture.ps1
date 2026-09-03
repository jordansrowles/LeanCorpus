$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DiagnosticDuration {
    param([object]$Parsed)

    $value = if ($Parsed.Has('Duration')) { [string]$Parsed.Get('Duration', '5s') } else { '5s' }
    $duration = ConvertTo-ProcessTimeout -Value $value
    if ($duration -le [TimeSpan]::Zero) {
        throw '--duration must be greater than zero for diagnostics capture.'
    }
    return $duration
}

function Invoke-DiagnosticsCounters {
    param(
        [object]$Parsed,
        [string]$RepoRoot,
        [string]$CommandLine
    )

    $process = Resolve-DiagnosticProcess -Parsed $Parsed
    $context = New-DiagnosticsContext -CommandLine $CommandLine -RepoRoot $RepoRoot `
        -ProcessId $process.Id -Tool 'dotnet-counters'
    $arguments = @('monitor', '--process-id', $process.Id.ToString(), '--refresh-interval', '1') + @($Parsed.PassThrough)
    try {
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-counters' -Arguments $arguments)
        Update-DiagnosticsMetadata -Context $context -Status 'Completed'
        Write-Success "Counters finished: $($context.RunDirectory)"
        return 0
    } catch {
        [void]$context.Warnings.Add($_.Exception.Message)
        Update-DiagnosticsMetadata -Context $context -Status 'Failed' -ErrorMessage $_.Exception.Message
        Write-Failure "Counters failed: $($_.Exception.Message)"
        return 1
    }
}

function Invoke-DiagnosticsTrace {
    param(
        [object]$Parsed,
        [string]$RepoRoot,
        [string]$CommandLine
    )

    $process = Resolve-DiagnosticProcess -Parsed $Parsed
    $context = New-DiagnosticsContext -CommandLine $CommandLine -RepoRoot $RepoRoot `
        -ProcessId $process.Id -Tool 'dotnet-trace'
    $outputName = 'trace.nettrace'
    $outputPath = Join-Path $context.RunDirectory $outputName
    $arguments = @('collect', '--process-id', $process.Id.ToString(), '--output', $outputPath) + @($Parsed.PassThrough)
    try {
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-trace' -Arguments $arguments -OutputName $outputName)
        Update-DiagnosticsMetadata -Context $context -Status 'Completed'
        Write-Success "Trace written to: $outputPath"
        return 0
    } catch {
        [void]$context.Warnings.Add($_.Exception.Message)
        Update-DiagnosticsMetadata -Context $context -Status 'Failed' -ErrorMessage $_.Exception.Message
        Write-Failure "Trace failed: $($_.Exception.Message)"
        return 1
    }
}

function Invoke-DiagnosticsGcDump {
    param(
        [object]$Parsed,
        [string]$RepoRoot,
        [string]$CommandLine
    )

    $process = Resolve-DiagnosticProcess -Parsed $Parsed
    $context = New-DiagnosticsContext -CommandLine $CommandLine -RepoRoot $RepoRoot `
        -ProcessId $process.Id -Tool 'dotnet-gcdump'
    $outputName = 'heap.gcdump'
    $outputPath = Join-Path $context.RunDirectory $outputName
    $arguments = @('collect', '--process-id', $process.Id.ToString(), '--output', $outputPath) + @($Parsed.PassThrough)
    try {
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-gcdump' -Arguments $arguments -OutputName $outputName)
        Update-DiagnosticsMetadata -Context $context -Status 'Completed'
        Write-Success "GC dump written to: $outputPath"
        return 0
    } catch {
        [void]$context.Warnings.Add($_.Exception.Message)
        Update-DiagnosticsMetadata -Context $context -Status 'Failed' -ErrorMessage $_.Exception.Message
        Write-Failure "GC dump failed: $($_.Exception.Message)"
        return 1
    }
}

function Invoke-DiagnosticsDump {
    param(
        [object]$Parsed,
        [string]$RepoRoot,
        [string]$CommandLine
    )

    $process = Resolve-DiagnosticProcess -Parsed $Parsed
    $dumpType = [string]$Parsed.Get('Type', 'Mini')
    if ($dumpType -notin @('Mini', 'Heap', 'Full', 'Triage')) {
        throw "Unsupported dump type '$dumpType'. Expected Mini, Heap, Full, or Triage."
    }

    Write-Warn 'Dumps may contain sensitive application memory.'
    $context = New-DiagnosticsContext -CommandLine $CommandLine -RepoRoot $RepoRoot `
        -ProcessId $process.Id -Tool 'dotnet-dump'
    $outputName = 'process.dmp'
    $outputPath = Join-Path $context.RunDirectory $outputName
    $arguments = @('collect', '--process-id', $process.Id.ToString(), '--output', $outputPath, '--type', $dumpType) + @($Parsed.PassThrough)
    try {
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-dump' -Arguments $arguments -OutputName $outputName)
        Update-DiagnosticsMetadata -Context $context -Status 'Completed'
        Write-Success "Dump written to: $outputPath"
        return 0
    } catch {
        [void]$context.Warnings.Add($_.Exception.Message)
        Update-DiagnosticsMetadata -Context $context -Status 'Failed' -ErrorMessage $_.Exception.Message
        Write-Failure "Dump failed: $($_.Exception.Message)"
        return 1
    }
}

function Invoke-DiagnosticsSymbols {
    param(
        [object]$Parsed,
        [string]$RepoRoot,
        [string]$CommandLine
    )

    if ($Parsed.Positionals.Count -eq 0) {
        throw 'Usage: devops diagnostics symbols <artifact> [-- <dotnet-symbol arguments>]'
    }
    $artifact = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Parsed.Positionals[0]))
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Diagnostic artifact was not found: $artifact"
    }

    $context = New-DiagnosticsContext -CommandLine $CommandLine -RepoRoot $RepoRoot -Tool 'dotnet-symbol'
    $arguments = @($artifact, '--output', $context.RunDirectory) + @($Parsed.PassThrough)
    try {
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-symbol' -Arguments $arguments)
        Update-DiagnosticsMetadata -Context $context -Status 'Completed'
        Write-Success "Symbols written to: $($context.RunDirectory)"
        return 0
    } catch {
        [void]$context.Warnings.Add($_.Exception.Message)
        Update-DiagnosticsMetadata -Context $context -Status 'Failed' -ErrorMessage $_.Exception.Message
        Write-Failure "Symbol download failed: $($_.Exception.Message)"
        return 1
    }
}

function Invoke-DiagnosticsCapture {
    param(
        [object]$Parsed,
        [string]$RepoRoot,
        [string]$CommandLine
    )

    $process = Resolve-DiagnosticProcess -Parsed $Parsed
    $duration = Get-DiagnosticDuration -Parsed $Parsed
    $durationText = $duration.ToString('c')
    $context = New-DiagnosticsContext -CommandLine $CommandLine -RepoRoot $RepoRoot `
        -ProcessId $process.Id -Tool 'capture'
    $failures = [System.Collections.Generic.List[string]]::new()

    $countersName = 'counters.json'
    $countersPath = Join-Path $context.RunDirectory $countersName
    try {
        $counterArgs = @('collect', '--process-id', $process.Id.ToString(), '--duration', $durationText,
            '--format', 'json', '--output', $countersPath) + @($Parsed.PassThrough)
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-counters' -Arguments $counterArgs -OutputName $countersName -Timeout ($duration.Add([TimeSpan]::FromSeconds(30))))
    } catch {
        [void]$failures.Add("Counters: $($_.Exception.Message)")
    }

    $traceName = 'capture.nettrace'
    $tracePath = Join-Path $context.RunDirectory $traceName
    try {
        $traceArgs = @('collect', '--process-id', $process.Id.ToString(), '--duration', $durationText,
            '--output', $tracePath) + @($Parsed.PassThrough)
        [void](Invoke-DiagnosticTool -Context $context -ToolName 'dotnet-trace' -Arguments $traceArgs -OutputName $traceName -Timeout ($duration.Add([TimeSpan]::FromSeconds(30))))
    } catch {
        [void]$failures.Add("Trace: $($_.Exception.Message)")
    }

    foreach ($failure in $failures) {
        [void]$context.Warnings.Add($failure)
    }
    if ($failures.Count -gt 0) {
        Update-DiagnosticsMetadata -Context $context -Status 'Failed' -ErrorMessage ($failures -join '; ')
        Write-Failure "Diagnostic capture completed with $($failures.Count) failure(s): $($context.RunDirectory)"
        return 1
    }

    Update-DiagnosticsMetadata -Context $context -Status 'Completed'
    Write-Success "Diagnostic capture written to: $($context.RunDirectory)"
    return 0
}
