$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsBuild {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $configuration = $parsed.Get('Configuration', 'Release')
    $framework = $parsed.Get('Framework', (Get-DefaultFramework))
    $project = $parsed.Get('Project', '')
    $repoRoot = Get-RepoRoot

    if ($project) {
        $projectPath = Join-Path $repoRoot $project
        $buildArgs = @('build', $projectPath, '-c', $configuration)
        Write-Heading "Building project: $project"
    } else {
        $slnPath = Join-Path $repoRoot 'Rowles.LeanCorpus.slnx'
        $buildArgs = @('build', $slnPath, '-c', $configuration)
        Write-Heading 'Building LeanCorpus...'
    }

    Write-Host "  Configuration: $configuration"
    Write-Host "  Framework:     $framework"
    if ($project) { Write-Host "  Project:       $project" }
    Write-Host ''

    Invoke-DotNet (@($buildArgs) + @('-f', $framework, '-p:UseSharedCompilation=false'))
    Write-Success 'Build succeeded.'
    exit 0
}
