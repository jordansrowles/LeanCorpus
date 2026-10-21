$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DevOpsEvidenceValue {
    param([object]$Document, [string]$Name, [object]$Default = '')
    if ($null -ne $Document -and $Document.PSObject.Properties[$Name]) { return $Document.$Name }
    return $Default
}

function ConvertTo-DevOpsUiRunManifest {
    param([Parameter(Mandatory = $true)][object]$Manifest)
    if (-not $Manifest.PSObject.Properties['state']) { return $Manifest }
    if ($Manifest.schemaVersion -ne 1) { throw "Unsupported managed run schema: $($Manifest.schemaVersion)" }
    $inputs = Get-DevOpsEvidenceValue $Manifest 'inputs' $null
    $started = Get-DevOpsEvidenceValue $Manifest 'startedUtc'
    $completed = Get-DevOpsEvidenceValue $Manifest 'completedUtc'
    $duration = 0
    if ($started -and $completed) { $duration = ([DateTimeOffset]::Parse($completed) - [DateTimeOffset]::Parse($started)).TotalMilliseconds }
    return [pscustomobject]@{
        runId = $Manifest.runId
        status = [string]$Manifest.state
        startedAtUtc = $started
        completedAtUtc = $completed
        durationMs = $duration
        branch = Get-DevOpsEvidenceValue $Manifest 'sourceBranch'
        commit = Get-DevOpsEvidenceValue $Manifest 'sourceCommit'
        dirty = Get-DevOpsEvidenceValue $Manifest 'sourceDirty' $false
        os = Get-DevOpsEvidenceValue $Manifest 'platform'
        target = Get-DevOpsEvidenceValue $inputs 'targets' (Get-DevOpsEvidenceValue $inputs 'target')
        framework = Get-DevOpsEvidenceValue $inputs 'framework'
        configuration = Get-DevOpsEvidenceValue $inputs 'configuration'
        commandLine = Get-DevOpsEvidenceValue $Manifest 'command'
        error = Get-DevOpsEvidenceValue $Manifest 'failure'
        outputs = @(Get-DevOpsEvidenceValue $Manifest 'artifacts' @())
        toolVersions = Get-DevOpsEvidenceValue $Manifest 'toolVersions' $null
    }
}

function ConvertTo-DevOpsUiTestReport {
    param([Parameter(Mandatory = $true)][object]$Report)
    if (-not $Report.PSObject.Properties['attempts']) { return $Report }
    if ($Report.schemaVersion -ne 1) { throw "Unsupported managed test report schema: $($Report.schemaVersion)" }
    $attempts = @($Report.attempts)
    $targetRows = @($attempts | ForEach-Object {
        [pscustomobject]@{
            iteration = $_.iteration; suite = $_.suite; targetKey = $_.target; outcome = $_.outcome
            durationMs = $_.durationMs
            failedTestCount = @($_.tests | Where-Object { $_.outcome -in @('Failed', 'Error', 'Timeout') }).Count
        }
    })
    $tests = @($Report.tests | ForEach-Object {
        [pscustomobject]@{
            suite = $_.suite; testName = $_.testName; classification = $_.stability
            failedCount = @($_.attempts | Where-Object { $_.outcome -in @('Failed', 'Error', 'Timeout') }).Count
        }
    })
    return [pscustomobject]@{
        summary = [pscustomobject]@{
            succeeded = $Report.succeeded
            requestedTargetExecutions = $Report.requestedExecutions
            passedTargetExecutions = @($attempts | Where-Object outcome -eq 'Passed').Count
            failedTargetExecutions = @($attempts | Where-Object outcome -eq 'AssertionFailure').Count
            crashedTargetExecutions = @($attempts | Where-Object outcome -eq 'Crashed').Count
            hungTargetExecutions = @($attempts | Where-Object outcome -eq 'Hung').Count
            incompleteTargetExecutions = @($attempts | Where-Object outcome -eq 'Incomplete').Count
            cancelledTargetExecutions = @($attempts | Where-Object outcome -eq 'Cancelled').Count
            unscheduledTargetExecutions = Get-DevOpsEvidenceValue $Report 'unscheduledExecutions' ([Math]::Max(0, $Report.requestedExecutions - $attempts.Count))
            totalDurationMs = ($attempts | Measure-Object -Property durationMs -Sum).Sum
        }
        targetResults = $targetRows
        tests = $tests
    }
}

function Invoke-DevOpsUiObserveMenu {
    while ($true) {
        $item = [string](Read-DevOpsUiSelection -Message 'Observe' -Choices @('Runs and artefacts', 'Diagnostics', 'Repository health', 'Back') -EnableSearch)
        switch ($item) {
            'Runs and artefacts' { Invoke-DevOpsUiRuns }
            'Diagnostics' { Invoke-DevOpsUiDiagnostics }
            'Repository health' { Invoke-DevOpsUiReport }
            default { return }
        }
    }
}

function Get-DevOpsUiRecentRuns {
    param([string]$RepoRoot = (Get-RepoRoot))
    $runs = [System.Collections.Generic.List[object]]::new()
    foreach ($kind in @('test', 'coverage', 'diagnostics', 'benchmark')) {
        $root = Get-ArtifactRunsRoot -Kind $kind -RepoRoot $repoRoot
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($directory in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
            $manifestPath = Join-Path $directory.FullName 'run.json'
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                $manifestPath = Join-Path $directory.FullName 'manifest.json'
                if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }
            }
            try {
                $manifest = ConvertTo-DevOpsUiRunManifest (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 32)
                $started = [DateTimeOffset]::MinValue
                [void][DateTimeOffset]::TryParse([string]$manifest.startedAtUtc, [ref]$started)
                [void]$runs.Add([pscustomobject]@{
                    Kind = $kind; RunId = [string]$manifest.runId; Status = [string]$manifest.status
                    Started = $started; Target = [string]$manifest.target; Framework = [string]$manifest.framework
                    Directory = $directory.FullName; Manifest = $manifest
                })
            } catch { continue }
        }
    }
    return @($runs | Sort-Object Started -Descending | Select-Object -First 40)
}

function Invoke-DevOpsUiRuns {
    $runs = @(Get-DevOpsUiRecentRuns)
    if ($runs.Count -eq 0) { Write-DevOpsUiMarkup '[yellow]No structured test, coverage, diagnostics or benchmark runs were found.[/]'; return }
    $rows = @($runs | ForEach-Object { [pscustomobject]@{
        Status = $_.Status; Kind = $_.Kind; Started = if ($_.Started -eq [DateTimeOffset]::MinValue) { 'unknown' } else { $_.Started.ToLocalTime().ToString('HH:mm dd MMM') }
        Target = $_.Target; Framework = $_.Framework; Run = $_.RunId
    } })
    Show-DevOpsUiSummaryTable -Rows $rows
    $selectedId = [string](Read-DevOpsUiSelection -Message 'Select a run to inspect' -Choices @($runs | ForEach-Object { $_.RunId }) -EnableSearch)
    if (-not $selectedId) { return }
    $run = @($runs | Where-Object RunId -eq $selectedId | Select-Object -First 1)[0]
    $manifest = $run.Manifest
    Write-SpectreRule -PassThru -Title "$($run.Kind) $($run.Status): $($run.RunId)" | Out-SpectreHost | Out-Host
    foreach ($property in @('branch', 'commit', 'dirty', 'os', 'architecture', 'machine', 'framework', 'configuration', 'target', 'commandLine', 'startedAtUtc', 'completedAtUtc', 'durationMs', 'error', 'toolVersions')) {
        if ($manifest.PSObject.Properties[$property] -and $null -ne $manifest.$property) { Write-DevOpsUiMarkup (ConvertTo-DevOpsUiLiteralText ("{0}: {1}" -f $property, $manifest.$property)) }
    }
    foreach ($name in @('summary', 'report', 'projects', 'outputs', 'warnings', 'failures')) {
        if ($manifest.PSObject.Properties[$name] -and $null -ne $manifest.$name) {
            Write-DevOpsUiMarkup (ConvertTo-DevOpsUiLiteralText "$name`:`n$($manifest.$name | ConvertTo-Json -Depth 8)")
        }
    }
    $testReportPath = Join-Path $run.Directory 'report.json'
    if ($run.Kind -eq 'test' -and (Test-Path -LiteralPath $testReportPath -PathType Leaf)) {
        $testReport = ConvertTo-DevOpsUiTestReport (Get-Content -LiteralPath $testReportPath -Raw | ConvertFrom-Json -Depth 32)
        if ($null -ne $testReport.summary) {
            Show-DevOpsUiSummaryTable -Rows @([pscustomobject]@{
                Succeeded = $testReport.summary.succeeded
                Targets = "$($testReport.summary.passedTargetExecutions)/$($testReport.summary.requestedTargetExecutions)"
                Failed = $testReport.summary.failedTargetExecutions
                Crashed = $testReport.summary.crashedTargetExecutions
                Hung = $testReport.summary.hungTargetExecutions
                Incomplete = Get-DevOpsEvidenceValue $testReport.summary 'incompleteTargetExecutions' 'unknown'
                Cancelled = Get-DevOpsEvidenceValue $testReport.summary 'cancelledTargetExecutions' 'unknown'
                Unscheduled = Get-DevOpsEvidenceValue $testReport.summary 'unscheduledTargetExecutions' 'unknown'
                DurationSeconds = [Math]::Round([double]$testReport.summary.totalDurationMs / 1000, 2)
            })
        }
        $targetRows = @($testReport.targetResults)
        if ($targetRows.Count -gt 0) {
            Show-DevOpsUiSummaryTable -Rows @($targetRows | ForEach-Object { [pscustomobject]@{
                Iteration = $_.iteration; Suite = $_.suite; Target = $_.targetKey; Outcome = $_.outcome
                DurationSeconds = [Math]::Round([double]$_.durationMs / 1000, 2); FailedTests = $_.failedTestCount
            } })
        }
        foreach ($failure in @($testReport.tests | Where-Object { [int]$_.failedCount -gt 0 } | Select-Object -First 20)) {
            Write-DevOpsUiMarkup (ConvertTo-DevOpsUiLiteralText ("Failure: {0} | {1} | {2}" -f $failure.suite, $failure.testName, $failure.classification))
        }
    }
    Write-SpectreRule -PassThru -Title 'Artefact tree' | Out-SpectreHost | Out-Host
    Format-SpectreTree -Data (New-DevOpsUiArtifactTreeNode -Path $run.Directory -RootPath $run.Directory) | Out-SpectreHost | Out-Host
    Write-DevOpsUiMarkup (ConvertTo-DevOpsUiLiteralText "Run directory: $($run.Directory)")
}

function New-DevOpsUiArtifactTreeNode {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$RootPath)
    $item = Get-Item -LiteralPath $Path -Force
    $children = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($directory in @(Get-ChildItem -LiteralPath $Path -Directory -Force -ErrorAction SilentlyContinue | Sort-Object Name)) {
        [void]$children.Add((New-DevOpsUiArtifactTreeNode -Path $directory.FullName -RootPath $RootPath))
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Force -ErrorAction SilentlyContinue | Sort-Object Name)) {
        $name = [IO.Path]::GetRelativePath($RootPath, $file.FullName)
        [void]$children.Add(@{ Value = (ConvertTo-DevOpsUiLiteralText ("{0} ({1} bytes)" -f $name, $file.Length)); Children = @() })
    }
    $label = if ([string]::Equals($Path, $RootPath, [StringComparison]::OrdinalIgnoreCase)) { $item.Name } else { $item.Name }
    return @{ Value = (ConvertTo-DevOpsUiLiteralText $label); Children = @($children.ToArray()) }
}

function Get-DevOpsUiDotNetProcesses {
    $matches = [System.Collections.Generic.List[object]]::new()
    foreach ($process in Get-Process -ErrorAction SilentlyContinue | Sort-Object Id) {
        $processPath = ''
        try { $processPath = [string]$process.Path } catch { }
        $isDotNet = $process.ProcessName -match '^(dotnet|Rowles\.|LeanCorpus)' -or $processPath -match 'dotnet'
        if (-not $isDotNet) {
            try { $isDotNet = @($process.Modules | Where-Object { $_.ModuleName -match '^(coreclr\.dll|libcoreclr\.so|libcoreclr\.dylib)$' }).Count -gt 0 } catch { }
        }
        if ($isDotNet) {
            $pathLabel = if ($processPath) { [IO.Path]::GetFileName($processPath) } else { '(path unavailable)' }
            [void]$matches.Add([pscustomobject]@{ Id = [int]$process.Id; Name = [string]$process.ProcessName; Path = $processPath; Label = ("{0} ({1}) {2}" -f $process.ProcessName, $process.Id, $pathLabel) })
        }
    }
    return @($matches)
}

function Invoke-DevOpsUiDiagnostics {
    $workflow = [string](Read-DevOpsUiSelection -Message 'Diagnostics workflow' -Choices @('Attach to a running .NET process', 'Download symbols for an artifact', 'Back'))
    if ($workflow -eq 'Back' -or -not $workflow) { return }
    if ($workflow -eq 'Download symbols for an artifact') {
        $artifact = Read-DevOpsUiText -Message 'Artifact path or URL'
        $null = Invoke-DevOpsUiCommand -Arguments @('diagnostics', 'symbols', $artifact) -Title 'Downloading diagnostic symbols'
        return
    }
    $processes = @(Get-DevOpsUiDotNetProcesses)
    if ($processes.Count -eq 0) { Write-DevOpsUiMarkup '[yellow]No running .NET processes were discovered.[/]'; return }
    $process = Read-DevOpsUiSelection -Message 'Select a running .NET process' -Choices $processes -ChoiceLabelProperty Label -EnableSearch
    if ($null -eq $process) { return }
    $action = [string](Read-DevOpsUiSelection -Message "Diagnostics action for $($process.Name) ($($process.Id))" -Choices @('Counters', 'Trace', 'GC dump', 'Dump', 'Capture', 'Back'))
    if ($action -eq 'Back' -or -not $action) { return }
    $subcommand = switch ($action) { 'Counters' { 'counters' } 'Trace' { 'trace' } 'GC dump' { 'gcdump' } 'Dump' { 'dump' } 'Capture' { 'capture' } }
    $arguments = @('diagnostics', $subcommand, '--pid', [string]$process.Id)
    if ($action -eq 'Dump') {
        Write-DevOpsUiMarkup '[yellow]A dump may contain sensitive application memory.[/]'
        $dumpType = [string](Read-DevOpsUiSelection -Message 'Dump type' -Choices @('Mini', 'Heap', 'Full', 'Triage'))
        $arguments += @('--type', $dumpType)
    }
    if ($action -eq 'Capture') { $arguments += @('--duration', (Read-DevOpsUiText -Message 'Capture duration' -DefaultAnswer '5s')) }
    $null = Invoke-DevOpsUiCommand -Arguments $arguments -Title "Capturing $action diagnostics for $($process.Name)"
}

function Invoke-DevOpsUiReport {
    $group = [string](Read-DevOpsUiSelection -Message 'Repository health report' -Choices @('all', 'git', 'files', 'code'))
    $top = Read-DevOpsUiText -Message 'Entries per report list' -DefaultAnswer '10'
    $arguments = @('report', $group, '-Top', $top)
    if ($group -ne 'git') {
        $path = Read-DevOpsUiText -Message 'Optional file glob (blank includes all paths)' -AllowEmpty
        if ($path) { $arguments += @('-Path', $path) }
    }
    if (Read-DevOpsUiSwitch -Message 'Use strict health checks?') { $arguments += '-Strict' }
    if (Read-DevOpsUiSwitch -Message 'Emit JSON output?') { $arguments += '-Json' }
    $null = Invoke-DevOpsUiCommand -Arguments $arguments -Title 'Generating repository health report'
}
