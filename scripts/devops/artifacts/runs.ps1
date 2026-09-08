$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function New-ArtifactRunId {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('test', 'coverage', 'benchmark', 'diagnostics')]
        [string]$Kind,
        [string]$Framework = ''
    )

    $utc = [DateTime]::UtcNow
    $entropy = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $os = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
    $suffix = if ($Framework) { "-$os-$($Framework.Replace('.', ''))" } else { "-$os" }
    return "$($utc.ToString('yyyyMMdd-HHmmss'))-$entropy$suffix"
}

function Get-ArtifactGitContext {
    param([string]$RepoRoot = (Get-RepoRoot))

    $commit = (& git -C $RepoRoot rev-parse HEAD 2>$null | Select-Object -First 1) -as [string]
    $branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1) -as [string]
    return [ordered]@{
        commit = if ($commit) { $commit.Trim() } else { '' }
        branch = if ($branch) { $branch.Trim() } else { '' }
        dirty = @(& git -C $RepoRoot status --porcelain --untracked-files=all 2>$null).Count -gt 0
    }
}

function New-ArtifactRun {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('test', 'coverage', 'benchmark', 'diagnostics')]
        [string]$Kind,
        [string]$Framework = '',
        [string]$Configuration = 'Release',
        [string]$Target = '',
        [string]$CommandLine = '',
        [string]$RepoRoot = (Get-RepoRoot),
        [string]$RunId = ''
    )

    if (-not $RunId) {
        $RunId = New-ArtifactRunId -Kind $Kind -Framework $Framework
    }
    $runDirectory = Join-Path (Get-ArtifactRunsRoot -Kind $Kind -RepoRoot $RepoRoot) $RunId
    [void][System.IO.Directory]::CreateDirectory($runDirectory)
    $git = Get-ArtifactGitContext -RepoRoot $RepoRoot
    $manifest = [ordered]@{
        schemaVersion = 1
        runId = $RunId
        kind = $Kind
        startedAtUtc = [DateTime]::UtcNow.ToString('O')
        completedAtUtc = $null
        commit = $git.commit
        branch = $git.branch
        dirty = $git.dirty
        framework = $Framework
        configuration = $Configuration
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        machine = [Environment]::MachineName
        ci = [bool]([Environment]::GetEnvironmentVariable('CI'))
        target = $Target
        commandLine = $CommandLine
        status = 'Running'
    }
    Write-AtomicJsonFile -Path (Join-Path $runDirectory 'run.json') -Value $manifest
    return [pscustomobject]@{ RunId = $RunId; RunDirectory = $runDirectory; Manifest = $manifest }
}

function Complete-ArtifactRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Passed', 'Failed', 'Incomplete', 'Cancelled')]
        [string]$Status,
        [hashtable]$AdditionalValues = @{}
    )
    $values = @{
        completedAtUtc = [DateTime]::UtcNow.ToString('O')
        status = $Status
    }
    foreach ($key in $AdditionalValues.Keys) { $values[$key] = $AdditionalValues[$key] }
    Update-ArtifactRunManifest -RunDirectory $RunDirectory -Values $values
}

function Get-LatestSuccessfulArtifactRun {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('test', 'coverage', 'benchmark', 'diagnostics')]
        [string]$Kind,
        [string]$RepoRoot = (Get-RepoRoot),
        [string]$Commit = ''
    )

    $runsRoot = Get-ArtifactRunsRoot -Kind $Kind -RepoRoot $RepoRoot
    if (-not (Test-Path $runsRoot -PathType Container)) {
        return $null
    }

    $candidates = [System.Collections.Generic.List[object]]::new()
    foreach ($runDirectory in @(Get-ChildItem -LiteralPath $runsRoot -Directory -ErrorAction SilentlyContinue)) {
        $manifestPath = Join-Path $runDirectory.FullName 'run.json'
        if (-not (Test-Path $manifestPath -PathType Leaf)) {
            continue
        }

        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ([string]$manifest.status -ne 'Passed') {
                continue
            }
            if ($Commit -and -not [string]::Equals(
                    [string]$manifest.commit,
                    $Commit,
                    [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $completedAtUtc = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse(
                    [string]$manifest.completedAtUtc,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::RoundtripKind,
                    [ref]$completedAtUtc)) {
                continue
            }

            [void]$candidates.Add([pscustomobject]@{
                RunDirectory = $runDirectory.FullName
                RunId = [string]$manifest.runId
                Manifest = $manifest
                CompletedAtUtc = $completedAtUtc
            })
        } catch {
            # Retained runs can be incomplete or partially written. Ignore them
            # during discovery and let the caller continue without evidence.
            continue
        }
    }

    return $candidates | Sort-Object CompletedAtUtc -Descending | Select-Object -First 1
}

function Set-ArtifactProcessEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunId,
        [Parameter(Mandatory = $true)]
        [string]$Kind,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [string]$Target = '',
        [string]$Suite = '',
        [int]$Iteration = 1,
        [bool]$Ci = $false,
        [bool]$Diagnostics = $false
    )
    $env:LEANCORPUS_RUN_ID = $RunId
    $env:LEANCORPUS_RUN_KIND = $Kind
    $env:LEANCORPUS_ARTIFACT_DIR = $ArtifactDirectory
    $env:LEANCORPUS_TARGET = $Target
    $env:LEANCORPUS_SUITE = $Suite
    $env:LEANCORPUS_ITERATION = $Iteration.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:LEANCORPUS_CI = $Ci.ToString().ToLowerInvariant()
    $env:LEANCORPUS_DIAGNOSTICS = $Diagnostics.ToString().ToLowerInvariant()
}

function Clear-ArtifactProcessEnvironment {
    foreach ($name in @(
        'LEANCORPUS_RUN_ID', 'LEANCORPUS_RUN_KIND', 'LEANCORPUS_ARTIFACT_DIR',
        'LEANCORPUS_TARGET', 'LEANCORPUS_SUITE', 'LEANCORPUS_ITERATION', 'LEANCORPUS_CI',
        'LEANCORPUS_DIAGNOSTICS', 'LEANCORPUS_TELEMETRY')) {
        [Environment]::SetEnvironmentVariable($name, $null)
    }
}
