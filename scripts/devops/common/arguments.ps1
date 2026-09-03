$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertFrom-DevOpsArguments {
    param([string[]]$Arguments = @())
    if ($null -eq $Arguments) { $Arguments = @() }

    $state = @{
        Parsed = @{}
        Positionals = @()
        PassThrough = @()
    }

    $i = 0
    while ($i -lt $Arguments.Count) {
        $arg = $Arguments[$i]
        if ($arg -eq '--') {
            if ($i + 1 -lt $Arguments.Count) {
                $state.PassThrough = @($Arguments[($i + 1)..($Arguments.Count - 1)])
            } else {
                $state.PassThrough = @()
            }
            break
        }
        if ($arg.StartsWith('-')) {
            $name = $arg.TrimStart('-')
            if ($i + 1 -lt $Arguments.Count -and -not $Arguments[$i + 1].StartsWith('-')) {
                $state.Parsed[$name] = $Arguments[$i + 1]
                $i++
            } else {
                $state.Parsed[$name] = $true
            }
        } else {
            $state.Positionals += $arg
        }
        $i++
    }

    $obj = [pscustomobject]@{
        Parsed = $state.Parsed
        Positionals = $state.Positionals
        PassThrough = $state.PassThrough
    }

    $obj | Add-Member -MemberType ScriptMethod -Name Get -Value {
        param($Name, $Default = $null)
        $requestedName = ([string]$Name -replace '[-_]', '').ToLowerInvariant()
        foreach ($key in $this.Parsed.Keys) {
            $candidateName = ([string]$key -replace '[-_]', '').ToLowerInvariant()
            if ($candidateName -eq $requestedName) { return $this.Parsed[$key] }
        }
        return $Default
    } -Force

    $obj | Add-Member -MemberType ScriptMethod -Name Has -Value {
        param($Name)
        $requestedName = ([string]$Name -replace '[-_]', '').ToLowerInvariant()
        foreach ($key in $this.Parsed.Keys) {
            $candidateName = ([string]$key -replace '[-_]', '').ToLowerInvariant()
            if ($candidateName -eq $requestedName) { return $true }
        }
        return $false
    } -Force

    return $obj
}
