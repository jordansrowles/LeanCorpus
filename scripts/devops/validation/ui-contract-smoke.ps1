Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Import-Module (Join-Path $PSScriptRoot '../DevOps.psm1') -Force -PassThru
$eventPath = [IO.Path]::GetTempFileName()
try {
    & $module {
        param([string]$eventPath)
        $script:checks = 0
        function Assert-UiContract($Condition, $Message) {
            if (-not $Condition) { throw "DevOps UI contract regression: $Message" }
            $script:checks++
        }

        $registry = Get-TestSuiteRegistry
        $grouped = @(Get-DevOpsUiTestSuiteChoices)
        $groupedKeys = @($grouped | ForEach-Object { $_.Choices } | ForEach-Object { $_.Key } | Sort-Object)
        Assert-UiContract (($groupedKeys -join ',') -ceq (@($registry.Keys | Sort-Object) -join ',')) `
            'grouped selection covers each registered suite exactly once'

        $areas = @(Read-DevOpsUiEnumValues -EnumName TestArea)
        $categories = @(Read-DevOpsUiEnumValues -EnumName TestCategory)
        $techniques = @(Read-DevOpsUiEnumValues -EnumName TestTechnique)
        Assert-UiContract ($areas -contains 'TextIntegration' -and $areas -contains 'Search') 'areas are discovered from the owning enum'
        Assert-UiContract (($categories -join ',') -ceq 'Unit,Integration,Chaos') 'categories reflect the test metadata enum'
        Assert-UiContract (($techniques -join ',') -ceq 'PropertyBased,StateMachine,Metamorphic') 'techniques reflect the test metadata enum'

        $frameworks = @(Get-DevOpsUiFrameworkChoices -SuiteKeys @('core', 'server-core'))
        Assert-UiContract (($frameworks -join ',') -ceq 'net10.0,net11.0') 'framework selection uses the selected suites common targets'

        $preview = New-DevOpsUiCommandPreview -Arguments @('test', '-Suite', 'core,text', '-Filter', 'FullyQualifiedName~Writer')
        Assert-UiContract ($preview -like 'pwsh ./devops.ps1 test *-Suite core,text*') 'preview routes the legacy test command through the PowerShell dispatcher'
        $managedPreview = New-DevOpsUiCommandPreview -Arguments @('build', '-Configuration', 'Release')
        Assert-UiContract ($managedPreview -ceq './devops build -Configuration Release') 'preview routes managed build directly through the root apphost'

        $tokens = @(ConvertFrom-DevOpsUiArgumentText -Text '--filter "*Single Segment*" --job short')
        Assert-UiContract (($tokens -join '|') -ceq '--filter|*Single Segment*|--job|short') 'quoted pass-through values remain a single argument'
        $datasets = Import-PowerShellDataFile (Join-Path (Get-RepoRoot) 'scripts/devops/config/benchmark-datasets.psd1')
        $benchmarkScript = Get-Content -LiteralPath (Join-Path (Get-RepoRoot) 'scripts/devops/commands/benchmark.ps1') -Raw
        Assert-UiContract ($datasets.wikipedia.Count -eq 17 -and $benchmarkScript -match 'benchmark-datasets\.psd1') `
            'benchmark dataset compatibility is shared by the UI and command'
        $downloadArguments = @(Get-GutenbergDownloadArguments -BookCount 321)
        Assert-UiContract (($downloadArguments -join '|') -ceq '-BookCount|321') `
            'Gutenberg data preparation forwards a validated book count to the downloader'
        $invalidBookCountRejected = $false
        try { $null = Get-GutenbergDownloadArguments -BookCount 0 } catch { $invalidBookCountRejected = $true }
        Assert-UiContract $invalidBookCountRejected 'Gutenberg download arguments reject non-positive book counts'

        function Read-DevOpsUiSwitch { param([string]$Message) return $false }
        function Read-SpectreSelection {
            param([string]$Message, [string[]]$Choices)
            return 'Exclude explicit tests'
        }
        $testOptions = [pscustomobject]@{
            Suites = @('core', 'text'); Framework = 'net10.0'; Configuration = 'Release'
            Commit = ''; Range = ''; Areas = @('Index'); Categories = @('Unit')
            Techniques = @('PropertyBased'); Filter = ''; Count = '1'; Profile = 'unit'
            HangTimeout = '100s'; Timeout = 'off'; Verbosity = 'minimal'; Flaky = $false
            RuntimeIdentifier = ''; Flags = @(); ExplicitMode = 'Exclude explicit tests'
        }
        $testArguments = New-DevOpsUiTestArguments -Options $testOptions
        $testPreview = New-DevOpsUiCommandPreview -Arguments @($testArguments)
        Assert-UiContract ($testPreview -match '-Suite core,text' -and $testPreview -match '-Technique PropertyBased') `
            'test option construction produces a previewable multi-suite command with technique filtering'

        Write-DevOpsUiTestEvent -Path $eventPath -Name 'TargetDiscovered' -Data @{ target = 'core/Index' }
        $event = Get-Content -LiteralPath $eventPath -Raw | ConvertFrom-Json -Depth 8
        Assert-UiContract ($event.schemaVersion -eq 1 -and $event.name -eq 'TargetDiscovered' -and $event.data.target -eq 'core/Index') `
            'UI test events are versioned structured JSON lines'

        $runFixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('LeanCorpus UI run evidence ' + [Guid]::NewGuid().ToString('N'))
        try {
            $managedDir = Join-Path $runFixtureRoot 'artifacts/test/runs/managed'
            $legacyDir = Join-Path $runFixtureRoot 'artifacts/test/runs/legacy'
            $historicalDir = Join-Path $runFixtureRoot 'artifacts/test/runs/historical'
            foreach ($directory in @($managedDir, $legacyDir, $historicalDir)) { [void][IO.Directory]::CreateDirectory($directory) }
            $managedJson = '{"schemaVersion":1,"runId":"managed","command":"test","state":"failed","startedUtc":"2026-10-21T01:00:00Z","completedUtc":"2026-10-21T01:00:02Z","sourceCommit":"abc","sourceBranch":"issue-97","sourceDirty":true,"platform":"Linux x64","inputs":{"targets":"core/net10.0"},"artifacts":[],"toolVersions":{}}'
            $legacyJson = '{"schemaVersion":1,"runId":"legacy","status":"Passed","startedAtUtc":"2026-10-20T01:00:00Z","target":"core","framework":"net10.0"}'
            Set-Content -LiteralPath (Join-Path $managedDir 'run.json') -Value $managedJson
            Set-Content -LiteralPath (Join-Path $managedDir 'manifest.json') -Value $legacyJson
            Set-Content -LiteralPath (Join-Path $legacyDir 'run.json') -Value $legacyJson
            Set-Content -LiteralPath (Join-Path $historicalDir 'manifest.json') -Value ($managedJson.Replace('"managed"', '"historical"'))
            $recent = @(Get-DevOpsUiRecentRuns -RepoRoot $runFixtureRoot)
            Assert-UiContract ($recent.Count -eq 3) 'run discovery includes canonical managed, legacy and historical managed evidence'
            $managedRun = @($recent | Where-Object RunId -eq 'managed')[0]
            Assert-UiContract ($managedRun.Status -eq 'failed' -and $managedRun.Target -eq 'core/net10.0') 'canonical managed state takes precedence over stale historical evidence'
            Assert-UiContract ($managedRun.Manifest.commit -eq 'abc' -and $managedRun.Manifest.dirty -and $managedRun.Manifest.durationMs -eq 2000) 'managed provenance and duration are projected for the existing UI'
            $reportJson = '{"schemaVersion":1,"succeeded":false,"requestedExecutions":3,"attempts":[{"target":"core/net10.0","suite":"core","iteration":1,"outcome":"Passed","durationMs":2,"tests":[]},{"target":"core/net10.0","suite":"core","iteration":2,"outcome":"Incomplete","durationMs":3,"tests":[]}],"tests":[{"suite":"core","testName":"case","stability":"intermittentFailure","attempts":[{"outcome":"Failed"},{"outcome":"Passed"}]}]}'
            $view = ConvertTo-DevOpsUiTestReport ($reportJson | ConvertFrom-Json)
            Assert-UiContract (-not $view.summary.succeeded -and $view.summary.passedTargetExecutions -eq 1 -and $view.summary.requestedTargetExecutions -eq 3 -and $view.summary.incompleteTargetExecutions -eq 1 -and $view.summary.unscheduledTargetExecutions -eq 1) 'missing or incomplete target executions are counted explicitly and are not displayed as passed'
            Assert-UiContract ($view.tests[0].failedCount -eq 1 -and $view.tests[0].classification -eq 'intermittentFailure') 'managed per-attempt failures remain visible in the existing UI'
            $legacyReport = '{"schemaVersion":1,"summary":{"succeeded":true},"targetResults":[],"tests":[]}' | ConvertFrom-Json
            Assert-UiContract ((ConvertTo-DevOpsUiTestReport $legacyReport).summary.succeeded) 'legacy report projection retains the existing shape'
            Set-Content -LiteralPath (Join-Path $managedDir 'run.json') -Value '{invalid'
            Assert-UiContract (@(Get-DevOpsUiRecentRuns -RepoRoot $runFixtureRoot).Count -eq 2) 'corrupt canonical evidence never falls back to stale historical success'
        } finally {
            if (Test-Path -LiteralPath $runFixtureRoot) { Remove-Item -LiteralPath $runFixtureRoot -Recurse -Force }
        }

        $childResult = Invoke-DevOpsUiProcess -Arguments @('--help')
        Assert-UiContract ($childResult.ExitCode -eq 0 -and (@($childResult.StdOut) -join "`n") -match 'LeanCorpus devops') `
            'the command runner preserves child arguments and returns its exit code'
        $uiFallback = Invoke-DevOpsUiProcess -Arguments @('ui')
        Assert-UiContract ($uiFallback.ExitCode -eq 2 -and (@($uiFallback.StdOut) -join "`n") -match 'interactive terminal') `
            'a non-interactive UI launch gives the terminal fallback before loading the optional dependency'
        Assert-UiContract ($null -eq (Get-Module -Name PwshSpectreConsole)) `
            'loading and using non-interactive DevOps helpers does not import the optional UI dependency'
        Write-Host "DevOps UI contract smoke passed ($script:checks checks)."
    } $eventPath
} finally {
    Remove-Item -LiteralPath $eventPath -Force -ErrorAction SilentlyContinue
    Remove-Module $module.Name -Force -ErrorAction SilentlyContinue
}
