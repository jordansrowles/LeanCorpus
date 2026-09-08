$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsPack {
    param([string[]]$Arguments = @())
    try {
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $repoRoot = Get-RepoRoot
        $configuration = [string]$parsed.Get('Configuration', 'Release')
        $outputDirectory = Join-Path (Get-ArtifactKindRoot -Kind package -RepoRoot $repoRoot) 'release'
        if ($parsed.Has('Clean') -and (Test-Path $outputDirectory)) {
            Remove-OwnedArtifactPath -Path $outputDirectory -ArtifactRoot (Get-ArtifactRoot -RepoRoot $repoRoot)
        }
        [void][System.IO.Directory]::CreateDirectory($outputDirectory)

        $arguments = @('pack', (Join-Path $repoRoot 'Rowles.LeanCorpus.slnx'), '-c', $configuration, '--output', $outputDirectory)
        if ($parsed.Has('NoBuild')) { $arguments += '--no-build' }
        Write-Heading 'Packing LeanCorpus packages'
        Invoke-DotNet $arguments

        $git = Get-ArtifactGitContext -RepoRoot $repoRoot
        $packages = @(Get-ChildItem $outputDirectory -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') } | Sort-Object Name)
        $entries = foreach ($package in $packages) {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($package.Name)
            if ($package.Name.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase)) {
                $name = [System.IO.Path]::GetFileNameWithoutExtension($name)
            }
            $match = [regex]::Match($name, '^(?<id>.+)\.(?<version>\d+\.\d+\.\d+(?:[-+].+)?)$')
            [ordered]@{
                packageId = if ($match.Success) { $match.Groups['id'].Value } else { $name }
                version = if ($match.Success) { $match.Groups['version'].Value } else { '' }
                path = $package.Name
                size = $package.Length
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash.ToLowerInvariant()
            }
        }
        Write-AtomicJsonFile -Path (Join-Path $outputDirectory 'manifest.json') -Value ([ordered]@{
            schemaVersion = 1
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            sourceCommit = $git.commit
            configuration = $configuration
            packages = @($entries)
        })
        Write-Success "Packages written to: $outputDirectory"
        return 0
    } catch {
        Write-Failure "Pack command failed: $($_.Exception.Message)"
        return 1
    }
}
