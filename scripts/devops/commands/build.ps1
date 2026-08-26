$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsBuild {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $configuration = $parsed.Get('Configuration', 'Release')
    # The Community Server is intentionally net11-only, so the solution build
    # defaults to the framework that can build every project. Core libraries
    # remain explicitly selectable with -Framework net10.0 when needed.
    $framework = $parsed.Get('Framework', 'net11.0')
    $project = $parsed.Get('Project', '')
    $repoRoot = Get-RepoRoot

    if ($project) {
        $projectPath = Join-Path $repoRoot $project
        $buildArgs = @('build', $projectPath, '-c', $configuration)
        Write-Heading "Building project: $project"
        $frameworkArgs = @('-f', $framework)
    } else {
        $slnPath = Join-Path $repoRoot 'Rowles.LeanCorpus.slnx'
        $buildArgs = @('build', $slnPath, '-c', $configuration)
        Write-Heading 'Building LeanCorpus...'
        # A solution contains the netstandard source generator and the
        # net11-only server alongside multi-targeted libraries. Let MSBuild
        # select each project's declared targets rather than forcing one TFM.
        $frameworkArgs = @()
    }

    Write-Host "  Configuration: $configuration"
    if ($project) {
        Write-Host "  Framework:     $framework"
    } else {
        Write-Host '  Framework:     each project target (net11.0 server)'
    }
    if ($project) { Write-Host "  Project:       $project" }
    Write-Host ''

    Invoke-DotNet (@($buildArgs) + $frameworkArgs + @('-p:UseSharedCompilation=false'))
    Write-Success 'Build succeeded.'
    exit 0
}
