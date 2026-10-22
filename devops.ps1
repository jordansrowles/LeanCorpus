#!/usr/bin/env pwsh

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot/scripts/devops/managed-host.ps1"

$command = if ($args.Count -gt 0) { $args[0] } else { '' }
$remaining = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }

if ($command -in @('build', 'clean', 'server', 'report', 'coverage', 'benchmark', 'diagnostics')) {
    $managedHost = Join-Path $PSScriptRoot (Get-ManagedHostExecutableName)
    $hostStatus = Test-ManagedHost -RepositoryRoot $PSScriptRoot -ExecutablePath $managedHost
    if (-not $hostStatus.IsCurrent) {
        [Console]::Error.WriteLine("Managed DevOps command '$command' is unavailable: $($hostStatus.Reason)")
        exit 3
    }

    $forwardedArguments = @($command) + @($remaining)
    & $managedHost @forwardedArguments
    exit $LASTEXITCODE
}

Import-Module "$PSScriptRoot/scripts/devops/DevOps.psm1" -Force
$result = Invoke-DevOps $command $remaining
exit $result
