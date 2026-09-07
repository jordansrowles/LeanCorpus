$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsCoverage {
    param([string[]]$Arguments = @())

    try {
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $frameworkWasSpecified = $parsed.Has('Framework')
        $framework = [string]$parsed.Get('Framework', (Get-DefaultFramework))
        $configuration = [string]$parsed.Get('Configuration', 'Release')
        $suite = ([string]$parsed.Get('Suite', 'all')).ToLowerInvariant()
        $clean = $parsed.Has('Clean')
        $includePerformance = $parsed.Has('IncludePerformance')
        $generateReport = $parsed.Has('GenerateReport')
        $repoRoot = Get-RepoRoot
        $testSuites = Get-TestSuiteRegistry
        $eligibleSuites = @(Get-CoverageSuiteKeys -TestSuites $testSuites)

        if ($suite -ne 'all' -and $suite -notin $eligibleSuites) {
            throw "Unknown or ineligible coverage suite '$suite'. Eligible suites: $($eligibleSuites -join ', ')."
        }

        $coverageRoot = Get-ArtifactKindRoot -Kind coverage -RepoRoot $repoRoot
        if ($clean -and (Test-Path $coverageRoot)) {
            Remove-OwnedArtifactPath -Path $coverageRoot -ArtifactRoot (Get-ArtifactRoot -RepoRoot $repoRoot)
        }
        $commandLine = ConvertTo-CommandLineText -Command './devops coverage' -Arguments $Arguments
        $coverageRun = New-ArtifactRun -Kind coverage -Framework $framework -Configuration $configuration `
            -Target $suite -CommandLine $commandLine -RepoRoot $repoRoot
        $resultsDir = Join-Path $coverageRun.RunDirectory 'raw'
        [void][System.IO.Directory]::CreateDirectory($resultsDir)

        $filter = if ($includePerformance) { '' } else { 'Coverage!=Skip' }
        $resolutionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $targets = @(
            Resolve-TestTargets -Suite $suite -Framework $framework -FrameworkExplicit $frameworkWasSpecified `
                -Configuration $configuration -Filter $filter -Ci $false -CollectCoverage $true `
                -RepoRoot $repoRoot -TestSuites $testSuites
        )
        $resolutionStopwatch.Stop()

        $options = [pscustomobject]@{
            Count = 1
            Flaky = $false
            FailFast = $false
            Diagnostics = $false
            Ci = $false
            CollectCoverage = $true
            ExplicitMode = 'off'
            ParallelProfile = 'integration'
            FailWarnings = $false
            ArtifactsEnabled = $true
            Configuration = $configuration
            RequestedFramework = if ($frameworkWasSpecified) { $framework } else { '' }
            RuntimeIdentifier = ''
            Area = ''
            Category = ''
            Filter = $filter
            Verbosity = ''
            HangTimeout = 'off'
            ProcessTimeout = [TimeSpan]::Zero
            ResolutionDuration = $resolutionStopwatch.Elapsed
            PassThrough = @()
            CoverageResultsDirectory = $resultsDir
        }

        $exitCode = Invoke-TestPipeline -Targets $targets -Options $options -CommandLine $commandLine `
            -DisplayName 'Coverage test run' -RepoRoot $repoRoot

        $xmlFiles = @(Find-CoverageResults $resultsDir)
        Write-Host ''
        Write-Success "Coverage data written to: $($coverageRun.RunDirectory)"
        Write-Host "  Found $($xmlFiles.Count) coverage file(s)."

        if ($generateReport -and $xmlFiles.Count -gt 0) {
            New-CoverageReport -XmlFiles $xmlFiles -OutputDir (Join-Path $coverageRun.RunDirectory 'html')
        }

        $coverageStatus = if ($exitCode -eq 0 -and $xmlFiles.Count -gt 0) { 'Passed' } else { 'Failed' }
        Complete-ArtifactRun -RunDirectory $coverageRun.RunDirectory -Status $coverageStatus `
            -AdditionalValues @{ coverageFiles = $xmlFiles.Count; raw = 'raw' }

        return $exitCode
    } catch {
        Write-Failure "Coverage command failed: $($_.Exception.Message)"
        return 1
    }
}
