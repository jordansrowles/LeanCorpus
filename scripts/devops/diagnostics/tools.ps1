$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DiagnosticToolPackageName {
    param([string]$ToolName)

    switch ($ToolName) {
        'dotnet-counters' { return 'dotnet-counters' }
        'dotnet-trace'    { return 'dotnet-trace' }
        'dotnet-gcdump'   { return 'dotnet-gcdump' }
        'dotnet-dump'     { return 'dotnet-dump' }
        'dotnet-symbol'   { return 'dotnet-symbol' }
        default { throw "Unsupported diagnostic tool '$ToolName'." }
    }
}

function New-DiagnosticsContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandLine,
        [string]$RepoRoot = (Get-RepoRoot),
        [int]$ProcessId = 0,
        [string]$Tool = ''
    )

    $runId = Get-TestRunId
    $runDirectory = Join-Path $RepoRoot "artifacts/diagnostics/$runId"
    [void][System.IO.Directory]::CreateDirectory($runDirectory)

    $context = [pscustomobject]@{
        RunId = $runId
        RunDirectory = $runDirectory
        RepoRoot = $RepoRoot
        CommandLine = $CommandLine
        ProcessId = if ($ProcessId -gt 0) { $ProcessId } else { $null }
        Tool = $Tool
        StartTimeUtc = [DateTime]::UtcNow
        EndTimeUtc = $null
        Outputs = [System.Collections.Generic.List[string]]::new()
        Warnings = [System.Collections.Generic.List[string]]::new()
        MetadataPath = Join-Path $runDirectory 'metadata.json'
        EnvironmentPath = Join-Path $runDirectory 'environment.json'
    }

    $environment = Get-TestEnvironmentSnapshot -RepoRoot $RepoRoot -CommandLine $CommandLine
    Write-AtomicJsonFile -Path $context.EnvironmentPath -Value $environment
    Update-DiagnosticsMetadata -Context $context -Status 'Running'
    return $context
}

function Update-DiagnosticsMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [string]$Status = 'Running',
        [string]$ErrorMessage = ''
    )

    $Context.EndTimeUtc = if ($Status -eq 'Running') { $null } else { [DateTime]::UtcNow }
    $durationMs = if ($Context.EndTimeUtc) {
        ($Context.EndTimeUtc - $Context.StartTimeUtc).TotalMilliseconds
    } else {
        ([DateTime]::UtcNow - $Context.StartTimeUtc).TotalMilliseconds
    }

    $document = [ordered]@{
        schemaVersion = 1
        runId = $Context.RunId
        commandLine = $Context.CommandLine
        status = $Status
        startTimeUtc = $Context.StartTimeUtc.ToString('O')
        endTimeUtc = if ($Context.EndTimeUtc) { $Context.EndTimeUtc.ToString('O') } else { $null }
        durationMs = [Math]::Round($durationMs, 3)
        processId = $Context.ProcessId
        tool = $Context.Tool
        outputs = @($Context.Outputs)
        warnings = @($Context.Warnings)
        error = $ErrorMessage
        environmentPath = 'environment.json'
    }
    Write-AtomicJsonFile -Path $Context.MetadataPath -Value $document
}

function Resolve-DiagnosticProcess {
    param([object]$Parsed)

    $value = if ($Parsed.Has('Pid')) {
        $Parsed.Get('Pid', '')
    } elseif ($Parsed.Has('p')) {
        $Parsed.Get('p', '')
    } else {
        ''
    }
    $processId = 0
    if (-not [int]::TryParse([string]$value, [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$processId) -or $processId -le 0) {
        throw '--pid must be a positive process ID.'
    }

    try {
        $process = Get-Process -Id $processId -ErrorAction Stop
    } catch {
        throw "Process $processId could not be found: $($_.Exception.Message)"
    }

    return $process
}

function Invoke-DiagnosticTool {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context,
        [Parameter(Mandatory = $true)]
        [string]$ToolName,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [string]$OutputName = '',
        [TimeSpan]$Timeout = [TimeSpan]::Zero
    )

    $packageName = Get-DiagnosticToolPackageName -ToolName $ToolName
    [void](Assert-DotNetTool -ToolName $ToolName -PackageName $packageName)

    $safeName = ($ToolName -replace '[^A-Za-z0-9_.-]', '_')
    $stdoutPath = Join-Path $Context.RunDirectory "$safeName.stdout.log"
    $stderrPath = Join-Path $Context.RunDirectory "$safeName.stderr.log"
    [void]$Context.Outputs.Add((Get-TestArtifactRelativePath -Context ([pscustomobject]@{ RunDirectory = $Context.RunDirectory }) -Path $stdoutPath))
    [void]$Context.Outputs.Add((Get-TestArtifactRelativePath -Context ([pscustomobject]@{ RunDirectory = $Context.RunDirectory }) -Path $stderrPath))
    if ($OutputName) {
        $outputPath = Join-Path $Context.RunDirectory $OutputName
        [void]$Context.Outputs.Add((Get-TestArtifactRelativePath -Context ([pscustomobject]@{ RunDirectory = $Context.RunDirectory }) -Path $outputPath))
    }

    $result = Invoke-ProcessWithLifecycle -FileName $ToolName -Arguments $Arguments `
        -WorkingDirectory $Context.RepoRoot -StdOutPath $stdoutPath -StdErrPath $stderrPath `
        -CaptureOutput $true -MirrorOutput $true -Timeout $Timeout
    if ($result.CancellationRequested) {
        throw "$ToolName was cancelled."
    }
    if ($result.TimedOut) {
        throw "$ToolName timed out after $($Timeout.ToString())."
    }
    if ($null -eq $result.ExitCode -or [int]$result.ExitCode -ne 0) {
        throw "$ToolName exited with code $($result.ExitCode). See $stderrPath."
    }

    return $result
}
