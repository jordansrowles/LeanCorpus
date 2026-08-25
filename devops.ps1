#!/usr/bin/env pwsh

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module "$PSScriptRoot/scripts/devops/DevOps.psm1" -Force

$command = if ($args.Count -gt 0) { $args[0] } else { '' }
$remaining = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }

$result = Invoke-DevOps $command $remaining
exit $result
