$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsBenchmarks {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $subCmd = $parsed.Positionals[0]
    if (-not $subCmd) { $subCmd = $parsed.Get('SubCommand', '') }

    if ($subCmd -ne 'docs') {
        Write-Error "Unknown benchmarks subcommand: '$subCmd'. Expected: docs"
        exit 1
    }

    & (Join-Path (Get-ScriptsPath) 'benchmarks/generate-docs.ps1')
    exit $LASTEXITCODE
}
