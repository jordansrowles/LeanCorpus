$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsServer {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $subCommand = if ($parsed.Positionals.Count -gt 0) { $parsed.Positionals[0].ToLowerInvariant() } else { 'start' }

    if ($subCommand -in @('help', '--help', '-h') -or $parsed.Has('Help')) {
        Write-Host ''
        Write-Host '  Usage: devops server start [options] [-- <application arguments>]'
        Write-Host ''
        Write-Host '  Start the local Community Server reference host on .NET 11.'
        Write-Host ''
        Write-Host '  Options:'
        Write-Host '    -Configuration       Debug or Release (default: Debug)'
        Write-Host '    -NoBuild             Pass --no-build to dotnet run'
        Write-Host '    -NoRestore           Pass --no-restore to dotnet run'
        Write-Host '    -External            Listen on all IPv4 interfaces at port 5080'
        Write-Host '    --                   Pass application arguments to the host'
        Write-Host ''
        Write-Host '  The server runs in the foreground. Press Ctrl+C to stop it.'
        Write-Host ''
        exit 0
    }

    if ($subCommand -ne 'start') {
        Write-Error "Unknown server command '$subCommand'. Valid: start"
        exit 1
    }

    $configuration = $parsed.Get('Configuration', 'Debug')
    if ($configuration -notin @('Debug', 'Release')) {
        Write-Error "Unknown configuration '$configuration'. Valid: Debug, Release"
        exit 1
    }

    $repoRoot = Get-RepoRoot
    $projectPath = Join-Path $repoRoot 'src/server/Rowles.LeanCorpus.Server.Local/Rowles.LeanCorpus.Server.Local.csproj'
    $dotnetArguments = @(
        'run',
        '--project', $projectPath,
        '--framework', 'net11.0',
        '--configuration', $configuration
    )
    if ($parsed.Has('NoBuild')) { $dotnetArguments += '--no-build' }
    if ($parsed.Has('NoRestore')) { $dotnetArguments += '--no-restore' }
    if ($parsed.Has('External')) {
        $dotnetArguments += '--'
        $dotnetArguments += @('--urls=http://0.0.0.0:5080', '--AllowedHosts=*')
    }
    if ($parsed.PassThrough.Count -gt 0) {
        if (-not $parsed.Has('External')) { $dotnetArguments += '--' }
        $dotnetArguments += $parsed.PassThrough
    }

    Write-Heading 'Starting LeanCorpus Community Server'
    Write-Host '  Framework:     net11.0'
    Write-Host "  Configuration: $configuration"
    if ($parsed.Has('External')) {
        Write-Host '  Listener:      http://0.0.0.0:5080 (external)' -ForegroundColor Yellow
        Write-Host '  Warning:       Requests are not encrypted; use only on a trusted network.' -ForegroundColor Yellow
    } else {
        Write-Host '  Listener:      http://127.0.0.1:5080 (loopback only)'
    }
    Write-Host ''
    Write-Host '  Running in the foreground. Press Ctrl+C to stop.'
    Write-Host ''
    Invoke-DotNet $dotnetArguments
    exit 0
}
