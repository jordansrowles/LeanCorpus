$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsDataForge {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot
    $projectPath = Join-Path $repoRoot 'src/devops/Rowles.DataForge.Tool/Rowles.DataForge.Tool.csproj'
    $dotnetArguments = @(
        'run',
        '--project', $projectPath,
        '--framework', 'net10.0',
        '--configuration', 'Release',
        '--'
    )
    $dotnetArguments += $Arguments

    Write-Heading 'Running DataForge'
    $argumentLog = ($dotnetArguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    Write-Host "  dotnet $argumentLog" -ForegroundColor DarkGray
    Push-Location $repoRoot
    try {
        dotnet @dotnetArguments
        $dotnetExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    exit $dotnetExitCode
}
