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
