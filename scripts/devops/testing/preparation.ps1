$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-AotExecutablePath {
    param(
        [object]$Target,
        [string]$RepoRoot
    )

    $publishDirectory = Join-Path $RepoRoot "src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/bin/$($Target.Configuration)/$($Target.Framework)/$($Target.RuntimeIdentifier)/publish"
    $fileName = if ($Target.RuntimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
        'Rowles.LeanCorpus.Tests.AOTSmoke.exe'
    } else {
        'Rowles.LeanCorpus.Tests.AOTSmoke'
    }

    return Join-Path $publishDirectory $fileName
}

function Get-MtpExecutablePath {
    param(
        [object]$Target,
        [string]$RepoRoot
    )

    $projectPath = Resolve-TestProjectPath -Target $Target -RepoRoot $RepoRoot
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $projectDirectory = [System.IO.Path]::GetDirectoryName($projectPath)
    $outputDirectory = Join-Path $projectDirectory "bin/$($Target.Configuration)/$($Target.Framework)"
    $candidates = @(
        (Join-Path $outputDirectory $projectName),
        (Join-Path $outputDirectory "$projectName.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $candidates[0]
}

function Prepare-TestTargets {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Targets,
        [Parameter(Mandatory = $true)]
        [object]$Options,
        [string]$RepoRoot = (Get-RepoRoot)
    )

    $prepared = [System.Collections.Generic.List[object]]::new()
    $restored = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $built = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $published = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $restoreStopwatch = [System.Diagnostics.Stopwatch]::new()
    $buildStopwatch = [System.Diagnostics.Stopwatch]::new()
    $publishStopwatch = [System.Diagnostics.Stopwatch]::new()
    $preparationTimings = [System.Collections.Generic.List[object]]::new()
    $preparationTimingByKey = @{}

    Write-Heading 'Preparing test targets'
    if ($Options.Ci) {
        Write-Info '  Managed targets: using CI-prepared build output.'
    }

    foreach ($target in @($Targets)) {
        $projectPath = Resolve-TestProjectPath -Target $target -RepoRoot $RepoRoot
        if ($target.RunnerKind -eq 'Mtp') {
            $targetKey = "$projectPath|$($target.Framework)|$($target.Configuration)"
            $reportedWorkItem = "$($target.Project)|$($target.Framework)|$($target.Configuration)"
            if (-not $Options.Ci -and $restored.Add($targetKey)) {
                Write-Info "  Restoring $($target.Key)..."
                $operationStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
                $restoreStopwatch.Start()
                try {
                    Invoke-DotNet @('restore', $projectPath, '--nologo') | Out-Host
                } finally {
                    $operationStopwatch.Stop()
                    $restoreStopwatch.Stop()
                    $timing = [pscustomobject]@{
                        Stage = 'Restore'
                        Operation = 'restore'
                        WorkItem = $reportedWorkItem
                        TargetKeys = [System.Collections.Generic.List[string]]::new()
                        DurationMs = [Math]::Round($operationStopwatch.Elapsed.TotalMilliseconds, 3)
                    }
                    [void]$timing.TargetKeys.Add($target.Key)
                    $preparationTimingByKey["restore|$targetKey"] = $timing
                    [void]$preparationTimings.Add($timing)
                }
            } elseif ($preparationTimingByKey.ContainsKey("restore|$targetKey")) {
                [void]$preparationTimingByKey["restore|$targetKey"].TargetKeys.Add($target.Key)
            }
            if (-not $Options.Ci -and $built.Add($targetKey)) {
                Write-Info "  Building $($target.Key)..."
                $operationStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
                $buildStopwatch.Start()
                try {
                    Invoke-DotNet @('build', $projectPath, '--configuration', $target.Configuration,
                        '--framework', $target.Framework, '--no-restore', '--nologo',
                        '-p:UseSharedCompilation=false') | Out-Host
                } finally {
                    $operationStopwatch.Stop()
                    $buildStopwatch.Stop()
                    $timing = [pscustomobject]@{
                        Stage = 'Build'
                        Operation = 'build'
                        WorkItem = $reportedWorkItem
                        TargetKeys = [System.Collections.Generic.List[string]]::new()
                        DurationMs = [Math]::Round($operationStopwatch.Elapsed.TotalMilliseconds, 3)
                    }
                    [void]$timing.TargetKeys.Add($target.Key)
                    $preparationTimingByKey["build|$targetKey"] = $timing
                    [void]$preparationTimings.Add($timing)
                }
            } elseif ($preparationTimingByKey.ContainsKey("build|$targetKey")) {
                [void]$preparationTimingByKey["build|$targetKey"].TargetKeys.Add($target.Key)
            }

            $executablePath = Get-MtpExecutablePath -Target $target -RepoRoot $RepoRoot
            if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
                throw "Managed test build completed but executable was not found: $executablePath"
            }

            [void]$prepared.Add([pscustomobject]@{
                Target = $target
                ProjectPath = $projectPath
                ExecutablePath = $executablePath
            })
            continue
        }

        if ($target.RunnerKind -ne 'AotNative') {
            throw "Unsupported runner kind '$($target.RunnerKind)' for target '$($target.Key)'."
        }

        $publishKey = "$projectPath|$($target.Framework)|$($target.RuntimeIdentifier)|$($target.Configuration)"
        $reportedWorkItem = "$($target.Project)|$($target.Framework)|$($target.RuntimeIdentifier)|$($target.Configuration)"
        if ($published.Add($publishKey)) {
            Write-Info "  Publishing $($target.Key)..."
            $operationStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            $publishStopwatch.Start()
            try {
                Invoke-DotNet @('publish', $projectPath, '--configuration', $target.Configuration,
                    '--runtime', $target.RuntimeIdentifier, '--self-contained', 'true',
                    '--framework', $target.Framework, '--nologo', '-p:UseSharedCompilation=false') | Out-Host
            } finally {
                $operationStopwatch.Stop()
                $publishStopwatch.Stop()
                $timing = [pscustomobject]@{
                    Stage = 'AOTPublish'
                    Operation = 'publish'
                    WorkItem = $reportedWorkItem
                    TargetKeys = [System.Collections.Generic.List[string]]::new()
                    DurationMs = [Math]::Round($operationStopwatch.Elapsed.TotalMilliseconds, 3)
                }
                [void]$timing.TargetKeys.Add($target.Key)
                $preparationTimingByKey["publish|$publishKey"] = $timing
                [void]$preparationTimings.Add($timing)
            }
        }

        $executablePath = Get-AotExecutablePath -Target $target -RepoRoot $RepoRoot
        if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
            throw "AOT publish completed but executable was not found: $executablePath"
        }
        [void]$prepared.Add([pscustomobject]@{
            Target = $target
            ProjectPath = $projectPath
            ExecutablePath = $executablePath
        })
    }

    Write-Success 'Preparation complete.'
    return [pscustomobject]@{
        Targets = @($prepared.ToArray())
        RestoreDuration = $restoreStopwatch.Elapsed
        BuildDuration = $buildStopwatch.Elapsed
        AotPublishDuration = $publishStopwatch.Elapsed
        PreparationTimings = @($preparationTimings.ToArray())
    }
}
