Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "leancorpus-affected-$([Guid]::NewGuid().ToString('N'))"
$module = Import-Module (Join-Path $PSScriptRoot '../DevOps.psm1') -Force -PassThru
try {
    [void][IO.Directory]::CreateDirectory($temporaryRoot)
    & $module {
        param($Root)
        $script:checks = 0
        function Assert-Selection($Condition, $Message) {
            if (-not $Condition) { throw "Affected selection regression: $Message" }
            $script:checks++
        }
        function Assert-Paths($Actual, $Expected, $Message) {
            Assert-Selection ((@($Actual) -join '|') -ceq (@($Expected) -join '|')) "$Message (actual: $(@($Actual) -join ', '))"
        }
        function Assert-Fails([scriptblock]$Action, $Text) {
            $failure = ''
            try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
            Assert-Selection ($failure.Contains($Text)) "Expected failure containing '$Text', got '$failure'"
        }
        function Invoke-TestGit([string[]]$Arguments) {
            $gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
            $output = & $gitExecutable -C $Root @Arguments 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Test repository Git failed: $output" }
            return ($output -join "`n")
        }
        function Write-File($Name, $Content) {
            $path = Join-Path $Root $Name
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
            [IO.File]::WriteAllText($path, $Content)
        }
        function Commit-Files($Message) {
            Invoke-TestGit @('add', '.') | Out-Null
            Invoke-TestGit @('-c', 'user.name=Selection Test', '-c', 'user.email=selection@example.invalid', 'commit', '-qm', $Message) | Out-Null
            return (Invoke-TestGit @('rev-parse', 'HEAD')).Trim()
        }
        $suites = @{ core = @{ Name = 'Core'; Frameworks = @('net10.0'); DefaultFramework = 'net10.0' } }
        $areas = @{
            source = @{ Globs = @('src/**'); Targets = @('core:Search', 'core:Index', 'core:Search') }
            ignored = @{ Globs = @('.gitignore'); Targets = @() }
        }
        function Intent($Commit = '', $Range = '') {
            Get-AffectedTestIntent -RepoRoot $Root -TestSuites $suites -CodeAreas $areas -Commit $Commit -Range $Range
        }
        Invoke-TestGit @('init', '-q', '-b', 'main') | Out-Null
        Write-File 'src/a.cs' 'root'
        Write-File '.gitignore' '*.ignored'
        $rootCommit = Commit-Files 'Root'
        Assert-Paths (Intent $rootCommit).Selection.paths @('.gitignore', 'src/a.cs') 'Root empty-tree paths'
        Assert-Selection ((Intent $rootCommit).Selection.firstParent -eq '') 'Root has no parent'

        # Isolate each dirty source so another source cannot mask a missing path.
        Write-File 'src/a.cs' 'unstaged only'
        Assert-Paths (Get-DirtyFiles $Root) @('src/a.cs') 'Unstaged-only tracked modification'
        Assert-Paths (Intent).Selection.paths @('src/a.cs') 'Unstaged-only selection paths'
        Write-File 'src/a.cs' 'root'

        Write-File 'src/a.cs' 'staged only'
        Invoke-TestGit @('add', 'src/a.cs') | Out-Null
        Assert-Paths (Get-DirtyFiles $Root) @('src/a.cs') 'Staged-only tracked modification'
        Assert-Paths (Intent).Selection.paths @('src/a.cs') 'Staged-only selection paths'
        Write-File 'src/a.cs' 'root'
        Invoke-TestGit @('add', 'src/a.cs') | Out-Null

        Write-File 'src/b.cs' 'untracked'
        Write-File 'hidden.ignored' 'ignored'
        Assert-Paths (Get-DirtyFiles $Root) @('src/b.cs') 'Untracked non-ignored file only'
        Assert-Paths (Intent).Selection.paths @('src/b.cs') 'Untracked-only selection paths'
        Remove-Item -LiteralPath (Join-Path $Root 'src/b.cs')

        Write-File 'src/a.cs' 'staged'
        Invoke-TestGit @('add', 'src/a.cs') | Out-Null
        Write-File 'src/a.cs' 'unstaged'
        Assert-Paths (Get-DirtyFiles $Root) @('src/a.cs') 'Same-file staged/unstaged de-duplication'
        Assert-Paths (Intent).Selection.paths @('src/a.cs') 'Same-file selection de-duplicates paths'
        Write-File 'src/b.cs' 'untracked'
        Assert-Paths (Get-DirtyFiles $Root) @('src/a.cs', 'src/b.cs') 'Staged/modified/untracked deduplication and ignore'
        $dirtyTargets = (Intent).Selection.targets
        $first = Commit-Files 'First production change'
        Assert-Paths (Get-DirtyFiles $Root) @() 'Clean worktree after production commit'
        Assert-Paths (Intent 'HEAD').Selection.paths @('src/a.cs', 'src/b.cs') 'HEAD clean-worktree regression'
        Assert-Paths (Intent $first.Substring(0, 8)).Selection.paths @('src/a.cs', 'src/b.cs') 'Abbreviated SHA'
        Assert-Selection ((Intent 'HEAD').Selection.resolvedCommit -eq $first) 'Full resolved SHA'
        Assert-Selection ((Intent 'HEAD').Selection.firstParent -eq $rootCommit) 'Resolved first parent'
        Assert-Selection ((Intent 'HEAD').Selection.head -eq $first) 'Current HEAD context'
        Assert-Paths (Intent 'HEAD').Selection.targets $dirtyTargets 'Same mapping for dirty and commit'
        Write-File 'src/c.cs' 'second'
        $second = Commit-Files 'Second production change'
        Assert-Paths (Intent '' 'HEAD~2..HEAD').Selection.paths @('src/a.cs', 'src/b.cs', 'src/c.cs') 'Multi-commit range regression'
        Assert-Paths (Intent '' "$rootCommit..$first").Selection.targets $dirtyTargets 'Same mapping for range'
        Write-File 'src/a.cs' 'unrelated modified'
        Write-File 'src/d.cs' 'unrelated staged'
        Invoke-TestGit @('add', 'src/d.cs') | Out-Null
        Write-File 'src/e.cs' 'unrelated untracked'
        Assert-Paths (Intent $second).Selection.paths @('src/c.cs') 'Commit ignores all dirty sources'
        Assert-Paths (Intent '' "$first..$second").Selection.paths @('src/c.cs') 'Range ignores all dirty sources'
        Assert-Paths (ConvertTo-AffectedPaths @('z\b.cs', 'A/a.cs', 'z/b.cs', 'a/A.cs')) @('A/a.cs', 'z/b.cs') 'Ordinal-ignore-case canonical sorting'
        Assert-Fails { Intent 'no-such-commit' } 'no-such-commit'
        $tree = (Invoke-TestGit @('rev-parse', 'HEAD^{tree}')).Trim()
        Assert-Fails { Intent $tree } $tree
        Assert-Fails { Intent '' 'no-such-base..HEAD' } 'no-such-base..HEAD'
        Assert-Fails { Intent '' 'HEAD' } 'HEAD'
        Assert-Fails { Intent '' 'HEAD..HEAD' } 'no changed files'
        Assert-Fails { Intent 'HEAD' 'HEAD~1..HEAD' } 'mutually exclusive'
        Assert-Fails { Resolve-TestTargets -Suite core -Commit HEAD -RepoRoot $Root } 'affected suite'
        Assert-Fails { Resolve-TestTargets -Suite core -Range 'HEAD~1..HEAD' -RepoRoot $Root } 'affected suite'
        Write-File 'unmapped.txt' 'unmapped'
        Assert-Fails { Intent } 'unmapped.txt'
        # Commit these owned temporary files, then create a genuine two-parent merge.
        $mainBase = Commit-Files 'Temporary dirty inputs'
        Invoke-TestGit @('switch', '-qc', 'topic') | Out-Null
        Write-File 'src/topic.cs' 'topic'
        $topic = Commit-Files 'Topic'
        Invoke-TestGit @('switch', '-q', 'main') | Out-Null
        Write-File 'src/main.cs' 'main'
        $main = Commit-Files 'Main'
        Invoke-TestGit @('-c', 'user.name=Selection Test', '-c', 'user.email=selection@example.invalid', 'merge', '--no-ff', '-qm', 'Merge', 'topic') | Out-Null
        Assert-Paths (Intent 'HEAD').Selection.paths @('src/topic.cs') 'Merge selects first-parent change only'
        Assert-Selection ((Intent 'HEAD').Selection.firstParent -eq $main) 'Merge first-parent provenance'
        Invoke-TestGit @('-c', 'user.name=Selection Test', '-c', 'user.email=selection@example.invalid', 'commit', '--allow-empty', '-qm', 'Empty') | Out-Null
        Assert-Fails { Intent 'HEAD' } 'no changed files'
        Write-File 'unmapped-committed.txt' 'unmapped'
        $unmapped = Commit-Files 'Unmapped'
        Assert-Fails { Intent $unmapped } 'unmapped-committed.txt'
        Assert-Fails { Intent '' 'HEAD~1..HEAD' } 'unmapped-committed.txt'
        $noTargets = @{ source = @{ Globs = @('**'); Targets = @() } }
        Assert-Fails { Get-AffectedTestIntent -RepoRoot $Root -Commit $first -TestSuites $suites -CodeAreas $noTargets } 'zero tests'

        # Exercise the command parser and normal target resolver; intercept only test execution.
        # Production-shaped paths use the real code-area configuration, not the custom mapping above.
        Write-File 'src/core/Rowles.LeanCorpus/Index/Selection.cs' 'production'
        $production = Commit-Files 'Production'
        function Get-RepoRoot { return $Root }
        $script:pipelineCalls = 0
        $script:captured = $null
        function Invoke-TestPipeline {
            param($Targets, $Options, $CommandLine, $DisplayName, $RepoRoot)
            $script:pipelineCalls++
            Assert-Selection ($Options.PassThrough.Count -eq 0) 'Selection options do not reach test runner'
            $script:captured = $Options.AffectedSelection
            $script:capturedTargets = $Targets
            Assert-Selection (@($Targets | Where-Object { $_.Suite -eq 'core' -and 'Index' -in $_.Areas }).Count -gt 0) 'Command resolves Core Index'
            return 0
        }
        Assert-Selection ((Invoke-DevOpsTest @('affected', '-Commit', 'HEAD')) -eq 0) 'Public command clean HEAD selection'
        Assert-Paths $script:captured.paths @('src/core/Rowles.LeanCorpus/Index/Selection.cs') 'Command retains exact selected path'
        Assert-Selection ((Invoke-DevOpsTest @('-Suite', 'affected', '-Range', 'HEAD~1..HEAD')) -eq 0) 'Named suite range selection'
        foreach ($arguments in @(
            @('affected', '-Commit', 'HEAD', '-Range', 'HEAD~1..HEAD'),
            @('core', '-Commit', 'HEAD'), @('core', '-Range', 'HEAD~1..HEAD'),
            @('affected', '-Commit'), @('affected', '-Range'),
            @('affected', '-Commit', 'missing-commit'), @('affected', '-Range', 'broken..range')
        )) {
            $callsBefore = $script:pipelineCalls
            Assert-Selection ((Invoke-DevOpsTest $arguments) -ne 0) 'Invalid CLI must fail'
            Assert-Selection ($script:pipelineCalls -eq $callsBefore) 'Invalid CLI fails before execution'
        }
        Write-File 'src/core/Rowles.LeanCorpus/Index/Selection.cs' 'staged'
        Invoke-TestGit @('add', 'src/core/Rowles.LeanCorpus/Index/Selection.cs') | Out-Null
        Write-File 'src/core/Rowles.LeanCorpus/Index/Selection.cs' 'modified again'
        Write-File 'src/core/Rowles.LeanCorpus/Index/space café.cs' 'untracked'
        Assert-Selection ((Invoke-DevOpsTest @('affected')) -eq 0) 'Public command default dirty selection'
        Assert-Paths $script:captured.paths @('src/core/Rowles.LeanCorpus/Index/Selection.cs', 'src/core/Rowles.LeanCorpus/Index/space café.cs') 'Dirty command includes all sources and Unicode paths'
        $dirtySelection = $script:captured
        Assert-Selection ((Invoke-DevOpsTest @('affected', '-Commit', $production)) -eq 0) 'Public command ignores dirty state in commit mode'
        $commitSelection = $script:captured
        Assert-Selection ((Invoke-DevOpsTest @('affected', '-Range', 'HEAD~1..HEAD')) -eq 0) 'Public command ignores dirty state in range mode'

        # Real manifest pipeline, avoiding SDK discovery only in the environment snapshot.
        function Get-TestEnvironmentSnapshot {
            param($RepoRoot, $CommandLine)
            return @{ gitCommit = $production; gitBranch = 'main'; gitDirty = $false; sdkVersion = 'test' }
        }
        $options = [pscustomobject]@{
            ArtifactsEnabled = $true; RequestedFramework = 'net10.0'; Configuration = 'Release'
            RuntimeIdentifier = ''; Count = 1; Flaky = $false; Diagnostics = $false; FailFast = $false
            AffectedSelection = $script:captured
        }
        foreach ($selection in @($dirtySelection, $commitSelection, $script:captured)) {
        $options.AffectedSelection = $selection
        $context = New-TestRunContext -Options $options -Targets $script:capturedTargets -CommandLine './devops test affected -Range HEAD~1..HEAD' -RepoRoot $Root
        $manifest = Get-Content $context.ManifestPath -Raw | ConvertFrom-Json
        Assert-Selection (($manifest.affectedSelection | ConvertTo-Json -Depth 10 -Compress) -ceq ($selection | ConvertTo-Json -Depth 10 -Compress)) 'Manifest preserves complete selection provenance'
        Complete-ArtifactRun -RunDirectory $context.RunDirectory -Status Passed
        $completed = Get-Content $context.ManifestPath -Raw | ConvertFrom-Json
        Assert-Selection ($completed.affectedSelection.mode -eq $selection.mode) 'Completion retains provenance'
        }
        Write-Host "Affected selection smoke passed: $script:checks assertions."
    } $temporaryRoot
} finally {
    if (Test-Path $temporaryRoot) { Remove-Item $temporaryRoot -Recurse -Force }
}
