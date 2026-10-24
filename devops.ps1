#!/usr/bin/env pwsh

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot/scripts/devops/managed-host.ps1"

$managedHost = Join-Path $PSScriptRoot (Get-ManagedHostExecutableName)
$hostStatus = Test-ManagedHost -RepositoryRoot $PSScriptRoot -ExecutablePath $managedHost
if (-not $hostStatus.IsCurrent) {
    [Console]::Error.WriteLine("Managed DevOps host is unavailable: $($hostStatus.Reason)")
    exit 3
}

$forwardedArguments = @($args)
if ($forwardedArguments.Count -eq 0) {
    $forwardedArguments = @('--help')
}

& $managedHost @forwardedArguments
exit $LASTEXITCODE
