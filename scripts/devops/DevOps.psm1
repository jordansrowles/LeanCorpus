$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot/common/output.ps1"
. "$PSScriptRoot/common/paths.ps1"
. "$PSScriptRoot/common/arguments.ps1"
. "$PSScriptRoot/common/processes.ps1"
. "$PSScriptRoot/common/tools.ps1"

$Script:TestSuites = Import-PowerShellDataFile "$PSScriptRoot/config/test-suites.psd1"
$Script:BenchmarkSuites = Import-PowerShellDataFile "$PSScriptRoot/config/benchmark-suites.psd1"
$Script:BenchmarkStrategies = Import-PowerShellDataFile "$PSScriptRoot/config/benchmark-strategies.psd1"

. "$PSScriptRoot/support/coverage.ps1"
. "$PSScriptRoot/support/benchmark.ps1"
. "$PSScriptRoot/support/docfx.ps1"

. "$PSScriptRoot/commands/build.ps1"
. "$PSScriptRoot/commands/test.ps1"
. "$PSScriptRoot/commands/aot.ps1"
. "$PSScriptRoot/commands/coverage.ps1"
. "$PSScriptRoot/commands/benchmark.ps1"
. "$PSScriptRoot/commands/data.ps1"
. "$PSScriptRoot/commands/docs.ps1"
. "$PSScriptRoot/commands/benchmarks.ps1"
. "$PSScriptRoot/commands/setup.ps1"
. "$PSScriptRoot/commands/report.ps1"

function Invoke-DevOps {
    param([string]$Command, [string[]]$Arguments)

    switch ($Command) {
        'build'      { Invoke-DevOpsBuild -Arguments $Arguments }
        'test'       { Invoke-DevOpsTest -Arguments $Arguments }
        'aot'        { Invoke-DevOpsAot -Arguments $Arguments }
        'coverage'   { Invoke-DevOpsCoverage -Arguments $Arguments }
        'benchmark'  { Invoke-DevOpsBenchmark -Arguments $Arguments }
        'data'       { Invoke-DevOpsData -Arguments $Arguments }
        'docs'       { Invoke-DevOpsDocs -Arguments $Arguments }
        'benchmarks' { Invoke-DevOpsBenchmarks -Arguments $Arguments }
        'setup'      { Invoke-DevOpsSetup -Arguments $Arguments }
        'report'     { Invoke-DevOpsReport -Arguments $Arguments }
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
    Write-Host ''
    Write-Host '  Commands:'
    Write-Host ''
    Write-Host '    build                Build the solution (Release, net10.0 by default)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host ''
    Write-Host '    test                 Run test suites'
    Write-Host '      -Suite              core, text, sourcegen, architecture, aot, affected,'
    Write-Host '                          or all (default: all)'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -RuntimeIdentifier  NativeAOT RID for the aot suite (auto-detected if omitted)'
    Write-Host '      -Filter             xUnit filter expression (e.g. FullyQualifiedName~Writer)'
    Write-Host '      -Verbosity          Test output detail: quiet, minimal, normal, detailed'
    Write-Host '      -HangTimeout        Integration test hang timeout (default: 100s; use off to disable)'
    Write-Host '      -Area               Comma-separated test areas'
    Write-Host '      -Category           Comma-separated test categories'
    Write-Host '      -List               List available suites and exit'
    Write-Host ''
    Write-Host '    aot                  Run NativeAOT smoke tests for both frameworks'
    Write-Host '      -RuntimeIdentifier  linux-x64, osx-x64, win-x64 (auto-detected if omitted)'
    Write-Host ''
    Write-Host '    coverage             Run tests with code coverage collection'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Clean              Remove previous coverage results before running'
    Write-Host '      -IncludePerformance  Include tests marked Coverage=Skip'
    Write-Host '      -GenerateReport     Generate HTML coverage report via ReportGenerator'
    Write-Host ''
    Write-Host '    benchmark            Run BenchmarkDotNet suites'
    Write-Host '      -Suite              Suite name (default: all). Use -List to see all suites'
    Write-Host '      -Strat              Strategy preset: fast (500 docs, dry), default (20K, short),'
    Write-Host '                          quick-compare (1K, short), intense (10K), stress (50K),'
    Write-Host '                          exhaustive (100K) (default: default)'
    Write-Host '      -DocCount           Override document count for the run'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -PrepareData        Download benchmark data if not already present'
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
    Write-Host '    data                 Download benchmark datasets'
    Write-Host '      gutenberg           Project Gutenberg ebooks'
    Write-Host '        -BookCount        Number of books to download (default: 200)'
    Write-Host '      news                20 Newsgroups dataset'
    Write-Host '      wikipedia           Wikipedia article dump'
    Write-Host ''
    Write-Host '    docs                 Build the documentation site'
    Write-Host '      build               Full build: API metadata + static site (default)'
    Write-Host '      metadata            API YAML metadata only, no site build'
    Write-Host '      serve               Build and serve on http://0.0.0.0:8080'
    Write-Host '      -SkipBenchmarks     Skip regenerating benchmark pages'
    Write-Host '      -SkipCoverage       Skip regenerating coverage report'
    Write-Host '      DocFX warnings are summarised; full logs are written to artifacts/docs'
    Write-Host ''
    Write-Host '    benchmarks           Benchmark documentation'
    Write-Host '      docs                Generate benchmark result pages'
    Write-Host ''

    Write-Host '    setup                Verify dev environment and directories'
    Write-Host '    report               Repository health report'
    Write-Host '      git                 Repository/commit-level stats (default: all groups)'
    Write-Host '      files               Per-file facts and history'
    Write-Host '      code                Source-code health'
    Write-Host '      -Top                Entries per list (default: 10)'
    Write-Host '      -Path               Restrict file/code scans to a glob (e.g. src/core/**)'
    Write-Host '      -Json               Emit a single JSON object instead of terminal output'
    Write-Host '      -Strict             Exit non-zero on illegal names, severe god classes, or AOT-hostile patterns'
    Write-Host ''
    Write-Host '  Examples:'
    Write-Host '    devops build'
    Write-Host '    devops test -Suite core -Category Integration -Framework net11.0'
    Write-Host '    devops test -Suite core -Category Unit -Verbosity detailed'
    Write-Host '    devops aot'
    Write-Host '    devops coverage -Clean -GenerateReport'
    Write-Host '    devops benchmark -List'
    Write-Host '    devops benchmark -Suite query -Strat fast'
    Write-Host '    devops benchmark -Suite mlt -Strat exhaustive -- --filter *SingleSegment*'
    Write-Host '    devops docs serve'
    Write-Host '    devops docs metadata'
    Write-Host '    devops data gutenberg -BookCount 500'
    Write-Host '    devops report'
    Write-Host '    devops report code -Strict'
    Write-Host ''
    exit 0
}

Export-ModuleMember -Function Invoke-DevOps
