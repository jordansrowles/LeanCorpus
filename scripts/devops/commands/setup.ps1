$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsSetup {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot

    Write-Heading 'LeanCorpus devops setup'
    Write-Host ''

    # Directories expected by devops commands, docfx, and CI
    $dirs = @(
        'bench',
        'bench/data',
        'coverage-results',
        'docs/api',
        'docs/coverage',
        'docs/site',
        'docs/changelog'
    )

    Write-Heading 'Directories'
    foreach ($dir in $dirs) {
        $path = Join-Path $repoRoot $dir
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

    # Tools
    Write-Heading 'Tools'
    $docfx = Get-Command docfx -ErrorAction SilentlyContinue
    if ($docfx) {
        Write-Success '  docfx: installed'
    } else {
        Write-Failure '  docfx: not installed (run: dotnet tool install -g docfx)'
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
    exit 0
}
