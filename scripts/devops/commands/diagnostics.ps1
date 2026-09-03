$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Show-DiagnosticsHelp {
    Write-Host ''
    Write-Host '  Diagnostics commands:'
    Write-Host ''
    Write-Host '    devops diagnostics ps'
    Write-Host '    devops diagnostics counters --pid <pid> [-- <tool arguments>]'
    Write-Host '    devops diagnostics trace --pid <pid> [-- <tool arguments>]'
    Write-Host '    devops diagnostics gcdump --pid <pid> [-- <tool arguments>]'
    Write-Host '    devops diagnostics dump --pid <pid> [--type Mini|Heap|Full|Triage]'
    Write-Host '    devops diagnostics symbols <artifact> [-- <tool arguments>]'
    Write-Host '    devops diagnostics capture --pid <pid> [--duration 5s]'
    Write-Host ''
    Write-Host '  Trace, GC dump, dump and capture output is written under artifacts/diagnostics.'
    Write-Host '  Dumps may contain sensitive application memory.'
    Write-Host ''
}

function Invoke-DiagnosticsProcessList {
    Get-Process |
        Select-Object Id, ProcessName |
        Sort-Object Id |
        Format-Table -AutoSize |
        Out-Host
    return 0
}

function Invoke-DevOpsDiagnostics {
    param([string[]]$Arguments = @())

    try {
        if ($Arguments.Count -eq 0 -or $Arguments[0] -in @('-h', '--help', '-Help', '--Help')) {
            Show-DiagnosticsHelp
            return 0
        }

        $subCommand = ([string]$Arguments[0]).ToLowerInvariant()
        $remaining = if ($Arguments.Count -gt 1) { @($Arguments[1..($Arguments.Count - 1)]) } else { @() }
        if ($subCommand -eq 'ps') {
            return Invoke-DiagnosticsProcessList
        }

        $parsed = ConvertFrom-DevOpsArguments $remaining
        $repoRoot = Get-RepoRoot
        $commandLine = ConvertTo-CommandLineText -Command './devops diagnostics' -Arguments $Arguments
        switch ($subCommand) {
            'counters' { return Invoke-DiagnosticsCounters -Parsed $parsed -RepoRoot $repoRoot -CommandLine $commandLine }
            'trace'    { return Invoke-DiagnosticsTrace -Parsed $parsed -RepoRoot $repoRoot -CommandLine $commandLine }
            'gcdump'   { return Invoke-DiagnosticsGcDump -Parsed $parsed -RepoRoot $repoRoot -CommandLine $commandLine }
            'dump'     { return Invoke-DiagnosticsDump -Parsed $parsed -RepoRoot $repoRoot -CommandLine $commandLine }
            'symbols'  { return Invoke-DiagnosticsSymbols -Parsed $parsed -RepoRoot $repoRoot -CommandLine $commandLine }
            'capture'  { return Invoke-DiagnosticsCapture -Parsed $parsed -RepoRoot $repoRoot -CommandLine $commandLine }
            default {
                throw "Unknown diagnostics command '$subCommand'. Use 'devops diagnostics --help'."
            }
        }
    } catch {
        Write-Failure "Diagnostics command failed: $($_.Exception.Message)"
        return 1
    }
}
