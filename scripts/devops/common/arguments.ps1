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
            $state.PassThrough = $Arguments[($i + 1)..($Arguments.Count - 1)]
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
        foreach ($key in @($Name, $Name.ToLower(), $Name.ToUpper())) {
            if ($this.Parsed.ContainsKey($key)) { return $this.Parsed[$key] }
        }
        return $Default
    } -Force

    $obj | Add-Member -MemberType ScriptMethod -Name Has -Value {
        param($Name)
        foreach ($key in @($Name, $Name.ToLower(), $Name.ToUpper())) {
            if ($this.Parsed.ContainsKey($key)) { return $true }
        }
        return $false
    } -Force

    return $obj
}
