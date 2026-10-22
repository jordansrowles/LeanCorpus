$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot/common/output.ps1"
. "$PSScriptRoot/common/paths.ps1"
. "$PSScriptRoot/common/arguments.ps1"
. "$PSScriptRoot/common/processes.ps1"
. "$PSScriptRoot/common/tools.ps1"
. "$PSScriptRoot/artifacts/paths.ps1"
. "$PSScriptRoot/artifacts/manifests.ps1"
. "$PSScriptRoot/artifacts/runs.ps1"
. "$PSScriptRoot/artifacts/clean.ps1"

$Script:TestSuites = Import-PowerShellDataFile "$PSScriptRoot/config/test-suites.psd1"
$Script:BenchmarkSuites = Import-PowerShellDataFile "$PSScriptRoot/config/benchmark-suites.psd1"
$Script:BenchmarkStrategies = Import-PowerShellDataFile "$PSScriptRoot/config/benchmark-strategies.psd1"

. "$PSScriptRoot/testing/targets.ps1"
. "$PSScriptRoot/testing/events.ps1"
. "$PSScriptRoot/testing/artifacts.ps1"
. "$PSScriptRoot/testing/results.ps1"
. "$PSScriptRoot/testing/preparation.ps1"
. "$PSScriptRoot/testing/execution.ps1"
. "$PSScriptRoot/testing/reports.ps1"
. "$PSScriptRoot/testing/runner.ps1"
. "$PSScriptRoot/testing/backup-restore-matrix.ps1"

. "$PSScriptRoot/diagnostics/tools.ps1"
. "$PSScriptRoot/diagnostics/capture.ps1"

. "$PSScriptRoot/support/coverage.ps1"
. "$PSScriptRoot/support/benchmark.ps1"
. "$PSScriptRoot/support/docfx.ps1"

. "$PSScriptRoot/commands/build.ps1"
. "$PSScriptRoot/commands/test.ps1"
. "$PSScriptRoot/commands/aot.ps1"
. "$PSScriptRoot/commands/coverage.ps1"
. "$PSScriptRoot/commands/diagnostics.ps1"
. "$PSScriptRoot/commands/benchmark.ps1"
. "$PSScriptRoot/commands/data.ps1"
. "$PSScriptRoot/commands/dataforge.ps1"
. "$PSScriptRoot/commands/docs.ps1"
. "$PSScriptRoot/commands/benchmarks.ps1"
. "$PSScriptRoot/commands/setup.ps1"
. "$PSScriptRoot/commands/report.ps1"
. "$PSScriptRoot/commands/server.ps1"
. "$PSScriptRoot/commands/clean.ps1"
. "$PSScriptRoot/commands/pack.ps1"
. "$PSScriptRoot/ui/ui.ps1"

function Invoke-DevOps {
    param([string]$Command, [string[]]$Arguments)

    switch ($Command) {
        'build'      { Invoke-DevOpsBuild -Arguments $Arguments }
        'test'       { Invoke-DevOpsTest -Arguments $Arguments }
        'aot'        { Invoke-DevOpsAot -Arguments $Arguments }
        'coverage'   { Invoke-DevOpsCoverage -Arguments $Arguments }
        'diagnostics'{ Invoke-DevOpsDiagnostics -Arguments $Arguments }
        'benchmark'  { Invoke-DevOpsBenchmark -Arguments $Arguments }
        'data'       { Invoke-DevOpsData -Arguments $Arguments }
        'dataforge'  { Invoke-DevOpsDataForge -Arguments $Arguments }
        'docs'       { Invoke-DevOpsDocs -Arguments $Arguments }
        'benchmarks' { Invoke-DevOpsBenchmarks -Arguments $Arguments }
        'setup'      { Invoke-DevOpsSetup -Arguments $Arguments }
        'report'     { Invoke-DevOpsReport -Arguments $Arguments }
        'server'     { Invoke-DevOpsServer -Arguments $Arguments }
        'clean'      { Invoke-DevOpsClean -Arguments $Arguments }
        'pack'       { Invoke-DevOpsPack -Arguments $Arguments }
        'ui'         { Invoke-DevOpsUi -Arguments $Arguments }
        ''           { Invoke-DevOpsHelp }
        '--help'     { Invoke-DevOpsHelp }
        '-Help'      { Invoke-DevOpsHelp }
        '-h'         { Invoke-DevOpsHelp }
        default      { Invoke-DevOpsHelp }
    }
}

function Invoke-DevOpsHelp {
    Write-Host ''
    Write-Host '  LeanCorpus devops'
    Write-Host '  ================'
    Write-Host '  The root managed apphost handles build, clean, report, coverage, diagnostics and server start.'
    Write-Host '  Run this PowerShell-only surface as: pwsh ./devops.ps1 <command>.'
    Write-Host '  Bootstrap or rebuild the managed host with: pwsh ./setup.ps1.'
    Write-Host ''
    Write-Host '  Commands:'
    Write-Host ''
    Write-Host '    build                Build the solution (Release, net11.0 by default)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Framework          net10.0 or net11.0 for -Project builds (default: net11.0)'
    Write-Host ''
    Write-Host '    test                 Run test suites'
    Write-Host '      -Suite              Comma-separated registered suite keys, all, or affected'
    Write-Host "                          Registered keys: $($Script:TestSuites.Keys -join ', ')"
    Write-Host '      -Commit             Select only files changed by a commit (affected suite only)'
    Write-Host '      -Range              Select only an explicit Git range; exclusive with -Commit'
    Write-Host '      -ExplainAffected     Resolve affected files and targets, then exit without running tests'
    Write-Host '      affected            Default: modified, staged and untracked files only'
    Write-Host '                          Unmapped files and zero targets fail in every mode'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -RuntimeIdentifier  NativeAOT RID for the aot suite (auto-detected if omitted)'
    Write-Host '      -NoRestore           Use existing restored packages and skip restore'
    Write-Host '      -Filter             xUnit filter expression (e.g. FullyQualifiedName~Writer)'
    Write-Host '      -Verbosity          Test output detail: quiet, minimal, normal, detailed'
    Write-Host '      -HangTimeout        Integration test hang timeout (default: 100s; use off to disable)'
    Write-Host '      -Area               Comma-separated test areas'
    Write-Host '      -Category           Comma-separated test categories'
    Write-Host '      -Technique          Comma-separated test techniques: PropertyBased,StateMachine,Metamorphic'
    Write-Host '      -Count              Sequential repetitions (default: 1)'
    Write-Host '      backup-restore-matrix  Fixed-seed five-variant filesystem investigation'
    Write-Host '        -Count            Independent launches per variant (default: 3)'
    Write-Host '        -Iterations       Explicit FsCheck override (normal default: 100)'
    Write-Host '        -TempRoot         Alternate source/backup/restore temporary root'
    Write-Host '        -SourceManifest   Verify exported source against retained manifest'
    Write-Host '      -Flaky              Repeat 30 times by default; use stress parallelism and summary telemetry'
    Write-Host '      -FailFast            Stop scheduling after the first failed target'
    Write-Host '      -Diagnostics        Enable full managed telemetry, MTP logging and bounded raw streams'
    Write-Host '      -CI                 Use CI-prepared managed output and write run artefacts'
    Write-Host '      -CollectCoverage    Collect coverage for registry-eligible suites'
    Write-Host '      -Profile            unit, integration, or stress parallel execution profile'
    Write-Host '      -Explicit           Include explicit tests'
    Write-Host '      -ExplicitOnly       Run only explicit tests'
    Write-Host '      -FailWarnings       Treat xUnit warnings as failures'
    Write-Host '      -Timeout            Outer process timeout, for example 30s or off'
    Write-Host '      -List               List available suites and exit'
    Write-Host ''
    Write-Host '    aot                  Run NativeAOT smoke tests for both frameworks'
    Write-Host '      -RuntimeIdentifier  linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64 or win-arm64 (auto-detected if omitted)'
    Write-Host '      -NoRestore           Use existing restored packages and skip restore'
    Write-Host ''
    Write-Host '    coverage             Run tests with code coverage collection'
    Write-Host '      -Suite              core, sourcegen, or all (default: all)'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Clean              Remove previous coverage results before running'
    Write-Host '      -IncludePerformance  Include tests marked Coverage=Skip'
    Write-Host '      -GenerateReport     Generate HTML coverage report via ReportGenerator'
    Write-Host ''

    Write-Host '    diagnostics          Attach standard .NET diagnostics to a process'
    Write-Host '      ps                  List process IDs and names'
    Write-Host '      counters --pid ID  Monitor counters; use -- for tool arguments'
    Write-Host '      trace --pid ID     Capture a trace under artifacts/diagnostics/runs'
    Write-Host '      gcdump --pid ID    Capture a GC dump under artifacts/diagnostics/runs'
    Write-Host '      dump --pid ID      Capture a dump (default type: Mini)'
    Write-Host '      symbols ARTIFACT   Download symbols for an explicit artifact'
    Write-Host '      capture --pid ID   Capture bounded counters and trace data'
    Write-Host ''
    Write-Host '    benchmark            Run BenchmarkDotNet suites'
    Write-Host '      -Suite              Suite name (default: all). Use -List to see all suites'
    Write-Host '      -Strat              Strategy preset: fast (500 docs, dry), default (20K, short),'
    Write-Host '                          quick-compare (1K, short), intense (10K), stress (50K),'
    Write-Host '                          exhaustive (100K) (default: default)'
    Write-Host '      -DocCount           Override document count for the run'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Dataset            synthetic or wikipedia (default: synthetic)'
    Write-Host '      -ReferencePath      Wikipedia v1 reference directory'
    Write-Host '      -PrepareData        Prepare Gutenberg for an explicitly selected Gutenberg suite'
    Write-Host '      -BookCount          Gutenberg books to fetch with -PrepareData (default: 200)'
    Write-Host '      -CorpusOnly         Skip Lucene.NET comparison; LeanCorpus methods only'
    Write-Host '      -Controlled         Deterministic preset: 1K docs, short job, corpus-only'
    Write-Host '      -Dry                Print the dotnet command without executing'
    Write-Host '      -Area               Comma-separated benchmark areas for core or text'
    Write-Host '      -Group              Comma-separated benchmark groups for core or text'
    Write-Host '      -GcDump             Collect GC heap dumps (requires dotnet-gcdump)'
    Write-Host '      -List               List available suites and strategies and exit'
    Write-Host '      -SourceCommit       Git commit hash for provenance (when .git unavailable)'
    Write-Host '      -SourceRef          Git ref for provenance'
    Write-Host '      -SourceManifest     Path to source manifest for provenance'
    Write-Host '      -- <args>           Arguments passed through to BenchmarkDotNet'
    Write-Host '        benchmark remote  Run benchmarks on a remote host via SSH/tmux'
    Write-Host ''
    Write-Host '    data                 Prepare benchmark data or frozen release fixtures'
    Write-Host '      gutenberg           Project Gutenberg ebooks'
    Write-Host '        -BookCount        Number of books to download (default: 200)'
    Write-Host '      migration-fixtures-311  Regenerate frozen 3.1.1 migration fixtures deliberately'
    Write-Host '        -PackageSource    NuGet source containing LeanCorpus 3.1.1 (default: nuget.org)'
    Write-Host ''
    Write-Host '    dataforge            Deterministic datasets and frozen reference data'
    Write-Host '      profiles            List available profiles and defaults'
    Write-Host '        -Json              Emit profile descriptors as JSON for automation'
    Write-Host '      generate            Generate and materialise a named profile'
    Write-Host '      inspect             Read dataset manifest details'
    Write-Host '      verify              Check materialised bytes and manifest hashes'
    Write-Host '      reproduce           Regenerate and compare dataset identity'
    Write-Host '      reference download  Fetch pinned Wikipedia source files'
    Write-Host '      reference build     Build immutable Wikipedia v1 from the local cache'
    Write-Host '      reference inspect   Read frozen Wikipedia identity and summaries'
    Write-Host '      reference verify    Verify Wikipedia v1 without the source dump'
    Write-Host '      qualify             Run synthetic and Wikipedia release qualification'
    Write-Host '        -ReferencePath    Override the Wikipedia v1 reference directory'
    Write-Host '        -Dry              Print qualification commands without running them'
    Write-Host ''
    Write-Host '    docs                 Build the documentation site'
    Write-Host '      prepare             Stage canonical content, imports and generated sections'
    Write-Host '      build               Full build: API metadata + static site (default)'
    Write-Host '      metadata            API YAML metadata only, no site build'
    Write-Host '      serve               Build and serve on http://0.0.0.0:8080'
    Write-Host '      test                Run ADR/static validation and the docs-site Playwright suite'
    Write-Host '      -SkipBenchmarks     Skip regenerating benchmark pages'
    Write-Host '      -SkipCoverage       Skip regenerating coverage report'
    Write-Host '      DocFX warnings are summarised; full logs are written to artifacts/docs'
    Write-Host ''
    Write-Host '    benchmarks           Benchmark documentation'
    Write-Host '      docs                Generate benchmark result pages from retained results'
    Write-Host ''

    Write-Host '    setup                Verify dev environment and directories'
    Write-Host '      -InstallUi          Install the pinned PwshSpectreConsole UI dependency for the current user'
    Write-Host '    clean [target]       Remove owned artefacts; default preserves packages and benchmark data/runs'
    Write-Host '    pack                 Build NuGet packages under artifacts/package/release'
    Write-Host '      -CompressionOnly   Pack only Core and the three optional codecs'
    Write-Host '      -ValidateCompression Inspect optional packages and run clean-consumer acceptance'
    Write-Host '      -ValidatePackages  Run isolated consumers for every shipping package'
    Write-Host '      -NoBuild           Pack existing builds'
    Write-Host '      -Clean             Remove previous package output before packing'
    Write-Host '    ui                   Open the interactive DevOps workbench (PowerShell 7.4+; run pwsh ./devops.ps1 setup -InstallUi once)'
    Write-Host '    report               Repository health report'
    Write-Host '      git                 Repository/commit-level stats (default: all groups)'
    Write-Host '      files               Per-file facts and history'
    Write-Host '      code                Source-code health'
    Write-Host '      -Top                Entries per list (default: 10)'
    Write-Host '      -Path               Restrict file/code scans to a glob (e.g. src/core/**)'
    Write-Host '      -Json               Emit a single JSON object instead of terminal output'
    Write-Host '      -Strict             Exit non-zero on illegal names, severe god classes, or AOT-hostile patterns'
    Write-Host ''
    Write-Host '    server               Community Server commands'
    Write-Host '      start               Start the local host on .NET 11'
    Write-Host '        -Configuration    Debug or Release (default: Debug)'
    Write-Host '        -NoBuild          Skip the build performed by dotnet run'
    Write-Host '        -NoRestore        Skip restore performed by dotnet run'
    Write-Host '        -External         Listen on all IPv4 interfaces at port 5080'
    Write-Host '        -- <args>         Pass application arguments to the host'
    Write-Host ''
    Write-Host '  Examples:'
    Write-Host '    ./devops build'
    Write-Host '    pwsh ./devops.ps1 test -Suite core -Category Integration -Framework net11.0'
    Write-Host '    pwsh ./devops.ps1 test -Suite core -Category Unit -Verbosity detailed'
    Write-Host '    pwsh ./devops.ps1 test -Suite core,text -Area Index,Search -Technique PropertyBased,Metamorphic'
    Write-Host '    pwsh ./devops.ps1 test affected'
    Write-Host '    pwsh ./devops.ps1 test affected -Commit HEAD'
    Write-Host '    pwsh ./devops.ps1 test affected -Commit caf4bd5'
    Write-Host '    pwsh ./devops.ps1 test affected -Range HEAD~3..HEAD'
    Write-Host '    pwsh ./devops.ps1 test core --count 2 --filter FullyQualifiedName~Writer'
    Write-Host '    pwsh ./devops.ps1 test core --flaky --count 5'
    Write-Host '    pwsh ./devops.ps1 diagnostics ps'
    Write-Host '    pwsh ./devops.ps1 aot'
    Write-Host '    pwsh ./devops.ps1 coverage -Clean -GenerateReport'
    Write-Host '    pwsh ./devops.ps1 benchmark -List'
    Write-Host '    pwsh ./devops.ps1 benchmark -Suite query -Strat fast'
    Write-Host '    pwsh ./devops.ps1 benchmark -Suite mlt -Strat exhaustive -- --filter *SingleSegment*'
    Write-Host '    pwsh ./devops.ps1 docs serve'
    Write-Host '    pwsh ./devops.ps1 docs test'
    Write-Host '    pwsh ./devops.ps1 docs metadata'
    Write-Host '    pwsh ./devops.ps1 docs prepare'
    Write-Host '    pwsh ./devops.ps1 data gutenberg -BookCount 500'
    Write-Host '    pwsh ./devops.ps1 data migration-fixtures-311'
    Write-Host '    pwsh ./devops.ps1 dataforge profiles'
    Write-Host '    pwsh ./devops.ps1 dataforge generate -Profile leancorpus-search -Version 1 -Seed 42 -Count 20000'
    Write-Host '    pwsh ./devops.ps1 report'
    Write-Host '    pwsh ./devops.ps1 report code -Strict'
    Write-Host '    ./devops server start'
    Write-Host '    ./devops server start -External'
    Write-Host ''
    exit 0
}

Export-ModuleMember -Function Invoke-DevOps
