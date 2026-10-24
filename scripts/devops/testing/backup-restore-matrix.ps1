$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-BackupRestoreSourceManifest {
    param([string]$RepoRoot, [string]$ManifestPath = '')

    if ($ManifestPath) {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
        if ($manifest.commit -notmatch '^[0-9a-f]{40}$' -or @($manifest.files).Count -eq 0) {
            throw 'Source manifest must contain a commit and a non-empty file inventory.'
        }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $manifest.files) {
            if ([string]::IsNullOrWhiteSpace($file.path) -or [IO.Path]::IsPathRooted($file.path) -or
                $file.path -match '(^|[/\\])\.\.([/\\]|$)|:' -or -not $seen.Add($file.path) -or
                $file.sha256 -notmatch '^[0-9a-f]{64}$') {
                throw "Invalid source manifest entry: $($file.path)"
            }
            $path = Join-Path $RepoRoot $file.path
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
                throw "Source manifest mismatch: $($file.path)"
            }
        }
    } else {
        $paths = @(& git -c core.quotepath=false -C $RepoRoot ls-files --cached --others --exclude-standard)
        if ($LASTEXITCODE -ne 0 -or $paths.Count -eq 0) {
            throw 'Source provenance requires Git or -SourceManifest for an exported checkout.'
        }
        $files = @($paths | Where-Object { Test-Path -LiteralPath (Join-Path $RepoRoot $_) -PathType Leaf } |
            ForEach-Object {
                [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $RepoRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
            })
        $manifest = [pscustomobject]@{ commit = (& git -C $RepoRoot rev-parse HEAD).Trim(); files = $files }
    }
    $lines = [string[]]@($manifest.files | ForEach-Object { "$($_.path)`t$($_.sha256)" })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if ($ManifestPath -and $manifest.PSObject.Properties.Name -contains 'fingerprint' -and
        $manifest.fingerprint -ne $fingerprint) { throw 'Source manifest fingerprint does not match its inventory.' }
    return [ordered]@{ commit = $manifest.commit; fingerprint = $fingerprint; files = $manifest.files }
}

function Invoke-BackupRestoreMatrix {
    param([string[]]$Arguments)

    $parsed = ConvertFrom-DevOpsArguments $Arguments
    $repoRoot = Get-RepoRoot
    $framework = [string]$parsed.Get('Framework', (Get-DefaultFramework))
    if ($framework -notin @('net10.0', 'net11.0')) { throw 'Matrix framework must be net10.0 or net11.0.' }
    $count = ConvertTo-TestCount -Value $parsed.Get('Count', 3)
    $configuration = [string]$parsed.Get('Configuration', 'Release')
    $alternate = [string]$parsed.Get('TempRoot', (Join-Path $repoRoot 'artifacts/temp/backup-restore-matrix'))
    $source = Get-BackupRestoreSourceManifest -RepoRoot $repoRoot -ManifestPath ([string]$parsed.Get('SourceManifest', ''))
    $run = New-ArtifactRun -Kind test -Framework $framework -Configuration $configuration `
        -Target 'backup-restore-matrix' -RepoRoot $repoRoot `
        -CommandLine (ConvertTo-CommandLineText -Command 'pwsh ./devops.ps1 test backup-restore-matrix' -Arguments $Arguments)
    Write-AtomicJsonFile -Path (Join-Path $run.RunDirectory 'source-manifest.json') -Value $source
    $variables = @('LEANCORPUS_BACKUP_RESTORE_MEASUREMENT', 'LEANCORPUS_BACKUP_COMPOUND',
        'LEANCORPUS_BACKUP_SOURCE_DURABLE', 'LEANCORPUS_BACKUP_TEMP_ROOT',
        'LEANCORPUS_BACKUP_SOURCE_COMMIT', 'LEANCORPUS_BACKUP_SOURCE_FINGERPRINT', 'CHAOS_ITERATIONS')
    $previous = @{}
    foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
    $status = 'Incomplete'
    $reports = [Collections.Generic.List[object]]::new()
    $variants = @(
        @{ name = 'baseline'; compound = $false; durable = $true; root = $null },
        @{ name = 'compound'; compound = $true; durable = $true; root = $null },
        @{ name = 'source-nondurable'; compound = $false; durable = $false; root = $null },
        @{ name = 'combined'; compound = $true; durable = $false; root = $null },
        @{ name = 'alternate-temp'; compound = $false; durable = $true; root = $alternate }
    )
    try {
        if ($parsed.Has('Iterations')) {
            $iterations = ConvertTo-TestCount -Value $parsed.Get('Iterations', '')
            [Environment]::SetEnvironmentVariable('CHAOS_ITERATIONS', [string]$iterations)
        }
        [Environment]::SetEnvironmentVariable('LEANCORPUS_BACKUP_SOURCE_COMMIT', $source.commit)
        [Environment]::SetEnvironmentVariable('LEANCORPUS_BACKUP_SOURCE_FINGERPRINT', $source.fingerprint)
        foreach ($variant in $variants) {
            for ($launch = 1; $launch -le $count; $launch++) {
                $reportPath = Join-Path $run.RunDirectory "$($variant.name)-$launch.json"
                [Environment]::SetEnvironmentVariable('LEANCORPUS_BACKUP_RESTORE_MEASUREMENT', $reportPath)
                [Environment]::SetEnvironmentVariable('LEANCORPUS_BACKUP_COMPOUND', [string]$variant.compound)
                [Environment]::SetEnvironmentVariable('LEANCORPUS_BACKUP_SOURCE_DURABLE', [string]$variant.durable)
                [Environment]::SetEnvironmentVariable('LEANCORPUS_BACKUP_TEMP_ROOT', $variant.root)
                $testArgs = @('-Suite', 'core', '-Framework', $framework, '-Configuration', $configuration,
                    '-Filter', 'FullyQualifiedName~BackupRestoreStateMachineTests.Backup_restore_operations_match_the_model',
                    '-HangTimeout', 'off')
                if ($parsed.Has('NoRestore')) { $testArgs += '-NoRestore' }
                Write-Heading "Backup/restore: $($variant.name), launch $launch/$count"
                $exitCode = Invoke-DevOpsTest -Arguments $testArgs
                if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                    $status = 'Failed'
                    throw "Matrix cell failed or has no report: $($variant.name)-$launch"
                }
                $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
                if (-not $report.Passed -or $report.SourceFingerprint -ne $source.fingerprint -or
                    $report.Compound -ne $variant.compound -or $report.SourceDurable -ne $variant.durable -or
                    -not $report.BackupRestoreDurable -or $report.Framework -ne ".NETCoreApp,Version=v$($framework.Substring(3))") {
                    $status = 'Failed'
                    throw "Invalid matrix report: $reportPath"
                }
                $reports.Add($report)
            }
        }
        $assemblies = @($reports | ForEach-Object { "$($_.CoreAssemblySha256)/$($_.TestAssemblySha256)/$($_.Runtime)" } | Select-Object -Unique)
        if ($assemblies.Count -ne 1) { $status = 'Failed'; throw 'Measured assemblies or runtime changed between cells.' }
        $workloads = @($reports | ForEach-Object {
            "$($_.Seed)/$($_.Gamma)/$($_.EffectiveIterations)/$($_.OperationCount)/$($_.OperationFingerprint)"
        } | Select-Object -Unique)
        if ($workloads.Count -ne 1) { $status = 'Failed'; throw 'Matrix workload provenance does not match.' }
        $finalSource = Get-BackupRestoreSourceManifest -RepoRoot $repoRoot -ManifestPath ([string]$parsed.Get('SourceManifest', ''))
        if ($finalSource.fingerprint -ne $source.fingerprint) { $status = 'Failed'; throw 'Source changed during matrix measurement.' }
        $status = 'Passed'
        Write-Success "Matched backup/restore matrix: $($run.RunDirectory)"
        return 0
    } finally {
        foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
        Complete-ArtifactRun -RunDirectory $run.RunDirectory -Status $status -AdditionalValues @{
            sourceFingerprint = $source.fingerprint; completedCells = $reports.Count; expectedCells = $variants.Count * $count
        }
    }
}
