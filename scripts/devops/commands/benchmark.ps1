$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsBenchmark {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot
    $scriptsPath = Get-ScriptsPath

    # Detect subcommands before full parsing
    $subCmd = if ($Arguments.Count -gt 0 -and -not $Arguments[0].StartsWith('-')) { $Arguments[0] } else { 'run' }
    if ($subCmd -eq 'remote') {
        $remoteScript = Join-Path $scriptsPath 'benchmarks/send-remote.ps1'
        $remoteArgs = @($Arguments | Select-Object -Skip 1)
        if ($remoteArgs.Count -gt 0) { & $remoteScript @remoteArgs } else { & $remoteScript }
        exit $LASTEXITCODE
    }
    if ($subCmd -eq 'affected') {
        $affectedArgs = @($Arguments | Select-Object -Skip 1)
        Invoke-AffectedBenchmarks -Arguments $affectedArgs
        exit $LASTEXITCODE
    }

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $suite = $parsed.Get('Suite', 'all')
    if (-not $parsed.Get('Suite', '') -and $parsed.Positionals.Count -gt 0) { $suite = $parsed.Positionals[0] }
    $strat = $parsed.Get('Strat', 'default')
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $docCount = [int]($parsed.Get('DocCount', '0'))
    $bookCount = [int]($parsed.Get('BookCount', '200'))
    $sourceCommit = $parsed.Get('SourceCommit', '')
    $sourceRef = $parsed.Get('SourceRef', '')
    $sourceManifest = $parsed.Get('SourceManifest', '')
    $prepareData = $parsed.Has('PrepareData')
    $corpusOnly = $parsed.Has('CorpusOnly')
    $list = $parsed.Has('List')
    $dry = $parsed.Has('Dry')
    $gcDump = $parsed.Has('GcDump')
    $controlled = $parsed.Has('Controlled')
    $passThrough = $parsed.PassThrough
    $area = $parsed.Get('Area', '')
    $group = $parsed.Get('Group', '')

    $suiteMap = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-suites.psd1"
    $stratMap = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-strategies.psd1"

    if ($list) {
        Write-Host ''
        Write-Host '  Available benchmark suites (-Suite):'
        Write-Host ''
        Write-Host '    core                LeanCorpus core benchmarks'
        Write-Host '    text                Rowles.Text analysis benchmarks'
        Write-Host '    compression         Compression codec benchmarks'
        foreach ($name in $suiteMap.Keys) {
            Write-Host ("    {0,-22} {1}" -f $name, $suiteMap[$name])
        }
        Write-Host '    affected            Benchmarks for dirty source areas'
        Write-Host ''
        Write-Host '  Available strategies (-Strat):'
        Write-Host ''
        foreach ($name in $stratMap.Keys) {
            Write-Host ("    {0,-16} {1}" -f $name, $stratMap[$name].Description)
        }
        Write-Host ''
        exit 0
    }

    if ($area -or $group) {
        if ($suite -notin @('core', 'text')) {
            Write-Error '-Area and -Group require the core or text benchmark suite.'
            exit 1
        }

        Invoke-SelectedBenchmarks -Suite $suite -Area $area -Group $group -Framework $framework -Dry $dry
        exit $LASTEXITCODE
    }

    $projectKeys = if ($suite -eq 'all') { @('core', 'text', 'compression') } elseif ($suite -in @('core', 'text', 'compression')) { @($suite) } else { @('core') }

    $stratCfg = Resolve-BenchmarkStrategy $strat
    $stratDocCount = $stratCfg.DocCount
    $stratJobArgs = $stratCfg.Job

    if ($controlled) {
        if ($docCount -le 0 -and $stratDocCount -le 0) { $stratDocCount = 1000 }
        if ($stratJobArgs.Count -eq 0) { $stratJobArgs = @('--job', 'short') }
        $standaloneSuites = @(
            'codec-frame',
            'codec-frame-read',
            'codec-migration',
            'compound-file',
            'windows-filesystem',
            'windows-storage'
        )
        if ($suite -notin $standaloneSuites) { $corpusOnly = $true }
    }

    $effectiveDocCount = 0
    if ($docCount -gt 0) { $effectiveDocCount = $docCount }
    elseif ($stratDocCount -gt 0) { $effectiveDocCount = $stratDocCount }

    if ($prepareData) {
        Prepare-BenchmarkData -RepoRoot $repoRoot -ScriptsPath $scriptsPath -BookCount $bookCount
    }

    if ($effectiveDocCount -gt 0) {
        $env:BENCH_DOC_COUNT = $effectiveDocCount.ToString()
    }
    if ($sourceCommit)   { $env:BENCH_SOURCE_COMMIT   = $sourceCommit }
    if ($sourceRef)      { $env:BENCH_SOURCE_REF      = $sourceRef }
    if ($sourceManifest) { $env:BENCH_SOURCE_MANIFEST = [System.IO.Path]::GetFullPath($sourceManifest) }

    Write-Host "Suite:      $suite"
    Write-Host "Strat:      $strat"
    Write-Host "Framework:  $framework"
    if ($controlled)     { Write-Host 'Mode:       controlled' }
    if ($corpusOnly)     { Write-Host 'CorpusOnly: enabled' }
    if ($effectiveDocCount -gt 0) { Write-Host "Docs:       $effectiveDocCount" }
    if ($stratJobArgs)   { Write-Host "Job:        $($stratJobArgs -join ' ')" }
    if ($passThrough)    { Write-Host "BDN args:   $($passThrough -join ' ')" }
    Write-Host "Projects:   $($projectKeys -join ', ')"

    if ($dry) {
        Write-Host ''
        Write-Host 'Dry run - commands that would execute:'
        foreach ($projectKey in $projectKeys) {
            $projectPath = Resolve-BenchmarkProjectPath $projectKey
            $runArgs = if ($projectKey -eq 'core') { @('--suite', $(if ($suite -in @('all', 'core')) { 'all' } else { $suite })) } else { @() }
            if ($effectiveDocCount -gt 0 -and $projectKey -eq 'core') { $runArgs += @('--doccount', $effectiveDocCount.ToString()) }
            if ($corpusOnly -and $projectKey -eq 'core') { $runArgs += '--corpus-only' }
            Write-Host "  dotnet run -c Release --framework $framework --project `"$projectPath`" -- $($runArgs -join ' ') $($stratJobArgs -join ' ') $($passThrough -join ' ')"
        }
        Write-Host ''
        exit 0
    }

    if ($gcDump) {
        Assert-DotNetTool 'dotnet-gcdump'
    }

    Write-Host ''
    $commandLine = ConvertTo-CommandLineText -Command './devops benchmark' -Arguments $Arguments
    $benchmarkRun = New-ArtifactRun -Kind benchmark -Framework $framework -Configuration Release `
        -Target $suite -CommandLine $commandLine -RepoRoot $repoRoot
    $projectResults = [System.Collections.Generic.List[object]]::new()
    $runCompleted = $false
    $runFailure = $null
    try {
        foreach ($projectKey in $projectKeys) {
            $projectPath = Resolve-BenchmarkProjectPath $projectKey
            $projectDirectory = Join-Path $benchmarkRun.RunDirectory $projectKey
            [void][System.IO.Directory]::CreateDirectory($projectDirectory)
            $runArgs = if ($projectKey -eq 'core') { @('--suite', $(if ($suite -in @('all', 'core')) { 'all' } else { $suite })) } else { @() }
            if ($effectiveDocCount -gt 0 -and $projectKey -eq 'core') { $runArgs += @('--doccount', $effectiveDocCount.ToString()) }
            if ($corpusOnly -and $projectKey -eq 'core') { $runArgs += '--corpus-only' }
            if ($gcDump -and $projectKey -eq 'core') { $runArgs += '--gcdump' }

            Set-ArtifactProcessEnvironment -RunId $benchmarkRun.RunId -Kind benchmark `
                -ArtifactDirectory $projectDirectory -Target $projectKey
            Write-Info "Running benchmark project: $projectKey"
            dotnet run -c Release --framework $framework --project $projectPath -- @runArgs @stratJobArgs @passThrough
            $exitCode = $LASTEXITCODE
            [void]$projectResults.Add([ordered]@{
                project = $projectKey
                status = if ($exitCode -eq 0) { 'Passed' } else { 'Failed' }
                exitCode = $exitCode
                path = $projectKey
            })
        }
        $failedProjects = @($projectResults | Where-Object { $_.status -ne 'Passed' })
        $report = [ordered]@{
            schemaVersion = 1
            runId = $benchmarkRun.RunId
            status = if ($failedProjects.Count -eq 0) { 'Passed' } else { 'Failed' }
            framework = $framework
            strategy = $strat
            comparable = $true
            projects = @($projectResults)
        }
        Write-AtomicJsonFile -Path (Join-Path $benchmarkRun.RunDirectory 'run-report.json') -Value $report
        $markdown = @(
            "# Benchmark run $($benchmarkRun.RunId)",
            '',
            "Status: $($report.status)",
            '',
            '| Project | Status | Path |',
            '| --- | --- | --- |'
        ) + @($projectResults | ForEach-Object { "| $($_.project) | $($_.status) | `$($_.path)/` |" })
        Write-AtomicTextFile -Path (Join-Path $benchmarkRun.RunDirectory 'report.md') -Content ($markdown -join [Environment]::NewLine)
        Complete-ArtifactRun -RunDirectory $benchmarkRun.RunDirectory -Status $report.status `
            -AdditionalValues @{ projects = @($projectResults); report = 'run-report.json'; comparable = $true }
        $runCompleted = $true
    } catch {
        $runFailure = $_.Exception
        try {
            Complete-ArtifactRun -RunDirectory $benchmarkRun.RunDirectory -Status Failed `
                -AdditionalValues @{ error = $runFailure.Message }
            $runCompleted = $true
        } catch {
            Write-Warn "Benchmark run could not be finalised as Failed: $($_.Exception.Message)"
        }
        Write-Failure "Benchmark command failed: $($runFailure.Message)"
    } finally {
        Clear-ArtifactProcessEnvironment
        if (-not $runCompleted) {
            try {
                Complete-ArtifactRun -RunDirectory $benchmarkRun.RunDirectory -Status Incomplete `
                    -AdditionalValues @{ error = 'Benchmark command did not complete normally.' }
                $runCompleted = $true
            } catch {
                Write-Warn "Benchmark run remained unfinalised: $($_.Exception.Message)"
            }
        }
    }

    if ($null -ne $runFailure) {
        exit 1
    }

    Write-Host "Benchmark run: $($benchmarkRun.RunDirectory)"
    exit $(if ($failedProjects.Count -eq 0) { 0 } else { 1 })
}

function Resolve-BenchmarkProjectPath {
    param([string]$Suite)

    switch ($Suite) {
        'text'        { return Join-Path (Get-RepoRoot) 'src/devops/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj' }
        'compression' { return Join-Path (Get-RepoRoot) 'src/devops/Rowles.LeanCorpus.Benchmarks.Compression/Rowles.LeanCorpus.Benchmarks.Compression.csproj' }
        default       { return Get-BenchmarkProjectPath }
    }
}

function Invoke-AffectedBenchmarks {
    param([string[]]$Arguments)

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $area = $parsed.Get('Area', '')
    $group = $parsed.Get('Group', '')
    $dry = $parsed.Has('Dry')
    $repoRoot = Get-RepoRoot
    $groups = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-groups.psd1"

    $dirty = @(Get-DirtyFiles $repoRoot)
    Write-Heading 'Affected benchmark runner'
    Write-Host "  Dirty files:   $($dirty.Count)"
    Write-Host ''

    # project key -> ordered set of benchmark class names
    $targets = @{}
    foreach ($file in $dirty) {
        $normalised = $file.Replace('\', '/')
        foreach ($name in $groups.Keys) {
            $entry = $groups[$name]
            if (-not (Test-BenchmarkGroupSelection -Name $name -Entry $entry -Area $area -Group $group)) {
                continue
            }
            foreach ($glob in $entry.Globs) {
                if (Test-GlobMatch -Path $normalised -Glob $glob) {
                    if (-not $targets.ContainsKey($entry.Project)) {
                        $targets[$entry.Project] = [System.Collections.Generic.HashSet[string]]::new()
                    }
                    foreach ($b in $entry.Benchmarks) {
                        [void]$targets[$entry.Project].Add($b)
                    }
                }
            }
        }
    }

    if ($targets.Count -eq 0) {
        Write-Failure 'No benchmark group matched the dirty files. Refusing to run zero benchmarks.'
        exit 1
    }

    Invoke-BenchmarkTargets -Targets $targets -Framework $framework -Dry $dry
}

function Invoke-SelectedBenchmarks {
    param(
        [string]$Suite,
        [string]$Area,
        [string]$Group,
        [string]$Framework,
        [bool]$Dry
    )

    $groups = Import-PowerShellDataFile "$PSScriptRoot/../config/benchmark-groups.psd1"
    $targets = @{}
    foreach ($name in $groups.Keys) {
        $entry = $groups[$name]
        if ($entry.Project -ne $Suite -or -not (Test-BenchmarkGroupSelection -Name $name -Entry $entry -Area $Area -Group $Group)) {
            continue
        }

        if (-not $targets.ContainsKey($entry.Project)) {
            $targets[$entry.Project] = [System.Collections.Generic.HashSet[string]]::new()
        }
        foreach ($benchmark in $entry.Benchmarks) {
            [void]$targets[$entry.Project].Add($benchmark)
        }
    }

    if ($targets.Count -eq 0) {
        Write-Error "No benchmark groups matched suite '$Suite', Area='$Area', Group='$Group'."
        exit 1
    }

    Write-Heading 'Selected benchmark runner'
    if ($Area) { Write-Host "  Area:          $Area" }
    if ($Group) { Write-Host "  Group:         $Group" }
    Write-Host ''
    Invoke-BenchmarkTargets -Targets $targets -Framework $Framework -Dry $Dry
}

function Test-BenchmarkGroupSelection {
    param(
        [string]$Name,
        $Entry,
        [string]$Area,
        [string]$Group
    )

    $areas = @($Area -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $groups = @($Group -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

    return ($areas.Count -eq 0 -or $areas -contains $Entry.Area) -and
           ($groups.Count -eq 0 -or $groups -contains $Name)
}

function Invoke-BenchmarkTargets {
    param(
        [hashtable]$Targets,
        [string]$Framework,
        [bool]$Dry
    )

    $failed = @()
    foreach ($project in $Targets.Keys | Sort-Object) {
        $classes = @($Targets[$project]) | Sort-Object
        $projectPath = Resolve-BenchmarkProjectPath $project

        foreach ($class in $classes) {
            # BDN --filter is a single glob (no OR), so run one class per call.
            $projectArgs = @()
            if ($project -eq 'core') { $projectArgs = @('--suite', 'all') }

            Write-Info "  $project/$class..."
            if ($Dry) {
                $filterArgs = if ($project -eq 'core') { "--suite all -- --filter `"*$class*`"" } else { "--filter `"*$class*`"" }
                Write-Host "    dotnet run -c Release --framework $Framework --project `"$projectPath`" -- $filterArgs"
                continue
            }
            if ($project -eq 'core') {
                dotnet run -c Release --framework $framework --project $projectPath -- @projectArgs -- --filter "*$class*"
            } else {
                dotnet run -c Release --framework $framework --project $projectPath -- --filter "*$class*"
            }
            if ($LASTEXITCODE -ne 0) {
                Write-Failure "  $project/$class - FAILED"
                $failed += "$project/$class"
            } else {
                Write-Success "  $project/$class - passed"
            }
        }
    }

    Write-Host ''
    if ($failed.Count -gt 0) {
        Write-Error "Failed benchmark projects: $($failed -join ', ')"
        exit 1
    }
    Write-Success 'All selected benchmark targets passed.'
    exit 0
}
