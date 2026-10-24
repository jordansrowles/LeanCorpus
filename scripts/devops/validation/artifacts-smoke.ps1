Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "leancorpus-artifacts-smoke-$([Guid]::NewGuid().ToString('N'))"
$modulePath = Join-Path $PSScriptRoot '../DevOps.psm1'
$generatorPath = Join-Path $PSScriptRoot '../../benchmarks/generate-docs.ps1'

try {
    [void][System.IO.Directory]::CreateDirectory($temporaryRoot)
    $devopsModule = Import-Module -Name $modulePath -Force -PassThru

    & $devopsModule {
        param(
            [string]$Root,
            [string]$Generator,
            [string]$RepositoryRoot
        )

        function Assert-Smoke {
            param(
                [bool]$Condition,
                [Parameter(Mandatory = $true)]
                [string]$Message
            )

            if (-not $Condition) {
                throw "Artefact smoke assertion failed: $Message"
            }
        }

        function Write-SmokeMarker {
            param([string]$Path)

            $parent = Split-Path -Parent $Path
            [void][System.IO.Directory]::CreateDirectory($parent)
            [System.IO.File]::WriteAllText($Path, 'smoke')
        }

        # A. Common run lifecycle.
        $run = New-ArtifactRun -Kind test -Framework net10.0 -Configuration Release `
            -CommandLine 'artifacts-smoke' -RepoRoot $Root -RunId 'lifecycle'
        $manifestPath = Join-Path $run.RunDirectory 'run.json'
        Assert-Smoke (Test-Path $manifestPath -PathType Leaf) 'run.json was not created.'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-Smoke ($manifest.status -eq 'Running') 'new run was not Running.'
        Assert-Smoke ('commit' -in @($manifest.PSObject.Properties.Name)) 'common commit provenance is missing.'
        Update-ArtifactRunManifest -RunDirectory $run.RunDirectory -Values @{ customField = 'retained' }
        Complete-ArtifactRun -RunDirectory $run.RunDirectory -Status Passed
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-Smoke ($manifest.status -eq 'Passed') 'completed run was not Passed.'
        Assert-Smoke ([bool]$manifest.completedAtUtc) 'completion timestamp is missing.'
        Assert-Smoke ($manifest.customField -eq 'retained') 'custom manifest field was lost.'
        $latestRun = Get-LatestSuccessfulArtifactRun -Kind test -RepoRoot $Root
        Assert-Smoke ($null -ne $latestRun -and $latestRun.RunDirectory -eq $run.RunDirectory) 'latest successful run discovery failed.'

        $buildRun = New-ArtifactRun -Kind build -Configuration Release `
            -CommandLine 'artifacts-smoke build' -RepoRoot $Root -RunId 'build-lifecycle'
        Complete-ArtifactRun -RunDirectory $buildRun.RunDirectory -Status Failed `
            -AdditionalValues @{ binaryLog = 'build.binlog'; textLog = 'build.log' }
        $buildManifest = Get-Content -LiteralPath (Join-Path $buildRun.RunDirectory 'run.json') -Raw | ConvertFrom-Json
        Assert-Smoke ($buildManifest.status -eq 'Failed') 'build run was not marked Failed.'
        Assert-Smoke ($buildManifest.binaryLog -eq 'build.binlog') 'build binary log metadata is missing.'

        # B. Clean containment.
        $artifactRoot = Get-ArtifactRoot -RepoRoot $Root
        $ownedFile = Join-Path $artifactRoot 'test/delete-me.txt'
        $outsideFile = Join-Path $Root 'outside/must-survive.txt'
        Write-SmokeMarker $ownedFile
        Write-SmokeMarker $outsideFile
        Invoke-ArtifactClean -Target test -RepoRoot $Root
        Assert-Smoke (-not (Test-Path $ownedFile)) 'owned test artefact was not removed.'
        Assert-Smoke (Test-Path $outsideFile) 'outside file was removed by owned clean.'
        $refused = $false
        try {
            Remove-OwnedArtifactPath -Path (Split-Path -Parent $outsideFile) -ArtifactRoot $artifactRoot
        } catch {
            $refused = $true
        }
        Assert-Smoke $refused 'clean accepted a path outside the artefact root.'
        if (-not $IsWindows) {
            $caseDifferentPath = Join-Path $Root 'ARTIFACTS-case-test'
            Write-SmokeMarker (Join-Path $caseDifferentPath 'must-survive.txt')
            $caseRefused = $false
            try {
                Remove-OwnedArtifactPath -Path $caseDifferentPath -ArtifactRoot $artifactRoot
            } catch {
                $caseRefused = $true
            }
            Assert-Smoke $caseRefused 'Unix clean accepted a case-different sibling.'
            Assert-Smoke (Test-Path $caseDifferentPath) 'Unix clean removed a case-different sibling.'
        }

        # C. Default clean preservation.
        foreach ($relative in @(
            'build/marker.txt',
            'test/marker.txt',
            'coverage/marker.txt',
            'docs/marker.txt',
            'diagnostics/marker.txt',
            'temp/marker.txt',
            'benchmark/runs/marker.txt',
            'benchmark/cache/marker.txt',
            'package/marker.txt'
        )) {
            Write-SmokeMarker (Join-Path $artifactRoot $relative)
        }
        Invoke-ArtifactClean -Target default -RepoRoot $Root
        foreach ($relative in @('build', 'test', 'coverage', 'docs', 'diagnostics', 'temp')) {
            Assert-Smoke (-not (Test-Path (Join-Path $artifactRoot $relative))) "default clean retained $relative."
        }
        foreach ($relative in @('benchmark/runs', 'benchmark/cache', 'package')) {
            Assert-Smoke (Test-Path (Join-Path $artifactRoot $relative)) "default clean removed $relative."
        }

        # D. Benchmark evidence selection and overlay behaviour.
        $benchmarkRoot = Join-Path $artifactRoot 'benchmark/runs/example'
        $coreRoot = Join-Path $benchmarkRoot 'core'
        $suiteRoot = Join-Path $coreRoot 'query'
        [void][System.IO.Directory]::CreateDirectory($suiteRoot)
        Write-AtomicJsonFile -Path (Join-Path $benchmarkRoot 'run-report.json') -Value @{
            schemaVersion = 1
            status = 'Passed'
            projects = @(@{ project = 'core'; status = 'Passed'; exitCode = 0; path = 'core' })
        }
        Write-AtomicJsonFile -Path (Join-Path $benchmarkRoot 'run.json') -Value @{
            runId = 'example'
            status = 'Passed'
            completedAtUtc = '2026-01-01T00:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $coreRoot 'report.json') -Value @{
            totalBenchmarkCount = 1
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            commitHash = 'smoke'
            dotnetVersion = 'smoke'
            provenance = @{ machineName = 'smoke'; effectiveDocCount = 1 }
            suites = @(@{ suiteName = 'query'; failedBenchmarkCount = 0; missingBenchmarkCount = 0 })
        }
        @(
            '| Method | Mean |',
            '| --- | --- |',
            '| Example | 1 ns |'
        ) | Set-Content -LiteralPath (Join-Path $suiteRoot 'example-report-github.md') -Encoding UTF8
        $olderTextRoot = Join-Path $artifactRoot 'benchmark/runs/text-older'
        $olderTextResults = Join-Path $olderTextRoot 'text/results'
        [void][System.IO.Directory]::CreateDirectory($olderTextResults)
        Write-AtomicJsonFile -Path (Join-Path $olderTextRoot 'run.json') -Value @{
            runId = 'text-older'; status = 'Passed'; completedAtUtc = '2026-01-02T00:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $olderTextRoot 'run-report.json') -Value @{
            projects = @(@{ project = 'text'; status = 'Passed'; exitCode = 0; path = 'text' })
        }
        @('| Method | Mean |', '| --- | --- |', '| OLDER-PASSED | 1 ns |') |
            Set-Content -LiteralPath (Join-Path $olderTextResults 'text-report-github.md') -Encoding UTF8

        $newerTextRoot = Join-Path $artifactRoot 'benchmark/runs/text-newer'
        $newerTextResults = Join-Path $newerTextRoot 'text/results'
        [void][System.IO.Directory]::CreateDirectory($newerTextResults)
        Write-AtomicJsonFile -Path (Join-Path $newerTextRoot 'run.json') -Value @{
            runId = 'text-newer'; status = 'Failed'; completedAtUtc = '2026-01-03T00:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $newerTextRoot 'run-report.json') -Value @{
            projects = @(@{ project = 'text'; status = 'Failed'; exitCode = 1; path = 'text' })
        }
        @('| Method | Mean |', '| --- | --- |', '| NEWER-FAILED | 1 ns |') |
            Set-Content -LiteralPath (Join-Path $newerTextResults 'text-report-github.md') -Encoding UTF8

        $compressionRoot = Join-Path $artifactRoot 'benchmark/runs/compression-only'
        $compressionResults = Join-Path $compressionRoot 'compression/results'
        [void][System.IO.Directory]::CreateDirectory($compressionResults)
        Write-AtomicJsonFile -Path (Join-Path $compressionRoot 'run.json') -Value @{
            runId = 'compression-only'; status = 'Passed'; completedAtUtc = '2026-01-02T12:00:00Z'
        }
        Write-AtomicJsonFile -Path (Join-Path $compressionRoot 'run-report.json') -Value @{
            projects = @(@{ project = 'compression'; status = 'Passed'; exitCode = 0; path = 'compression' })
        }
        @('| Method | Mean |', '| --- | --- |', '| COMPRESSION-PASSED | 1 ns |') |
            Set-Content -LiteralPath (Join-Path $compressionResults 'compression-report-github.md') -Encoding UTF8

        $outputRoot = Join-Path $Root 'generated-benchmark-docs'
        [void][System.IO.Directory]::CreateDirectory($outputRoot)
        Set-Content -LiteralPath (Join-Path $outputRoot 'stale.md') -Value 'STALE-OUTPUT' -Encoding UTF8
        $chartBundle = Join-Path $Root 'chart.umd.js'
        Set-Content -LiteralPath $chartBundle -Value 'CHART-BUNDLE' -Encoding UTF8
        & $Generator -BenchDir (Join-Path $artifactRoot 'benchmark/runs') -OutputDir $outputRoot -ChartJsPath $chartBundle
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'query.md')) 'Core benchmark page was not generated.'
        $textPage = Get-Content -LiteralPath (Join-Path $outputRoot 'text.md') -Raw
        Assert-Smoke ($textPage.Contains('OLDER-PASSED')) 'older passed text evidence was not selected.'
        Assert-Smoke (-not $textPage.Contains('NEWER-FAILED')) 'newer failed text evidence replaced passed evidence.'
        Assert-Smoke ((Get-Content -LiteralPath (Join-Path $outputRoot 'compression.md') -Raw).Contains('COMPRESSION-PASSED')) 'compression-only evidence was not generated.'
        Assert-Smoke (-not (Test-Path (Join-Path $outputRoot 'stale.md'))) 'stale generated benchmark output was retained.'
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'overview.md')) 'benchmark overview was not generated.'
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'toc.yml')) 'benchmark TOC was not generated.'
        Assert-Smoke (Test-Path (Join-Path $outputRoot 'chart.umd.js')) 'pinned Chart.js runtime asset was not staged.'

        # E. One-shot and repeated test classifications.
        . (Join-Path $RepositoryRoot 'scripts/devops/commands/pack.ps1')
        foreach ($case in @(
            @{ Policy = @{ packageId = 'Smoke'; enableApiCompatibility = $true; baselineVersion = '1.0.0' }; Strict = $false; Baseline = '1.0.0' },
            @{ Policy = @{ packageId = 'Smoke'; enableApiCompatibility = $true; baselineVersion = '1.0.0'; strictBaseline = $false }; Strict = $false; Baseline = '1.0.0' },
            @{ Policy = @{ packageId = 'Smoke'; enableApiCompatibility = $true; baselineVersion = '1.0.0'; strictBaseline = $true }; Strict = $true; Baseline = '1.0.0' },
            @{ Policy = @{ packageId = 'Smoke'; enableApiCompatibility = $true; baselineVersion = $null }; Strict = $false; Baseline = '' }
        )) {
            Assert-PackageBaselinePolicy -Entry $case.Policy
            [xml]$props = New-PackageValidationProperties -Entry $case.Policy -ProjectName 'Smoke.Project'
            $group = $props.Project.PropertyGroup
            Assert-Smoke ($group.EnablePackageValidation -ceq 'true') 'SDK package validation was disabled.'
            $strict = $group.SelectSingleNode('EnableStrictModeForBaselineValidation')
            if ($case.Strict) {
                Assert-Smoke ($null -ne $strict -and $strict.InnerText -ceq 'true') 'strict opt-in was not emitted.'
            } else {
                Assert-Smoke ($null -eq $strict) 'normal validation emitted a strict-mode property.'
            }
            $baseline = $group.SelectSingleNode('PackageValidationBaselineVersion')
            if ($case.Baseline) {
                Assert-Smoke ($null -ne $baseline -and $baseline.InnerText -ceq $case.Baseline) 'baseline pin was not retained.'
            } else {
                Assert-Smoke ($null -eq $baseline) 'unpublished package emitted a baseline.'
            }
            Assert-Smoke ($group.Condition -ceq "'`$(MSBuildProjectName)' == 'Smoke.Project'") 'validation leaked into referenced projects.'
        }
        foreach ($invalid in @(
            @{ strictBaseline = 'false'; enableApiCompatibility = $true; baselineVersion = '1.0.0' },
            @{ strictBaseline = 0; enableApiCompatibility = $true; baselineVersion = '1.0.0' },
            @{ strictBaseline = $null; enableApiCompatibility = $true; baselineVersion = '1.0.0' },
            @{ strictBaseline = $true; enableApiCompatibility = $false; baselineVersion = '1.0.0' },
            @{ strictBaseline = $true; enableApiCompatibility = $true; baselineVersion = $null },
            @{ strictBaseline = $true; enableApiCompatibility = $true; baselineVersion = '' },
            @{ strictBaseline = $true; enableApiCompatibility = $true; baselineVersion = ' ' }
        )) {
            $invalid.packageId = 'Invalid.Smoke'
            $message = ''
            try { Assert-PackageBaselinePolicy -Entry $invalid } catch { $message = $_.Exception.Message }
            Assert-Smoke ($message.Contains('Invalid.Smoke')) 'invalid baseline policy was accepted or its package ID was omitted.'
        }

        $pass = [pscustomobject]@{ Outcome = 'Passed' }
        $fail = [pscustomobject]@{ Outcome = 'Failed' }
        Assert-Smoke ((Get-TestObservationClassification @($pass) 1) -eq 'Passed') 'one pass was not classified Passed.'
        Assert-Smoke ((Get-TestObservationClassification @($fail) 1) -eq 'Failed') 'one failure was not classified Failed.'
        Assert-Smoke ((Get-TestObservationClassification @($pass, $pass) 2) -eq 'Always passes') 'repeated passes were misclassified.'
        Assert-Smoke ((Get-TestObservationClassification @($fail, $fail) 2) -eq 'Always fails') 'repeated failures were misclassified.'
        Assert-Smoke ((Get-TestObservationClassification @($pass, $fail) 2) -eq 'Intermittent failure') 'mixed repeats were misclassified.'

        $skip = [pscustomobject]@{ Outcome = 'Skipped' }
        $notExecuted = [pscustomobject]@{ Outcome = 'NotExecuted' }
        $inconclusive = [pscustomobject]@{ Outcome = 'Inconclusive' }
        foreach ($case in @(
            @{ Observations = @($skip); Expected = 1; Want = 'Skipped' },
            @{ Observations = @($skip, $skip); Expected = 2; Want = 'Skipped' },
            @{ Observations = @($skip); Expected = 2; Want = 'Incomplete' },
            @{ Observations = @($pass); Expected = 2; Want = 'Incomplete' },
            @{ Observations = @(); Expected = 1; Want = 'Incomplete' },
            @{ Observations = @($pass, $skip); Expected = 2; Want = 'Incomplete' },
            @{ Observations = @($fail, $skip); Expected = 2; Want = 'Incomplete' },
            @{ Observations = @($pass, $notExecuted); Expected = 2; Want = 'Incomplete' },
            @{ Observations = @($skip, $notExecuted); Expected = 2; Want = 'Incomplete' },
            @{ Observations = @($notExecuted); Expected = 1; Want = 'Incomplete' },
            @{ Observations = @($inconclusive); Expected = 1; Want = 'Incomplete' },
            @{ Observations = @([pscustomobject]@{ Outcome = 'Error' }); Expected = 1; Want = 'Failed' },
            @{ Observations = @([pscustomobject]@{ Outcome = 'Timeout' }); Expected = 1; Want = 'Failed' }
        )) {
            $actual = Get-TestObservationClassification $case.Observations $case.Expected
            Assert-Smoke ($actual -ceq $case.Want) "observations '$(@($case.Observations | ForEach-Object { $_.Outcome }) -join ', ')' / expected $($case.Expected) classified as '$actual', wanted '$($case.Want)'."
        }
        Assert-Smoke ((ConvertTo-NeutralTestOutcome 'NotRunnable') -ceq 'Skipped') 'explicit NotRunnable outcome was not Skipped.'
        Assert-Smoke ((ConvertTo-NeutralTestOutcome 'NotExecuted') -ceq 'NotExecuted') 'NotExecuted was collapsed into Skipped.'

        $reportTarget = [pscustomobject]@{
            Key = 'core/net10.0'; Name = 'Core net10.0'; Suite = 'core'; RunnerKind = 'Mtp'
            Project = 'Core.csproj'; Framework = 'net10.0'; Configuration = 'Release'
            RuntimeIdentifier = ''; Filter = ''; Areas = @(); Categories = @()
            CoverageEligible = $false; Capabilities = @(); AdditionalArguments = @()
        }
        $reportOptions = [pscustomobject]@{
            Count = 1; Configuration = 'Release'; RequestedFramework = 'net10.0'; RuntimeIdentifier = ''
            Flaky = $false; Diagnostics = $false; FailFast = $false; Ci = $true; CollectCoverage = $false
        }
        $reportContext = [pscustomobject]@{
            Options = $reportOptions; StartTimeUtc = [DateTime]::UtcNow; EndTimeUtc = [DateTime]::UtcNow
            InfrastructureErrors = @(); ReportErrors = @(); StageTimings = @(); PreparationTimings = @()
            Targets = @($reportTarget); EnvironmentPath = (Join-Path $Root 'smoke-environment.json'); RunDirectory = ''; RunId = 'skipped-smoke'
            CommandLine = 'artifacts-smoke explicit exclusion'
        }
        Write-AtomicJsonFile -Path $reportContext.EnvironmentPath -Value @{
            os = 'smoke'; architecture = 'x64'; processArchitecture = 'x64'; processorName = 'smoke'
            logicalProcessorCount = 1; availableMemoryBytes = 1024; sdkVersion = 'smoke'
            gitCommit = 'smoke'; gitBranch = 'smoke'; gitDirty = $false; environment = @{}
        }
        $reportTest = [pscustomobject]@{
            Identity = 'case:Smoke|Explicit|Boundary'; IdentityStrength = 'ClassMethodDisplay'
            TestId = 'explicit'; TestName = 'Stored Fields: explicit boundary'; ClassName = 'Smoke'
            MethodName = 'Explicit'; Outcome = 'Skipped'; DurationMs = 0
            ErrorMessage = ''; StackTrace = ''
        }
        $reportExecution = [pscustomobject]@{
            Completed = $true; Outcome = 'Passed'; Iteration = 1; DurationMs = 1
            Target = $reportTarget; TestResults = @($reportTest); ArtifactDirectory = ''
            DiagnosticPaths = @(); ProcessId = 1; ExitCode = 0; StartTimeUtc = [DateTime]::UtcNow
            EndTimeUtc = [DateTime]::UtcNow; TimedOut = $false; WasKilled = $false
            CancellationRequested = $false; StdOutPath = ''; StdErrPath = ''; TrxPath = ''; Error = ''
        }
        $reportSummary = New-TestRunSummary -Context $reportContext -Targets @($reportTarget) -ExecutionResults @($reportExecution)
        Assert-Smoke ($reportSummary.Succeeded) 'an intentionally skipped explicit test made the passing target unhealthy.'
        Assert-Smoke ($reportSummary.SkippedTests.Count -eq 1 -and $reportSummary.IncompleteTests.Count -eq 0) 'summary did not separate skipped and incomplete tests.'
        $report = New-TestReportDocument -Context $reportContext -Summary $reportSummary
        Assert-Smoke ($report.skippedTests.Count -eq 1 -and $report.skippedTests[0].classification -ceq 'Skipped') 'JSON report did not represent the explicit test as Skipped.'
        Assert-Smoke ($report.incompleteTests.Count -eq 0) 'JSON report included the explicit test as incomplete.'
        $markdown = New-TestMarkdownReport -Context $reportContext -Summary $reportSummary
        Assert-Smoke ($markdown.Contains('## Skipped tests') -and $markdown.Contains('Stored Fields: explicit boundary')) 'Markdown report did not list skipped tests separately.'
        Assert-Smoke ($markdown.Contains('## Incomplete tests') -and $markdown.Contains('None.')) 'Markdown incomplete section did not remain empty.'

        # F. Windows stress-test timeout and cleanup evidence contract.
        $stressTestPath = Join-Path $RepositoryRoot 'src/devops/Rowles.LeanCorpus.Tests.Core/Index/Integration/CommitSearcherRaceReproTests.cs'
        $stressTest = Get-Content -LiteralPath $stressTestPath -Raw
        Assert-Smoke ($stressTest.Contains('Timeout = 120_000')) 'stress-test timeout is not 120 seconds.'
        Assert-Smoke ($stressTest.Contains('const int iterations = 120')) 'stress iterations changed.'
        Assert-Smoke ($stressTest.Contains('const int pinnedGenerations = 8')) 'pinned generation count changed.'
        foreach ($cleanupStage in @('held searchers released', 'SearcherManager.Dispose', 'IndexWriter.Dispose', 'MMapDirectory.Dispose')) {
            Assert-Smoke ($stressTest.Contains($cleanupStage)) "stress cleanup timing is missing for $cleanupStage."
        }

        # G. Test telemetry mode precedence and the MTP result contract.
        $localOptions = [pscustomobject]@{ Ci = $false; Flaky = $false; Diagnostics = $false }
        $ciOptions = [pscustomobject]@{ Ci = $true; Flaky = $false; Diagnostics = $false }
        $flakyOptions = [pscustomobject]@{ Ci = $false; Flaky = $true; Diagnostics = $false }
        $diagnosticOptions = [pscustomobject]@{ Ci = $false; Flaky = $false; Diagnostics = $true }
        $combinedOptions = [pscustomobject]@{ Ci = $true; Flaky = $true; Diagnostics = $true }
        Assert-Smoke ((Get-TestTelemetryMode $localOptions) -eq 'off') 'ordinary local mode did not disable telemetry.'
        Assert-Smoke ((Get-TestTelemetryMode $ciOptions) -eq 'summary') 'CI did not select summary telemetry.'
        Assert-Smoke ((Get-TestTelemetryMode $flakyOptions) -eq 'summary') 'Flaky mode did not select summary telemetry.'
        Assert-Smoke ((Get-TestTelemetryMode $diagnosticOptions) -eq 'full') 'Diagnostics did not select full telemetry.'
        Assert-Smoke ((Get-TestTelemetryMode $combinedOptions) -eq 'full') 'Diagnostics did not win over CI and flaky mode.'

        $mtpTarget = [pscustomobject]@{
            Filter = ''
            Capabilities = @()
            AdditionalArguments = @()
            CoverageEligible = $false
        }
        $mtpContext = [pscustomobject]@{
            ArtifactsEnabled = $true
            RepoRoot = $RepositoryRoot
            Options = [pscustomobject]@{
                Verbosity = ''
                ParallelProfile = 'integration'
                ExplicitMode = ''
                FailWarnings = $false
                Ci = $true
                CollectCoverage = $false
                Flaky = $false
                Diagnostics = $false
                HangTimeout = 'off'
            }
        }
        $mtpArguments = @(Get-MtpTestArguments -Target $mtpTarget -Context $mtpContext -ArtifactDirectory (Join-Path $Root 'mtp-arguments'))
        Assert-Smoke ('--report-xunit-trx' -in $mtpArguments) 'MTP TRX output was removed.'
        Assert-Smoke ('--report-xunit-trx-filename' -in $mtpArguments -and 'results.trx' -in $mtpArguments) 'MTP TRX filename is missing.'
        Assert-Smoke (-not ($mtpArguments -contains '--report-xunit-ctrf')) 'MTP CTRF output remains enabled.'
        Assert-Smoke (-not ($mtpArguments -contains '--report-xunit-ctrf-filename')) 'MTP CTRF filename remains configured.'
        Assert-Smoke (-not ($mtpArguments -contains 'results.ctrf.json')) 'CTRF output remains configured.'

        $syntheticTrxPath = Join-Path $Root 'synthetic-outcomes.trx'
        $syntheticTrx = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun>
  <Results>
    <UnitTestResult testId="passed" testName="PassedCase" outcome="Passed" />
    <UnitTestResult testId="failed" testName="FailedCase" outcome="Failed" />
    <UnitTestResult testId="skipped" testName="SkippedCase" outcome="Skipped" />
    <UnitTestResult testId="notexecuted" testName="NotExecutedCase" outcome="NotExecuted" />
    <UnitTestResult testId="notrunnable" testName="NotRunnableCase" outcome="NotRunnable" />
    <UnitTestResult testId="unknown" testName="UnknownCase" outcome="FutureRunnerOutcome" />
    <UnitTestResult testId="empty" testName="EmptyOutcomeCase" outcome="" />
    <UnitTestResult testId="missing" testName="MissingOutcomeCase" />
  </Results>
</TestRun>
'@
        [System.IO.File]::WriteAllText($syntheticTrxPath, $syntheticTrx, [System.Text.UTF8Encoding]::new($false))
        $parsedTrx = Read-MtpResults -Path $syntheticTrxPath
        Assert-Smoke ($parsedTrx.Exists -and $parsedTrx.Valid -and $parsedTrx.Status -eq 'Parsed') 'synthetic TRX was not parsed as a valid result.'
        Assert-Smoke ($parsedTrx.Tests.Count -eq 8) 'synthetic TRX result count changed.'
        $parsedOutcomes = @{}
        foreach ($testResult in $parsedTrx.Tests) {
            $parsedOutcomes[$testResult.TestName] = $testResult.Outcome
        }
        foreach ($expectedOutcome in @{
            PassedCase = 'Passed'
            FailedCase = 'Failed'
            SkippedCase = 'Skipped'
            NotExecutedCase = 'NotExecuted'
            NotRunnableCase = 'Skipped'
            UnknownCase = 'FutureRunnerOutcome'
            EmptyOutcomeCase = 'Unknown'
            MissingOutcomeCase = 'Unknown'
        }.GetEnumerator()) {
            Assert-Smoke ($parsedOutcomes[$expectedOutcome.Key] -ceq $expectedOutcome.Value) "TRX outcome '$($expectedOutcome.Key)' was not preserved as '$($expectedOutcome.Value)'."
        }
        Assert-Smoke ((ConvertTo-NeutralTestOutcome -Outcome $null) -eq 'Unknown') 'null TRX outcomes were not classified Unknown.'

        # H. Telemetry path discovery and workflow mode contracts.
        $telemetryTarget = Join-Path $Root 'telemetry-target'
        foreach ($relative in @(
            'telemetry/summary.json',
            'telemetry/tests.ndjson',
            'telemetry/activities.ndjson',
            'telemetry/metrics.ndjson',
            'runtime/counters.ndjson',
            'results.trx',
            'results.ctrf.json'
        )) {
            Write-SmokeMarker (Join-Path $telemetryTarget $relative)
        }
        $telemetryPaths = @(Get-ExecutionDiagnosticPaths -ArtifactDirectory $telemetryTarget)
        foreach ($relative in @('telemetry/summary.json', 'telemetry/tests.ndjson', 'telemetry/activities.ndjson', 'telemetry/metrics.ndjson', 'runtime/counters.ndjson')) {
            $expectedPath = [System.IO.Path]::GetFullPath((Join-Path $telemetryTarget $relative))
            Assert-Smoke ($expectedPath -in $telemetryPaths) "execution telemetry path was not discovered: $relative"
        }
        Assert-Smoke (@($telemetryPaths | Where-Object { [System.IO.Path]::GetFileName($_) -eq 'results.ctrf.json' }).Count -eq 0) 'CTRF result was classified as a diagnostic artefact.'

        $stressWorkflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot '.github/workflows/test-stress.yml') -Raw
        $diagnosticsWorkflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot '.github/workflows/test-diagnostics.yml') -Raw
        Assert-Smoke ($stressWorkflow.Contains('name: Test Stress')) 'stress workflow displayed name is incorrect.'
        Assert-Smoke ($stressWorkflow.Contains("'--ci',") -and $stressWorkflow.Contains("'--count',") -and $stressWorkflow.Contains("'--profile',") -and $stressWorkflow.Contains("'stress',")) 'stress workflow does not pass --ci, count and --profile stress.'
        Assert-Smoke ($stressWorkflow -notmatch '(?m)^      (flaky|diagnostics):') 'stress workflow still has flaky or diagnostics inputs.'
        Assert-Smoke ($diagnosticsWorkflow.Contains('name: Test Diagnostics')) 'diagnostics workflow displayed name is incorrect.'
        Assert-Smoke ($diagnosticsWorkflow.Contains("'--diagnostics',") -and $diagnosticsWorkflow.Contains("'--profile',") -and $diagnosticsWorkflow.Contains("'integration',")) 'diagnostics workflow does not pass --diagnostics and --profile integration.'
        Assert-Smoke ($diagnosticsWorkflow.Contains('name: test-diagnostics-linux') -and $diagnosticsWorkflow.Contains('name: test-diagnostics-windows')) 'diagnostics workflow artefact names are incorrect.'

        foreach ($workflowPath in @(
            '.github/workflows/build.yml',
            '.github/workflows/test-diagnostics.yml',
            '.github/workflows/test-stress.yml'
        )) {
            $workflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot $workflowPath) -Raw
            $buildSteps = [regex]::Matches(
                $workflow,
                '(?ms)^\s*- name: (?:Build solution|Build prepared test output)\s*\r?\n(?<body>.*?)(?=^\s*- name:|\z)'
            )
            Assert-Smoke ($buildSteps.Count -gt 0) "workflow '$workflowPath' has no recognised product build step."
            foreach ($buildStep in $buildSteps) {
                $stepBody = $buildStep.Groups['body'].Value
                Assert-Smoke ($stepBody -match '(?m)^\s*\$managedHost\s*=\s*if \(\$IsWindows\)') "workflow '$workflowPath' does not select the checked-out host for the current OS."
                Assert-Smoke ($stepBody -match '(?m)^\s*&\s*\$managedHost\s+build(?:\s|$)') "workflow '$workflowPath' does not invoke the checked-out host for its product build."
                Assert-Smoke ($stepBody -notmatch '(?i)devops\.ps1\s+build') "workflow '$workflowPath' still invokes the PowerShell dispatcher for a product build."
            }
        }
        $benchmarkWorkflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot '.github/workflows/benchmark.yml') -Raw
        $benchmarkSteps = [regex]::Matches(
            $benchmarkWorkflow,
            '(?ms)^\s*- name: Run benchmarks\s*\r?\n(?<body>.*?)(?=^\s*- name:|\z)'
        )
        Assert-Smoke ($benchmarkSteps.Count -eq 1) 'the manual benchmark workflow does not define one benchmark execution step.'
        $benchmarkStepBody = $benchmarkSteps[0].Groups['body'].Value
        Assert-Smoke ($benchmarkStepBody -match '(?m)^\s*\$managedHost\s*=\s*if \(\$IsWindows\)') 'the benchmark workflow does not select the checked-out host for the current OS.'
        Assert-Smoke ($benchmarkStepBody -match '(?m)^\s*&\s*\$managedHost\s+@benchmarkArguments\b') 'the benchmark workflow does not invoke its arguments through the checked-out host.'
        Assert-Smoke ($benchmarkStepBody -notmatch '(?im)^\s*&\s*\./devops\.ps1\s+@benchmarkArguments') 'the benchmark workflow still invokes the PowerShell dispatcher.'

        $buildWorkflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot '.github/workflows/build.yml') -Raw
        $foundationJob = [regex]::Match(
            $buildWorkflow,
            '(?ms)^  managed-devops-foundation:\s*\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\s*$|\z)'
        )
        Assert-Smoke $foundationJob.Success 'Build workflow is missing the managed DevOps foundation job.'
        $foundationBody = $foundationJob.Groups['body'].Value
        foreach ($platform in @(
            @{ Name = 'linux-arm64'; Runner = 'ubuntu-24.04-arm' },
            @{ Name = 'osx-arm64'; Runner = 'macos-15' }
        )) {
            $platformPattern = '(?m)^\s{10}- platform: ' + [regex]::Escape($platform.Name) + '\s*\r?\n\s{12}runner: ' + [regex]::Escape($platform.Runner) + '\s*$'
            Assert-Smoke ($foundationBody -match $platformPattern) "managed DevOps foundation does not bootstrap '$($platform.Name)' on '$($platform.Runner)'."
        }
        foreach ($packageJob in @(
            @{ Name = 'package-validation'; StepName = 'Validate shipping package APIs and archives'; Expected = 'pack --clean' },
            @{ Name = 'package-consumer'; StepName = 'Validate generated package consumers'; Expected = 'pack --clean --validate-packages' }
        )) {
            $jobMatch = [regex]::Match(
                $buildWorkflow,
                "(?ms)^  $([regex]::Escape($packageJob.Name)):\s*\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\s*$|\z)"
            )
            Assert-Smoke $jobMatch.Success "Build workflow is missing the '$($packageJob.Name)' job."
            $jobBody = $jobMatch.Groups['body'].Value
            Assert-Smoke ($jobBody -match '(?m)^\s{6}- name: Bootstrap managed DevOps host\s*\r?\n\s{8}shell: pwsh\s*\r?\n\s{8}run: pwsh -NoProfile -File \./setup\.ps1\s*$') "package job '$($packageJob.Name)' does not bootstrap the checked-out managed host."
            $bootstrapIndex = $jobBody.IndexOf('- name: Bootstrap managed DevOps host', [StringComparison]::Ordinal)
            $packIndex = $jobBody.IndexOf("- name: $($packageJob.StepName)", [StringComparison]::Ordinal)
            Assert-Smoke ($bootstrapIndex -ge 0 -and $packIndex -gt $bootstrapIndex) "package job '$($packageJob.Name)' does not bootstrap before package validation."
            $packStep = [regex]::Match(
                $jobBody,
                "(?ms)^\s{6}- name: $([regex]::Escape($packageJob.StepName))\s*\r?\n(?<body>.*?)(?=^\s{6}- name:|\z)"
            )
            Assert-Smoke $packStep.Success "package job '$($packageJob.Name)' is missing its package validation step."
            $packStepBody = $packStep.Groups['body'].Value
            Assert-Smoke ($packStepBody -match '(?m)^\s{10}\$managedHost\s*=\s*if \(\$IsWindows\)') "package job '$($packageJob.Name)' does not select the checked-out host for the current OS."
            $packageCommandPattern = '(?m)^\s{10}&\s*\$managedHost\s+' + [regex]::Escape($packageJob.Expected) + '(?:\s|$)'
            Assert-Smoke ($packStepBody -match $packageCommandPattern) "package job '$($packageJob.Name)' does not invoke the managed package route '$($packageJob.Expected)'."
            Assert-Smoke ($packStepBody -notmatch '(?i)devops\.ps1\s+pack') "package job '$($packageJob.Name)' still invokes the PowerShell dispatcher."
        }

        # I. Orchard functional and explicit performance suite separation.
        $testSuites = Get-TestSuiteRegistry
        $functionalProject = 'src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests.csproj'
        $functionalProjectSuites = @($testSuites.Keys | Where-Object { [string]$testSuites[$_].Project -ceq $functionalProject })
        Assert-Smoke ($functionalProjectSuites.Count -eq 1 -and $functionalProjectSuites[0] -ceq 'server-orchard-functional') 'the Orchard functional project is registered more than once or under another suite.'
        Assert-Smoke (-not $testSuites.ContainsKey('server-orchard-performance')) 'the duplicate Orchard performance suite remains registered.'
        Assert-Smoke ([string]$testSuites['server-orchard-functional'].DefaultFilter -ceq
            'FullyQualifiedName!~Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests.ProviderPerformanceTests.') 'the functional suite does not exclude the explicit performance class.'
        $codeAreas = Import-PowerShellDataFile -LiteralPath (Join-Path $RepositoryRoot 'scripts/devops/config/code-areas.psd1')
        foreach ($areaName in $codeAreas.Keys) {
            foreach ($targetName in @($codeAreas[$areaName].Targets)) {
                Assert-Smoke (-not ([string]$targetName).StartsWith('server-orchard-performance:', [StringComparison]::Ordinal)) "affected-test area '$areaName' still references the removed Orchard performance suite."
            }
        }

        # J. Published host consumers must exercise every supported runtime target.
        . (Join-Path $RepositoryRoot 'scripts/devops/packaging/validate-package-consumers.ps1')
        $packagePolicy = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'scripts/devops/packaging/package-policy.json') -Raw | ConvertFrom-Json -AsHashtable
        $consumerScenarios = @(Get-ConsumerScenarioDefinitions -Scope all -Policy $packagePolicy -RepoRoot $RepositoryRoot)
        foreach ($name in @('server-abstractions', 'server-core', 'server-aspnetcore', 'server-grpc', 'studio', 'orchard-search', 'server-local')) {
            $frameworks = @($consumerScenarios | Where-Object Name -eq $name | ForEach-Object Framework | Sort-Object -Unique)
            Assert-Smoke ($frameworks.Count -eq 2 -and 'net10.0' -in $frameworks -and 'net11.0' -in $frameworks) "package consumer '$name' does not cover both host frameworks."
        }

        Write-Host 'Artefact infrastructure smoke validation passed.'
    } $temporaryRoot $generatorPath (Resolve-Path (Join-Path $PSScriptRoot '../../..'))
    exit 0
} catch {
    Write-Error "Artefact infrastructure smoke validation failed: $($_.Exception.Message)"
    exit 1
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
