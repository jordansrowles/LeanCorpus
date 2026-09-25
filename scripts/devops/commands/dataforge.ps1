$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsDataForge {
    param([string[]]$Arguments = @())

    if ($Arguments.Count -gt 0 -and $Arguments[0] -eq 'qualify') {
        $qualificationArguments = @($Arguments | Select-Object -Skip 1)
        $qualificationExitCode = Invoke-DataForgeQualification -Arguments $qualificationArguments
        exit $qualificationExitCode
    }

    $repoRoot = Get-RepoRoot
    $projectPath = Join-Path $repoRoot 'src/devops/Rowles.DataForge.Tool/Rowles.DataForge.Tool.csproj'
    $dotnetArguments = @(
        'run',
        '--project', $projectPath,
        '--framework', 'net10.0',
        '--configuration', 'Release',
        '--'
    )
    $dotnetArguments += $Arguments

    Write-Heading 'Running DataForge'
    $argumentLog = ($dotnetArguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    Write-Host "  dotnet $argumentLog" -ForegroundColor DarkGray
    Push-Location $repoRoot
    try {
        dotnet @dotnetArguments
        $dotnetExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    exit $dotnetExitCode
}

function Invoke-DataForgeQualification {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $unknownOptions = @($parsed.Parsed.Keys | Where-Object { $_ -notin @('ReferencePath', 'Dry') })
    if ($unknownOptions.Count -gt 0 -or $parsed.Positionals.Count -gt 0 -or $parsed.PassThrough.Count -gt 0) {
        Write-Error 'Usage: devops dataforge qualify [-ReferencePath <path>] [-Dry]'
        return 1
    }

    $repoRoot = Get-RepoRoot
    $requestedReferencePath = [string]$parsed.Get('ReferencePath', '')
    $referencePath = if ($requestedReferencePath) {
        if ([System.IO.Path]::IsPathRooted($requestedReferencePath)) {
            [System.IO.Path]::GetFullPath($requestedReferencePath)
        } else {
            [System.IO.Path]::GetFullPath((Join-Path $repoRoot $requestedReferencePath))
        }
    } else {
        Join-Path $repoRoot 'artifacts/dataforge/reference/leancorpus-wikipedia-en-v1'
    }

    $commands = [System.Collections.Generic.List[object]]::new()
    [void]$commands.Add([pscustomobject]@{ Group = 'build'; Name = 'Solution build'; Arguments = @('build'); RunKind = 'build' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-reference'; Name = 'Verify frozen Wikipedia reference'; Arguments = @('dataforge', 'reference', 'verify', '-ReferencePath', $referencePath); RunKind = '' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-tests'; Name = 'DataForge net10.0'; Arguments = @('test', '-Suite', 'dataforge', '-Framework', 'net10.0', '-CI'); RunKind = 'test' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-tests'; Name = 'DataForge net11.0'; Arguments = @('test', '-Suite', 'dataforge', '-Framework', 'net11.0', '-CI'); RunKind = 'test' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-tests'; Name = 'Architecture net10.0'; Arguments = @('test', '-Suite', 'architecture', '-Framework', 'net10.0', '-CI'); RunKind = 'test' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-tests'; Name = 'Server integration net11.0'; Arguments = @('test', '-Suite', 'server-integration', '-Framework', 'net11.0', '-CI'); RunKind = 'test' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-benchmarks'; Name = 'Term search smoke'; Arguments = @('benchmark', '-Suite', 'query', '-Strat', 'fast'); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-benchmarks'; Name = 'HNSW smoke'; Arguments = @('benchmark', '-Suite', 'hnsw', '-Strat', 'fast'); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-benchmarks'; Name = 'Vector quantisation smoke'; Arguments = @('benchmark', '-Suite', 'vq', '-Strat', 'fast'); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-benchmarks'; Name = 'Hybrid search smoke'; Arguments = @('benchmark', '-Suite', 'hybrid', '-Strat', 'fast'); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'synthetic-benchmarks'; Name = 'Rowles.Text multilingual smoke'; Arguments = @('benchmark', '-Suite', 'text', '-Strat', 'fast', '--', '--filter', '*MultiLanguageAnalysisBenchmarks*'); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-smoke'; Name = 'Wikipedia indexing smoke'; Arguments = @('benchmark', '-Suite', 'index', '-Strat', 'fast', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-smoke'; Name = 'Wikipedia term search smoke'; Arguments = @('benchmark', '-Suite', 'query', '-Strat', 'fast', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-smoke'; Name = 'Wikipedia phrase search smoke'; Arguments = @('benchmark', '-Suite', 'phrase', '-Strat', 'fast', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-smoke'; Name = 'Wikipedia prefix search smoke'; Arguments = @('benchmark', '-Suite', 'prefix', '-Strat', 'fast', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-smoke'; Name = 'Wikipedia wildcard search smoke'; Arguments = @('benchmark', '-Suite', 'wildcard', '-Strat', 'fast', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-smoke'; Name = 'Wikipedia regexp search smoke'; Arguments = @('benchmark', '-Suite', 'regexp', '-Strat', 'fast', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-benchmarks'; Name = 'Wikipedia indexing'; Arguments = @('benchmark', '-Suite', 'index', '-Strat', 'default', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-benchmarks'; Name = 'Wikipedia term search'; Arguments = @('benchmark', '-Suite', 'query', '-Strat', 'default', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-benchmarks'; Name = 'Wikipedia phrase search'; Arguments = @('benchmark', '-Suite', 'phrase', '-Strat', 'default', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-benchmarks'; Name = 'Wikipedia multi-term search'; Arguments = @('benchmark', '-Suite', 'terminset', '-Strat', 'default', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-benchmarks'; Name = 'Wikipedia highlighting'; Arguments = @('benchmark', '-Suite', 'highlighter', '-Strat', 'default', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })
    [void]$commands.Add([pscustomobject]@{ Group = 'wikipedia-benchmarks'; Name = 'Wikipedia merge'; Arguments = @('benchmark', '-Suite', 'merge', '-Strat', 'default', '-Dataset', 'wikipedia', '-ReferencePath', $referencePath); RunKind = 'benchmark' })

    if ($parsed.Has('Dry')) {
        Write-Heading 'DataForge release qualification dry run'
        Write-Host "  Wikipedia reference: $referencePath"
        Write-Host ''
        foreach ($command in $commands) {
            $commandText = ConvertTo-QualificationCommandText -Arguments $command.Arguments
            Write-Host ("  [{0}] {1}" -f $command.Group, $commandText)
        }
        Write-Host ''
        return 0
    }

    $runId = New-DataForgeQualificationRunId
    $qualificationDirectory = Join-Path $repoRoot "artifacts/dataforge/qualification/$runId"
    [void][System.IO.Directory]::CreateDirectory($qualificationDirectory)
    $git = Get-ArtifactGitContext -RepoRoot $repoRoot
    $startedAtUtc = [DateTime]::UtcNow
    $steps = [System.Collections.Generic.List[object]]::new()
    $blockers = [System.Collections.Generic.List[string]]::new()
    $syntheticIdentities = @{}
    $referenceIdentity = $null
    $referenceVerified = $false
    $buildPassed = $false

    $summary = [ordered]@{
        schemaVersion = 1
        qualificationId = $runId
        status = 'Running'
        startedAtUtc = $startedAtUtc.ToString('O')
        completedAtUtc = $null
        source = [ordered]@{
            commit = $git.commit
            branch = $git.branch
            dirty = $git.dirty
            os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
            architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            sdkVersion = ((& dotnet --version 2>$null | Select-Object -First 1) -as [string]).Trim()
        }
        synthetic = [ordered]@{
            mode = 'synthetic'
            identities = @()
        }
        wikipedia = [ordered]@{
            referencePath = [System.IO.Path]::GetRelativePath($repoRoot, $referencePath).Replace('\', '/')
            verified = $false
            identity = $null
        }
        commands = @()
        blockers = @()
    }
    $summaryPath = Join-Path $qualificationDirectory 'qualification.json'
    Write-AtomicJsonFile -Path $summaryPath -Value $summary

    Write-Heading 'DataForge release qualification'
    Write-Host "  Run:       $runId"
    Write-Host "  Reference: $referencePath"
    Write-Host "  Commit:    $($git.commit)"
    Write-Host ''

    $buildCommand = $commands[0]
    $buildStep = Invoke-DataForgeQualificationCommand -Command $buildCommand -RepoRoot $repoRoot -QualificationDirectory $qualificationDirectory
    [void]$steps.Add($buildStep)
    $buildPassed = $buildStep.status -eq 'Passed'
    if (-not $buildPassed) {
        [void]$blockers.Add("The solution build did not pass (exit code $($buildStep.exitCode)).")
    }
    Update-DataForgeQualificationSummary -Summary $summary -Steps $steps -Blockers $blockers `
        -SyntheticIdentities $syntheticIdentities -ReferenceIdentity $referenceIdentity `
        -ReferenceVerified $referenceVerified -Path $summaryPath -Status 'Running'

    $referenceCommand = @($commands | Where-Object { $_.Group -eq 'wikipedia-reference' } | Select-Object -First 1).Arguments
    if (Test-Path -LiteralPath (Join-Path $referencePath 'manifest.json') -PathType Leaf) {
        $referenceStep = Invoke-DataForgeQualificationCommand -Command ([pscustomobject]@{
            Group = 'wikipedia-reference'
            Name = 'Verify frozen Wikipedia reference'
            Arguments = $referenceCommand
            RunKind = ''
        }) -RepoRoot $repoRoot -QualificationDirectory $qualificationDirectory
        [void]$steps.Add($referenceStep)
        if ($referenceStep.status -eq 'Passed') {
            try {
                $manifestPath = Join-Path $referencePath 'manifest.json'
                $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                $source = $manifest.source
                $referenceIdentity = [ordered]@{
                    sourceKind = [string]$manifest.sourceKind
                    datasetId = [string]$source.datasetId
                    datasetVersion = [int]$source.datasetVersion
                    recordCount = [int]$manifest.recordCount
                    logicalByteCount = [long]$manifest.logicalByteCount
                    contentSha256 = [string]$manifest.contentSha256
                    artefactSha256 = [string]$manifest.artefactSha256
                    dumpDate = [string]$source.dumpDate
                    primarySha1 = [string]$source.primarySha1
                    indexSha1 = [string]$source.indexSha1
                }
                if ($referenceIdentity.sourceKind -ne 'Imported' -or
                    $referenceIdentity.datasetId -ne 'leancorpus-wikipedia-en' -or
                    $referenceIdentity.datasetVersion -ne 1 -or
                    $referenceIdentity.recordCount -ne 20000 -or
                    $referenceIdentity.dumpDate -ne '20260901') {
                    throw 'The verified artefact manifest does not identify the required 20,000-record leancorpus-wikipedia-en v1 reference.'
                }
                $referenceVerified = $true
            } catch {
                [void]$blockers.Add("Wikipedia reference identity validation failed: $($_.Exception.Message)")
            }
        } else {
            [void]$blockers.Add("Wikipedia reference verification failed (exit code $($referenceStep.exitCode)).")
        }
    } else {
        $referenceStep = [ordered]@{
            group = 'wikipedia-reference'
            name = 'Verify frozen Wikipedia reference'
            command = (ConvertTo-QualificationCommandText -Arguments $referenceCommand)
            status = 'Blocked'
            exitCode = $null
            elapsedSeconds = 0
            runId = $null
            runKind = $null
            artifactPath = [System.IO.Path]::GetRelativePath($repoRoot, $referencePath).Replace('\', '/')
        }
        [void]$steps.Add($referenceStep)
        [void]$blockers.Add("The frozen Wikipedia v1 reference is missing at '$referencePath'; qualify does not download or rebuild it.")
    }
    Update-DataForgeQualificationSummary -Summary $summary -Steps $steps -Blockers $blockers `
        -SyntheticIdentities $syntheticIdentities -ReferenceIdentity $referenceIdentity `
        -ReferenceVerified $referenceVerified -Path $summaryPath -Status 'Running'

    foreach ($command in @($commands | Where-Object { $_.Group -eq 'synthetic-tests' -or $_.Group -eq 'synthetic-benchmarks' })) {
        if (-not $buildPassed) {
            [void]$steps.Add((New-SkippedQualificationStep -Command $command -Reason 'Skipped because the solution build failed.'))
            continue
        }

        $step = Invoke-DataForgeQualificationCommand -Command $command -RepoRoot $repoRoot -QualificationDirectory $qualificationDirectory
        [void]$steps.Add($step)
        if ($step.status -ne 'Passed') {
            [void]$blockers.Add("$($command.Name) did not pass (exit code $($step.exitCode)).")
        }
        if ($step.runKind -eq 'benchmark' -and $step.runId) {
            Add-DataForgeQualificationIdentities -RunDirectory (Join-Path (Get-ArtifactRunsRoot -Kind benchmark -RepoRoot $repoRoot) $step.runId) `
                -RunId $step.runId -IdentityMap $syntheticIdentities
        }
        Update-DataForgeQualificationSummary -Summary $summary -Steps $steps -Blockers $blockers `
            -SyntheticIdentities $syntheticIdentities -ReferenceIdentity $referenceIdentity `
            -ReferenceVerified $referenceVerified -Path $summaryPath -Status 'Running'
    }

    $requiredSyntheticProfiles = @(
        'leancorpus-search',
        'leancorpus-vector',
        'leancorpus-hybrid',
        'rowles-text-multilingual'
    )
    foreach ($profileId in $requiredSyntheticProfiles) {
        if ((@($syntheticIdentities.Values | Where-Object { $_.profileId -eq $profileId })).Count -eq 0) {
            [void]$blockers.Add("No normal benchmark run recorded the '$profileId' synthetic identity.")
        }
    }

    $wikipediaSmokePassed = $true
    foreach ($command in @($commands | Where-Object { $_.Group -eq 'wikipedia-smoke' })) {
        if (-not $buildPassed) {
            [void]$steps.Add((New-SkippedQualificationStep -Command $command -Reason 'Skipped because the solution build failed.'))
            continue
        }
        if (-not $referenceVerified) {
            [void]$steps.Add((New-SkippedQualificationStep -Command $command -Reason 'Skipped because the frozen Wikipedia reference did not verify.'))
            continue
        }

        $step = Invoke-DataForgeQualificationCommand -Command $command -RepoRoot $repoRoot -QualificationDirectory $qualificationDirectory
        if ($step.status -eq 'Passed') {
            $reportError = Get-DataForgeWikipediaReportError -RepoRoot $repoRoot -RunRelativePath $step.artifactPath -ReferenceIdentity $referenceIdentity
            if ($reportError) {
                $step.status = 'Failed'
                $step.error = $reportError
                [void]$blockers.Add("$($command.Name) did not record the verified Wikipedia identity: $reportError")
                $wikipediaSmokePassed = $false
            }
        }
        [void]$steps.Add($step)
        if ($step.status -ne 'Passed') {
            if (-not $step.error) { [void]$blockers.Add("$($command.Name) did not pass (exit code $($step.exitCode)).") }
            $wikipediaSmokePassed = $false
        }
        Update-DataForgeQualificationSummary -Summary $summary -Steps $steps -Blockers $blockers `
            -SyntheticIdentities $syntheticIdentities -ReferenceIdentity $referenceIdentity `
            -ReferenceVerified $referenceVerified -Path $summaryPath -Status 'Running'
    }

    foreach ($command in @($commands | Where-Object { $_.Group -eq 'wikipedia-benchmarks' })) {
        if (-not $buildPassed) {
            [void]$steps.Add((New-SkippedQualificationStep -Command $command -Reason 'Skipped because the solution build failed.'))
            continue
        }
        if (-not $referenceVerified) {
            [void]$steps.Add((New-SkippedQualificationStep -Command $command -Reason 'Skipped because the frozen Wikipedia reference did not verify.'))
            continue
        }
        if (-not $wikipediaSmokePassed) {
            [void]$steps.Add((New-SkippedQualificationStep -Command $command -Reason 'Skipped because one or more Wikipedia fast smoke suites failed.'))
            continue
        }

        $step = Invoke-DataForgeQualificationCommand -Command $command -RepoRoot $repoRoot -QualificationDirectory $qualificationDirectory
        if ($step.status -eq 'Passed') {
            $reportError = Get-DataForgeWikipediaReportError -RepoRoot $repoRoot -RunRelativePath $step.artifactPath -ReferenceIdentity $referenceIdentity
            if ($reportError) {
                $step.status = 'Failed'
                $step.error = $reportError
                [void]$blockers.Add("$($command.Name) did not record the verified Wikipedia identity: $reportError")
            }
        }
        [void]$steps.Add($step)
        if ($step.status -ne 'Passed') {
            if (-not $step.error) { [void]$blockers.Add("$($command.Name) did not pass (exit code $($step.exitCode)).") }
        }
        Update-DataForgeQualificationSummary -Summary $summary -Steps $steps -Blockers $blockers `
            -SyntheticIdentities $syntheticIdentities -ReferenceIdentity $referenceIdentity `
            -ReferenceVerified $referenceVerified -Path $summaryPath -Status 'Running'
    }

    $nonPassingSteps = @($steps | Where-Object { $_.status -ne 'Passed' })
    $status = if ($blockers.Count -eq 0 -and $nonPassingSteps.Count -eq 0) { 'Passed' } else { 'Failed' }
    Update-DataForgeQualificationSummary -Summary $summary -Steps $steps -Blockers $blockers `
        -SyntheticIdentities $syntheticIdentities -ReferenceIdentity $referenceIdentity `
        -ReferenceVerified $referenceVerified -Path $summaryPath -Status $status -Complete

    Write-Host ''
    Write-Host "Qualification summary: $summaryPath"
    if ($status -eq 'Passed') {
        Write-Success 'DataForge qualification passed.'
        return 0
    }

    Write-Failure 'DataForge qualification recorded one or more blockers.'
    foreach ($blocker in $blockers) { Write-Host "  - $blocker" -ForegroundColor Red }
    return 1
}

function New-DataForgeQualificationRunId {
    $os = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
    $entropy = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    return "$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$entropy-$os"
}

function ConvertTo-QualificationCommandText {
    param([string[]]$Arguments)
    if ($Arguments.Count -eq 0) { return './devops' }
    $command = "./devops $($Arguments[0])"
    $remaining = if ($Arguments.Count -gt 1) { @($Arguments | Select-Object -Skip 1) } else { @() }
    return (ConvertTo-CommandLineText -Command $command -Arguments $remaining).Trim()
}

function Invoke-DataForgeQualificationCommand {
    param(
        [Parameter(Mandatory = $true)][object]$Command,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$QualificationDirectory
    )

    $commandText = ConvertTo-QualificationCommandText -Arguments @($Command.Arguments)
    Write-Host "[$($Command.Group)] $($Command.Name)" -ForegroundColor Cyan
    Write-Host "  $commandText" -ForegroundColor DarkGray
    $runRoot = if ($Command.RunKind) { Get-ArtifactRunsRoot -Kind $Command.RunKind -RepoRoot $RepoRoot } else { '' }
    $existingRunIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($runRoot -and (Test-Path -LiteralPath $runRoot -PathType Container)) {
        foreach ($directory in @(Get-ChildItem -LiteralPath $runRoot -Directory -ErrorAction SilentlyContinue)) {
            [void]$existingRunIds.Add($directory.Name)
        }
    }

    $stepStartedAtUtc = [DateTime]::UtcNow
    $exitCode = $null
    $elapsedSeconds = 0.0
    $processError = ''
    try {
        $processArguments = @('-NoProfile', '-File', (Join-Path $RepoRoot 'devops.ps1')) + @($Command.Arguments)
        $processResult = Invoke-ProcessWithLifecycle -FileName 'pwsh' -Arguments $processArguments `
            -WorkingDirectory $RepoRoot -CaptureOutput $false -MirrorOutput $false -ProgressIntervalSeconds 60 `
            -OnProgress {
                param($Process, $Elapsed)
                $elapsedText = ([TimeSpan]$Elapsed).ToString('hh\:mm\:ss')
                Write-Host "  Qualification command still running ($elapsedText elapsed)..." -ForegroundColor DarkGray
            }
        $exitCode = $processResult.ExitCode
        $elapsedSeconds = [Math]::Round($processResult.Elapsed.TotalSeconds, 3)
    } catch {
        $processError = $_.Exception.Message
    }

    $run = $null
    if ($Command.RunKind) {
        $run = Find-DataForgeQualificationRun -Kind $Command.RunKind -RepoRoot $RepoRoot `
            -StartedAtUtc $stepStartedAtUtc -CommandText $commandText -ExistingRunIds $existingRunIds
    }

    $runStatus = if ($null -ne $run) { [string]$run.Manifest.status } else { '' }
    $passed = $exitCode -eq 0 -and (-not $Command.RunKind -or ($null -ne $run -and $runStatus -eq 'Passed'))
    $status = if ($passed) { 'Passed' } else { 'Failed' }
    if ($processError) { Write-Warn "  $processError" }
    if ($Command.RunKind -and $null -eq $run) { Write-Warn '  No new normal run artefact was found for this command.' }
    if (-not $passed) { Write-Host "  Result: $status (exit code $exitCode; run status '$runStatus')" -ForegroundColor Red }

    return [ordered]@{
        group = [string]$Command.Group
        name = [string]$Command.Name
        command = $commandText
        status = $status
        exitCode = $exitCode
        elapsedSeconds = $elapsedSeconds
        runId = if ($null -ne $run) { [string]$run.RunId } else { $null }
        runKind = if ($null -ne $run) { [string]$Command.RunKind } else { $null }
        artifactPath = if ($null -ne $run) { [string]$run.RelativePath } else { $null }
        error = if ($processError) { $processError } else { $null }
    }
}

function Find-DataForgeQualificationRun {
    param(
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][DateTime]$StartedAtUtc,
        [Parameter(Mandatory = $true)][string]$CommandText,
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)][System.Collections.Generic.HashSet[string]]$ExistingRunIds
    )

    $runRoot = Get-ArtifactRunsRoot -Kind $Kind -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) { return $null }
    $matches = [System.Collections.Generic.List[object]]::new()
    foreach ($directory in @(Get-ChildItem -LiteralPath $runRoot -Directory -ErrorAction SilentlyContinue)) {
        if ($ExistingRunIds.Contains($directory.Name)) { continue }
        $manifestPath = Join-Path $directory.FullName 'run.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifestStart = [DateTime]::Parse([string]$manifest.startedAtUtc).ToUniversalTime()
            if ($manifestStart -lt $StartedAtUtc.AddSeconds(-3) -or
                ([string]$manifest.commandLine).Trim() -ne $CommandText.Trim()) { continue }
            [void]$matches.Add([pscustomobject]@{
                RunId = [string]$manifest.runId
                Directory = $directory.FullName
                RelativePath = [System.IO.Path]::GetRelativePath($RepoRoot, $directory.FullName).Replace('\', '/')
                Manifest = $manifest
                StartedAtUtc = $manifestStart
            })
        } catch {
            continue
        }
    }
    return $matches | Sort-Object StartedAtUtc -Descending | Select-Object -First 1
}

function Get-DataForgeWikipediaReportError {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$RunRelativePath,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ReferenceIdentity
    )

    $runDirectory = Join-Path $RepoRoot $RunRelativePath
    $reportPath = Join-Path (Join-Path $runDirectory 'core') 'report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        return "The normal benchmark report is missing at '$RunRelativePath/core/report.json'."
    }

    try {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ([int]$report.schemaVersion -ne 4) {
            return "The benchmark report schema is $($report.schemaVersion); schema v4 is required."
        }

        $datasets = @($report.provenance.datasets)
        if ($datasets.Count -ne 1) {
            return "The benchmark report contains $($datasets.Count) dataset identities; exactly one imported Wikipedia identity is required."
        }

        $identity = $datasets[0]
        if ([string]$identity.sourceKind -ne 'Imported' -or
            [int]$identity.dataForgeVersion -ne 1 -or
            [string]$identity.datasetId -ne [string]$ReferenceIdentity.datasetId -or
            [int]$identity.datasetVersion -ne [int]$ReferenceIdentity.datasetVersion -or
            [int]$identity.recordCount -ne [int]$ReferenceIdentity.recordCount -or
            -not [string]::Equals([string]$identity.contentSha256, [string]$ReferenceIdentity.contentSha256, [StringComparison]::OrdinalIgnoreCase)) {
            return 'The benchmark report dataset identity does not match the verified Wikipedia v1 reference manifest.'
        }
    } catch {
        return "The benchmark report could not be read or validated: $($_.Exception.Message)"
    }

    return $null
}

function Add-DataForgeQualificationIdentities {
    param(
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][hashtable]$IdentityMap
    )

    if (-not (Test-Path -LiteralPath $RunDirectory -PathType Container)) { return }
    foreach ($identityPath in @(Get-ChildItem -LiteralPath $RunDirectory -Recurse -File -Filter '*.json' -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'dataforge' })) {
        try {
            $identity = Get-Content -LiteralPath $identityPath.FullName -Raw | ConvertFrom-Json
            $contentSha = [string]$identity.contentSha256
            if ([string]$identity.sourceKind -ne 'Generated' -or
                [int]$identity.dataForgeVersion -ne 1 -or
                [int]$identity.recordCount -lt 1 -or
                $contentSha -notmatch '^[0-9a-f]{64}$') {
                throw "Synthetic identity sidecar '$($identityPath.FullName)' is invalid."
            }
            $parameterKey = $identity.parameters | ConvertTo-Json -Compress -Depth 8
            $key = '{0}|{1}|{2}|{3}|{4}' -f [string]$identity.profileId, [int]$identity.profileVersion,
                [int]$identity.recordCount, $parameterKey, $contentSha
            if (-not $IdentityMap.ContainsKey($key)) {
                $IdentityMap[$key] = [ordered]@{
                    profileId = [string]$identity.profileId
                    profileVersion = [int]$identity.profileVersion
                    seed = $identity.seed
                    recordCount = [int]$identity.recordCount
                    parameters = $identity.parameters
                    contentSha256 = $contentSha
                    benchmarkRunIds = @($RunId)
                }
            } elseif ($IdentityMap[$key].benchmarkRunIds -notcontains $RunId) {
                $IdentityMap[$key].benchmarkRunIds += $RunId
            }
        } catch {
            Write-Warn "  Could not read DataForge identity from '$($identityPath.FullName)': $($_.Exception.Message)"
        }
    }
}

function New-SkippedQualificationStep {
    param([object]$Command, [string]$Reason)
    return [ordered]@{
        group = [string]$Command.Group
        name = [string]$Command.Name
        command = (ConvertTo-QualificationCommandText -Arguments @($Command.Arguments))
        status = 'Skipped'
        exitCode = $null
        elapsedSeconds = 0
        runId = $null
        runKind = $null
        artifactPath = $null
        error = $Reason
    }
}

function Update-DataForgeQualificationSummary {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Summary,
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[object]]$Steps,
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[string]]$Blockers,
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)][hashtable]$SyntheticIdentities,
        [object]$ReferenceIdentity,
        [Parameter(Mandatory = $true)][bool]$ReferenceVerified,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Status,
        [switch]$Complete
    )

    $Summary.status = $Status
    if ($Complete) { $Summary.completedAtUtc = [DateTime]::UtcNow.ToString('O') }
    $Summary.synthetic.identities = @($SyntheticIdentities.Values | Sort-Object profileId, recordCount, contentSha256)
    $Summary.wikipedia.verified = $ReferenceVerified
    $Summary.wikipedia.identity = $ReferenceIdentity
    $Summary.commands = @($Steps.ToArray())
    $Summary.blockers = @($Blockers.ToArray())
    Write-AtomicJsonFile -Path $Path -Value $Summary
}
