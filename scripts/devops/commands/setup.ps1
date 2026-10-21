$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsSetup {
    param([string[]]$Arguments = @())

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $unknownOptions = @($parsed.Parsed.Keys | Where-Object { $_ -notin @('InstallUi') })
    if ($unknownOptions.Count -gt 0 -or $parsed.Positionals.Count -gt 0 -or $parsed.PassThrough.Count -gt 0) {
        Write-Failure 'Usage: ./devops setup [-InstallUi]'
        return 2
    }
    if ($parsed.Has('InstallUi')) {
        try {
            $module = Install-DevOpsUiDependency
            Write-Success "PwshSpectreConsole $($module.Version) is installed for the current user."
            return 0
        } catch {
            Write-Failure "Could not install the interactive UI dependency: $($_.Exception.Message)"
            return 1
        }
    }

    $repoRoot = Get-RepoRoot

    Write-Heading 'LeanCorpus devops setup'
    Write-Host ''

    # Directories expected by devops commands, docfx, and CI
    $dirs = @(
        (Get-ArtifactRunsRoot -Kind build -RepoRoot $repoRoot),
        (Get-ArtifactRunsRoot -Kind test -RepoRoot $repoRoot),
        (Get-ArtifactRunsRoot -Kind coverage -RepoRoot $repoRoot),
        (Get-ArtifactRunsRoot -Kind benchmark -RepoRoot $repoRoot),
        (Get-BenchmarkDataRoot -RepoRoot $repoRoot),
        (Get-ArtifactRunsRoot -Kind diagnostics -RepoRoot $repoRoot),
        (Get-DocsArtifactPath -Name api -RepoRoot $repoRoot),
        (Join-Path (Get-ArtifactKindRoot -Kind docs -RepoRoot $repoRoot) 'content'),
        (Get-DocsArtifactPath -Name coverage -RepoRoot $repoRoot),
        (Get-DocsArtifactPath -Name site -RepoRoot $repoRoot),
        (Get-DocsArtifactPath -Name diagnostics -RepoRoot $repoRoot),
        (Get-ArtifactKindRoot -Kind package -RepoRoot $repoRoot),
        (Get-ArtifactKindRoot -Kind temp -RepoRoot $repoRoot)
    )

    Write-Heading 'Directories'
    foreach ($path in $dirs) {
        $dir = [System.IO.Path]::GetRelativePath($repoRoot, $path)
        if (-not (Test-Path $path)) {
            New-Item -ItemType Directory -Path $path | Out-Null
            Write-Success "  created $dir"
        } else {
            Write-Info "  $dir"
        }
    }

    Write-Host ''

    # .NET SDKs
    Write-Heading '.NET SDKs'
    $sdkList = dotnet --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Failure '  dotnet CLI not found'
    } else {
        $required = @(
            @{Name = '.NET 10'; Prefix = '10.0'; Recommended = '10.0.x'},
            @{Name = '.NET 11'; Prefix = '11.0'; Recommended = '11.0.x'}
        )

        foreach ($r in $required) {
            $found = $sdkList -split "`n" | Where-Object { $_.Trim() -like "$($r.Prefix).*" } | Select-Object -First 1

            if ($found) {
                Write-Success "  $($r.Name) SDK: $(($found -split '\s+')[0])"
            } else {
                Write-Failure "  $($r.Name) SDK: not found (install $($r.Recommended))"
            }
        }
    }

    Write-Host ''

    # Repository-pinned tools
    Write-Heading 'Tools'
    $toolManifest = Join-Path $repoRoot '.config/dotnet-tools.json'
    if (Test-Path -LiteralPath $toolManifest -PathType Leaf) {
        $manifest = Get-Content -LiteralPath $toolManifest -Raw | ConvertFrom-Json
        if ($manifest.tools.docfx) {
            Write-Info "  DocFX $($manifest.tools.docfx.version): pinned locally (run ./setup.ps1 to restore)"
        } else {
            Write-Failure '  DocFX: missing from the local tool manifest (run ./setup.ps1 after correcting the manifest)'
        }
    } else {
        Write-Failure '  repository tool manifest: not found (run ./setup.ps1 after restoring the repository files)'
    }

    if (Test-DevOpsUiDependency) {
        Write-Success "  PwshSpectreConsole $(Get-DevOpsUiDependencyVersion): installed"
    } else {
        Write-Info "  PwshSpectreConsole $(Get-DevOpsUiDependencyVersion): not installed (run: ./devops setup -InstallUi)"
    }

    Write-Host ''

    # Git status vs origin (notify only, no pull)
    Write-Heading 'Repository'
    $gitDir = Join-Path $repoRoot '.git'
    if (Test-Path $gitDir) {
        $branch = git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null
        $aheadBehind = git -C $repoRoot rev-list --left-right --count '@{u}'...HEAD 2>$null

        if ($LASTEXITCODE -eq 0 -and $aheadBehind -match '^(\d+)\s+(\d+)$') {
            $ahead = [int]$Matches[1]
            $behind = [int]$Matches[2]
            $remote = git -C $repoRoot rev-parse --abbrev-ref '@{u}' 2>$null

            if ($ahead -eq 0 -and $behind -eq 0) {
                Write-Success "  branch $branch : up to date with $remote"
            } else {
                if ($behind -gt 0) { Write-Warn "  branch $branch : $behind commit(s) behind $remote" }
                if ($ahead -gt 0)  { Write-Warn "  branch $branch : $ahead commit(s) ahead of $remote" }
            }
        } else {
            Write-Warn '  no upstream tracking branch configured'
        }
    } else {
        Write-Info '  not a git repository'
    }

    Write-Host ''
    Write-Success 'Setup checks complete.'
    return 0
}
