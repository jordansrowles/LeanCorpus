$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsUiBenchmarks {
    $mode = [string](Read-DevOpsUiSelection -Message 'Benchmark discovery' -Choices @('Affected source changes', 'Grouped area and group', 'Named benchmark suite', 'Back') -EnableSearch)
    if (-not $mode -or $mode -eq 'Back') { return }
    $argumentsByProject = [System.Collections.Generic.List[object]]::new()

    if ($mode -eq 'Affected source changes') {
        $framework = [string](Read-DevOpsUiSelection -Message 'Benchmark framework' -Choices @('net10.0', 'net11.0'))
        $arguments = @('benchmark', 'affected', '-Framework', $framework)
        [void]$argumentsByProject.Add([pscustomobject]@{ Arguments = $arguments; Label = 'Affected benchmarks' })
    } else {
        $strategies = Get-DevOpsConfigurationData -ConfigurationName 'benchmark-strategies'
        $strategy = [string](Read-DevOpsUiSelection -Message 'Benchmark strategy' -Choices @($strategies.Keys | Sort-Object) -EnableSearch)
        $framework = [string](Read-DevOpsUiSelection -Message 'Benchmark framework' -Choices @('net10.0', 'net11.0'))
        $benchmarkDotNetText = Read-DevOpsUiText -Message 'Additional BenchmarkDotNet arguments (optional; quote values containing spaces)' -AllowEmpty
        $benchmarkDotNetArguments = @(ConvertFrom-DevOpsUiArgumentText -Text $benchmarkDotNetText)

        if ($mode -eq 'Grouped area and group') {
            $groupRegistry = Get-DevOpsConfigurationData -ConfigurationName 'benchmark-groups'
            $areas = @($groupRegistry.Values | ForEach-Object { [string]$_.Area } | Sort-Object -Unique)
            $selectedAreas = @(Read-DevOpsUiMultiSelection -Message 'Benchmark areas (OR within areas)' -Choices $areas)
            if ($selectedAreas.Count -eq 0) { return }
            $groupChoices = @($groupRegistry.Keys | Where-Object { $groupRegistry[$_].Area -in $selectedAreas } | Sort-Object)
            $selectedGroups = @(Read-DevOpsUiMultiSelection -Message 'Benchmark groups (OR within groups)' -Choices $groupChoices -PageSize 18)
            if ($selectedGroups.Count -eq 0) { return }
            $projects = @($selectedGroups | ForEach-Object { [string]$groupRegistry[$_].Project } | Sort-Object -Unique)
            foreach ($project in $projects) {
                $projectGroups = @($selectedGroups | Where-Object { $groupRegistry[$_].Project -eq $project })
                $projectAreas = @($projectGroups | ForEach-Object { [string]$groupRegistry[$_].Area } | Sort-Object -Unique)
                $arguments = @('benchmark', '-Suite', $project, '-Group', ($projectGroups -join ','), '-Area', ($projectAreas -join ','), '-Strat', $strategy, '-Framework', $framework)
                if ($project -in @('core', 'text')) {
                    $docCount = Read-DevOpsUiText -Message 'Document count override (0 keeps the selected strategy default)' -DefaultAnswer '0'
                    if ($docCount -ne '0') { $arguments += @('-DocCount', $docCount) }
                }
                if ($project -eq 'core' -and (Read-DevOpsUiSwitch -Message 'Run only LeanCorpus methods (skip Lucene.NET comparison)?')) { $arguments += '-CorpusOnly' }
                if ($project -eq 'core' -and (Read-DevOpsUiSwitch -Message 'Collect GC heap dumps?')) { $arguments += '-GcDump' }
                if ($benchmarkDotNetArguments.Count -gt 0) { $arguments += @('--') + $benchmarkDotNetArguments }
                [void]$argumentsByProject.Add([pscustomobject]@{ Arguments = $arguments; Label = "$project grouped benchmarks" })
            }
        } else {
            $suiteConfig = Get-DevOpsConfigurationDocument -ConfigurationName 'benchmark-suites'
            $suiteRegistry = $suiteConfig.data
            $datasetConfig = Get-DevOpsConfigurationDocument -ConfigurationName 'benchmark-datasets'
            $datasetRegistry = $datasetConfig.data
            $defaultDataset = if ($datasetConfig.Contains('defaultDataset') -and $datasetConfig.defaultDataset) { [string]$datasetConfig.defaultDataset } else { 'synthetic' }
            $projectRegistry = Get-DevOpsConfigurationData -ConfigurationName 'benchmark-projects'
            $search = Read-DevOpsUiText -Message 'Narrow benchmark suite names/descriptions' -AllowEmpty
            $suiteChoices = @($suiteRegistry.Keys | Where-Object {
                -not $search -or $_.Contains($search, [StringComparison]::OrdinalIgnoreCase) -or
                    ([string]$suiteRegistry[$_]).Contains($search, [StringComparison]::OrdinalIgnoreCase)
            } | Sort-Object)
            if ($suiteChoices.Count -eq 0) { Write-DevOpsUiMarkup '[yellow]No benchmark suites matched that search.[/]'; return }
            $selectedSuites = @(Read-DevOpsUiMultiSelection -Message 'Select benchmark suites from the narrowed catalogue' -Choices $suiteChoices -PageSize 18)
            if ($selectedSuites.Count -eq 0) { return }
            foreach ($suite in $selectedSuites) {
                $datasets = @($defaultDataset)
                foreach ($datasetName in $datasetRegistry.Keys) {
                    if ($suite -in $datasetRegistry[$datasetName]) { $datasets += $datasetName }
                }
                $dataset = [string](Read-DevOpsUiSelection -Message "Dataset for '$suite'" -Choices $datasets)
                $arguments = @('benchmark', '-Suite', $suite, '-Strat', $strategy, '-Framework', $framework, '-Dataset', $dataset)
                $docCount = Read-DevOpsUiText -Message "Document count override for '$suite' (0 keeps the selected strategy default)" -DefaultAnswer '0'
                if ($docCount -ne '0') { $arguments += @('-DocCount', $docCount) }
                if ($dataset -eq 'wikipedia') {
                    $reference = Read-DevOpsUiText -Message 'Wikipedia reference directory' -DefaultAnswer 'artifacts/dataforge/reference/leancorpus-wikipedia-en-v1'
                    $arguments += @('-ReferencePath', $reference)
                }
                $suiteMetadata = if ($suiteConfig.Contains('suiteMetadata')) { $suiteConfig.suiteMetadata } else { [ordered]@{} }
                $suitePolicy = if ($suiteMetadata.Contains($suite)) { $suiteMetadata[$suite] } else { $null }
                $projectKey = if ($suitePolicy -and $suitePolicy.Contains('Project')) { [string]$suitePolicy.Project } elseif ($projectRegistry.Contains($suite)) { $suite } else { 'core' }
                if ($dataset -eq $defaultDataset -and (($suitePolicy -and $suitePolicy.Contains('RequiresGutenbergData') -and $suitePolicy.RequiresGutenbergData) -or
                        ($projectRegistry[$projectKey].Capabilities -contains 'Gutenberg' -and @($benchmarkDotNetArguments | Where-Object { $_ -match 'GutenbergAnalysisBenchmarks' }).Count -gt 0))) {
                    if (Read-DevOpsUiSwitch -Message 'Prepare Gutenberg benchmark data before running?') {
                        $bookCount = Read-DevOpsUiText -Message 'Gutenberg book count' -DefaultAnswer '200'
                        $arguments += @('-PrepareData', '-BookCount', $bookCount)
                    }
                }
                if ($projectKey -eq 'core') {
                    if (Read-DevOpsUiSwitch -Message 'Run only LeanCorpus methods (skip Lucene.NET comparison)?') { $arguments += '-CorpusOnly' }
                }
                if ($projectKey -eq 'core' -and (Read-DevOpsUiSwitch -Message 'Collect GC heap dumps?')) { $arguments += '-GcDump' }
                if ($benchmarkDotNetArguments.Count -gt 0) { $arguments += @('--') + $benchmarkDotNetArguments }
                [void]$argumentsByProject.Add([pscustomobject]@{ Arguments = $arguments; Label = $suite })
            }
        }
    }

    foreach ($entry in $argumentsByProject) {
        $args = @($entry.Arguments)
        $null = Show-DevOpsUiCommandPreview -Arguments $args
    }
    if (-not (Read-DevOpsUiSwitch -Message 'Execute all benchmark commands shown above?')) { return }
    foreach ($entry in $argumentsByProject) {
        $args = @($entry.Arguments)
        $null = Invoke-DevOpsUiCommand -Arguments $args -Title "Running $($entry.Label)" -Confirmed
    }
}

function Invoke-DevOpsUiBenchmarkRemote {
    $remote = Read-DevOpsUiText -Message 'SSH target' -DefaultAnswer 'jordan@debian'
    $remotePath = Read-DevOpsUiText -Message 'Remote repository path' -DefaultAnswer '/home/jordan/code/leancorpus'
    $branch = Read-DevOpsUiText -Message 'Remote branch' -DefaultAnswer 'main'
    $session = Read-DevOpsUiText -Message 'tmux session name' -DefaultAnswer 'leancorpus-bench'
    $benchmarkArgs = Read-DevOpsUiText -Message 'Arguments passed to the remote benchmark command' -DefaultAnswer '--suite query --strat intense' -AllowEmpty
    $arguments = @('benchmark', 'remote', '-Remote', $remote, '-RemotePath', $remotePath,
        '-Branch', $branch, '-SessionName', $session, '-BenchmarkArgs', $benchmarkArgs)
    $preview = Show-DevOpsUiCommandPreview -Arguments $arguments
    if (-not (Read-DevOpsUiSwitch -Message "The remote script fetches, checks out and fast-forward pulls '$branch' on '$remote'. Continue?")) { return }
    $null = Invoke-DevOpsUiCommand -Arguments $arguments -Title "Starting remote benchmark on $remote" -Confirmed
}
