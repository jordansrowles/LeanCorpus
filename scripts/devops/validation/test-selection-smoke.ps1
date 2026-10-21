Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Import-Module (Join-Path $PSScriptRoot '../DevOps.psm1') -Force -PassThru
try {
    & $module {
        $script:checks = 0
        function Assert-Selection($Condition, $Message) {
            if (-not $Condition) { throw "Test selection regression: $Message" }
            $script:checks++
        }
        function Assert-Fails([scriptblock]$Action, [string]$Message) {
            $failure = ''
            try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
            Assert-Selection ($failure.Contains($Message)) "Expected '$Message', got '$failure'."
        }

        $filter = Build-TestFilter -Area 'Index,Search' -Category 'Unit,Chaos' `
            -Technique 'PropertyBased,Metamorphic' -Filter 'FullyQualifiedName~Writer'
        Assert-Selection ($filter -ceq '(Area=Index|Area=Search)&(Category=Unit|Category=Chaos)&(Technique=PropertyBased|Technique=Metamorphic)&FullyQualifiedName~Writer') `
            'trait dimensions are ORed internally and ANDed across dimensions'

        $suites = @{
            core = @{ Name = 'Core'; Project = 'core.csproj'; Frameworks = @('net10.0', 'net11.0') }
            text = @{ Name = 'Text'; Project = 'text.csproj'; Frameworks = @('net10.0', 'net11.0') }
        }
        $targets = @(Resolve-TestTargets -Suite @('core', 'text') -Framework 'net10.0' `
            -FrameworkExplicit $true -Area 'Index,Search' -Category 'Unit,Chaos' `
            -Technique 'PropertyBased,Metamorphic' -Filter 'FullyQualifiedName~Writer' -TestSuites $suites)
        Assert-Selection ((@($targets | ForEach-Object { $_.Suite }) -join ',') -ceq 'core,text') `
            'explicit suites produce one ordered target each'
        Assert-Selection (@($targets | Where-Object { $_.Filter -ceq $filter }).Count -eq 2) `
            'the same composed filter reaches every selected suite'
        Assert-Selection ((@($targets[0].Techniques) -join ',') -ceq 'PropertyBased,Metamorphic') `
            'target metadata retains the selected techniques'
        Assert-Fails { Resolve-TestTargets -Suite @('all', 'core') -TestSuites $suites } `
            "cannot be combined with explicit suites"
        Assert-Fails { Resolve-TestTargets -Suite @('affected', 'core') -TestSuites $suites } `
            "cannot be combined with explicit suites"

        $functionalTargets = @(Resolve-TestTargets -Suite @('server-orchard-functional') -Framework 'net11.0' `
            -FrameworkExplicit $true -Filter 'FullyQualifiedName~Shell|FullyQualifiedName~Vector' -TestSuites (Get-TestSuiteRegistry))
        Assert-Selection ($functionalTargets.Count -eq 1) 'functional filtering selects exactly one target'
        Assert-Selection ($functionalTargets[0].Filter -ceq '(FullyQualifiedName~Shell|FullyQualifiedName~Vector)&(FullyQualifiedName!~Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests.ProviderPerformanceTests.)') `
            'functional performance exclusion composes with a caller filter in the same filter language'
        $context = [pscustomobject]@{
            RepoRoot = (Get-RepoRoot)
            ArtifactsEnabled = $false
            Options = [pscustomobject]@{
                Verbosity = 'minimal'; Ci = $false; CollectCoverage = $false
                HangTimeout = 'off'; Flaky = $false; Diagnostics = $false
            }
        }
        $functionalArguments = @(Get-MtpTestArguments -Target $functionalTargets[0] -Context $context -ArtifactDirectory '')
        Assert-Selection ('--filter' -in $functionalArguments -and '--filter-not-class' -notin $functionalArguments) `
            'MTP invocation does not mix VSTest and xUnit simple filters'

        Assert-Selection ($null -eq (Get-Module -Name PwshSpectreConsole)) `
            'loading the normal DevOps module does not import the optional UI dependency'

        $registeredSuites = Get-TestSuiteRegistry
        $allSuiteKeys = @(Get-OrderedTestSuiteKeys -TestSuites $registeredSuites)
        $allListedSuiteKeys = @(Get-OrderedTestSuiteKeys -TestSuites $registeredSuites -IncludeExplicitOnly)
        Assert-Selection ('docs-site' -notin $allSuiteKeys) `
            'the docs browser suite is excluded from all because it requires a freshly built site'
        Assert-Selection ('docs-site' -in $allListedSuiteKeys) `
            'the docs browser suite remains available for explicit runs'
        $docsTargets = @(Resolve-TestTargets -Suite @('docs-site') -Framework 'net10.0' `
            -FrameworkExplicit $true -TestSuites $registeredSuites)
        Assert-Selection ($docsTargets.Count -eq 1 -and $docsTargets[0].Suite -eq 'docs-site') `
            'the docs browser suite resolves explicitly through the test registry'
        Write-Host "Test selection smoke passed ($script:checks checks)."
    }
} finally {
    Remove-Module $module.Name -Force -ErrorAction SilentlyContinue
}
