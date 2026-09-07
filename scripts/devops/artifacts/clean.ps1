$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Remove-OwnedArtifactPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactRoot
    )
    $root = [System.IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($resolved, $root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside the artefact root: $resolved"
    }
    if (Test-Path $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Info "  removed $([System.IO.Path]::GetRelativePath((Get-RepoRoot), $resolved))"
    }
}

function Invoke-ArtifactClean {
    param(
        [string]$Target = 'default',
        [string]$RepoRoot = (Get-RepoRoot)
    )
    $root = Get-ArtifactRoot -RepoRoot $RepoRoot
    $map = @{
        test = @('test')
        coverage = @('coverage')
        docs = @('docs')
        diagnostics = @('diagnostics')
        benchmark = @('benchmark/runs')
        package = @('package')
        cache = @('benchmark/cache')
        default = @('bin', 'obj', 'publish', 'test', 'coverage', 'diagnostics', 'docs', 'temp')
        all = @('')
    }
    $key = $Target.ToLowerInvariant()
    if (-not $map.ContainsKey($key)) {
        throw "Unknown clean target '$Target'. Valid: default, test, coverage, docs, diagnostics, benchmark, package, cache, all."
    }
    foreach ($relative in @($map[$key])) {
        $path = if ($relative) { Join-Path $root $relative } else { $root }
        Remove-OwnedArtifactPath -Path $path -ArtifactRoot $root
    }
}
