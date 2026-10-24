[CmdletBinding()]
param(
    [string]$PlanOnly = 'false',
    [string]$CollectedReportsPath = 'collected-run-reports.json',
    [string]$OutputPath = 'qualification-summary.json'
)

$ErrorActionPreference = 'Stop'
$shardExitVariables = @('SHARD1_EXIT', 'SHARD2_EXIT', 'SHARD3_EXIT', 'SHARD4_EXIT')
$expectedRunCount = 5 # One smoke run and four fixed comparison shards.
$isPlanOnly = $PlanOnly.Equals('true', [System.StringComparison]::OrdinalIgnoreCase)
$failureReasons = [System.Collections.Generic.List[string]]::new()
$failedShards = [System.Collections.Generic.List[string]]::new()
$collected = @()

if (Test-Path -LiteralPath $CollectedReportsPath -PathType Leaf) {
    $parsed = Get-Content -LiteralPath $CollectedReportsPath -Raw | ConvertFrom-Json
    if ($null -ne $parsed) {
        $collected = @($parsed)
    }
}

$runs = @(
    foreach ($item in $collected) {
        $report = $item.report
        [ordered]@{
            runId = $report.runId
            status = $report.status
            execution = $report.execution
            qualificationState = $report.comparisonAssessment.qualificationState
            eligibleForRatio = [bool]$report.comparisonAssessment.eligibleForRatio
            reasonCodes = @($report.comparisonAssessment.reasonCodes)
            reportPath = $item.path
        }
    }
)

if (-not $isPlanOnly) {
    if ($runs.Count -ne $expectedRunCount) {
        $failureReasons.Add("Expected $expectedRunCount run reports but collected $($runs.Count).")
    }

    foreach ($name in $shardExitVariables) {
        $rawExitCode = [Environment]::GetEnvironmentVariable($name)
        if ([string]::IsNullOrWhiteSpace($rawExitCode)) {
            $failedShards.Add("$name (missing)")
            continue
        }

        $exitCode = 0
        if (-not [int]::TryParse($rawExitCode, [ref]$exitCode)) {
            $failedShards.Add("$name (invalid exit code)")
        }
        elseif ($exitCode -ne 0) {
            $failedShards.Add("$name ($exitCode)")
        }
    }

    if ($failedShards.Count -gt 0) {
        $failureReasons.Add('One or more fixed benchmark shards failed, were cancelled, or did not report an exit code.')
    }
}

$failedRuns = @($runs | Where-Object { $_.status -ne 'Passed' -or $_.execution.classification -ne 'Completed' })
if ($failedRuns.Count -gt 0) {
    $failureReasons.Add('One or more benchmark runs did not complete successfully.')
}

$summary = [ordered]@{
    schemaVersion = 1
    planOnly = $isPlanOnly
    sourceCommit = (git rev-parse HEAD).Trim()
    expectedRunCount = if ($isPlanOnly) { 0 } else { $expectedRunCount }
    runCount = $runs.Count
    qualifiedPairCount = @($runs | Where-Object { $_.eligibleForRatio }).Count
    failedShards = @($failedShards)
    failureReasons = @($failureReasons)
    runs = $runs
    comparativeValuesPublished = $false
}

$summary | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputPath -Encoding utf8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value '## Benchmark qualification summary'
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ''
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "Runs collected: $($summary.runCount). Qualified pairs: $($summary.qualifiedPairCount)."
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value 'Comparative values remain unpublished by this bounded workflow.'
    if ($failureReasons.Count -gt 0) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "Failed shards: $($failedShards -join ', ')."
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "Failure reasons: $($failureReasons -join '; ')."
    }
}

if ($failureReasons.Count -gt 0) {
    exit 1
}
