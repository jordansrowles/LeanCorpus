<#
.SYNOPSIS
    Starts a benchmark run on the Debian benchmark host.

.DESCRIPTION
    Connects to the configured host over SSH, updates the repository from
    origin/main, and starts the benchmark script inside a detached tmux session.

.PARAMETER Remote
    SSH target for the benchmark host. Defaults to jordan@debian.

.PARAMETER RemotePath
    Repository path on the remote host.

.PARAMETER Branch
    Branch to fetch, check out, and pull from origin. Defaults to main.

.PARAMETER SessionName
    Name of the tmux session that will run the benchmark.

.PARAMETER BenchmarkArgs
    Additional arguments passed to ./scripts/benchmark.sh on the remote host.

.EXAMPLE
    .\scripts\send-for-bench.ps1
    Updates the remote main checkout and starts the default benchmark run.

.EXAMPLE
    .\scripts\send-for-bench.ps1 -BenchmarkArgs '--suite query --strat intense'
    Starts the query benchmark suite with the intense strategy.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Remote = 'jordan@debian',

    [string]$RemotePath = '/home/jordan/code/leancorpus',

    [string]$Branch = 'main',

    [string]$SessionName = 'leancorpus-bench',

    [string]$BenchmarkArgs = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-BashSingleQuotedString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "'\''") + "'"
}

$quotedPath = ConvertTo-BashSingleQuotedString -Value $RemotePath
$quotedBranch = ConvertTo-BashSingleQuotedString -Value $Branch
$quotedSession = ConvertTo-BashSingleQuotedString -Value $SessionName
$benchmarkCommand = './devops benchmark'

if (-not [string]::IsNullOrWhiteSpace($BenchmarkArgs)) {
    $benchmarkCommand = "$benchmarkCommand $BenchmarkArgs"
}

$quotedBenchmarkCommand = ConvertTo-BashSingleQuotedString -Value $benchmarkCommand

$remoteScript = @"
set -euo pipefail
cd $quotedPath
git fetch origin $quotedBranch
git checkout $quotedBranch
git pull --ff-only origin $quotedBranch
if tmux has-session -t $quotedSession 2>/dev/null; then
    echo "tmux session already exists." >&2
    exit 1
fi
tmux new-session -d -s $quotedSession $quotedBenchmarkCommand
tmux display-message -p -t $quotedSession "Started #S"
"@

if ($PSCmdlet.ShouldProcess($Remote, "start '$SessionName' benchmark session")) {
    Write-Host "Connecting to $Remote..." -ForegroundColor Cyan
    $remoteScript | ssh $Remote 'bash -s'

    if ($LASTEXITCODE -ne 0) {
        throw "Remote benchmark setup failed with exit code $LASTEXITCODE."
    }
}
