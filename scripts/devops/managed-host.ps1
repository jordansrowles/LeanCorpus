$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-ManagedHostComparison {
    if ($IsWindows) { return [System.StringComparison]::OrdinalIgnoreCase }
    return [System.StringComparison]::Ordinal
}

function Get-ManagedHostExecutableName {
    if ($IsWindows) { return 'devops.exe' }
    return 'devops'
}

function Get-ManagedHostSourceEntries {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot)
    $paths = [System.Collections.Generic.List[string]]::new()
    $requiredFiles = @(
        'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
        'src/devops/Directory.Build.props', 'src/devops/Directory.Packages.props', 'src/devops/Rowles.LeanCorpus.DevOps.slnx'
    )
    foreach ($relative in $requiredFiles) {
        $path = Join-Path $root ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $attributes = [System.IO.File]::GetAttributes($path)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Managed DevOps source input '$path' is a link or reparse point."
            }
            $paths.Add($relative)
        }
    }

    $extensions = @('.cs', '.csproj', '.json', '.props', '.targets', '.slnx')
    foreach ($relativeDirectory in @('src/devops/Rowles.LeanCorpus.DevOps', 'config/devops')) {
        $directory = Join-Path $root ($relativeDirectory.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        $pending = [System.Collections.Generic.Stack[string]]::new()
        $pending.Push($directory)
        while ($pending.Count -gt 0) {
            $current = $pending.Pop()
            foreach ($item in Get-ChildItem -LiteralPath $current -Force) {
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Managed DevOps source input '$($item.FullName)' is a link or reparse point."
                }
                if ($item.PSIsContainer) {
                    $pending.Push($item.FullName)
                    continue
                }
                if ($extensions -contains $item.Extension.ToLowerInvariant()) {
                    $paths.Add([System.IO.Path]::GetRelativePath($root, $item.FullName).Replace([System.IO.Path]::DirectorySeparatorChar, '/'))
                }
            }
        }
    }

    $paths.Sort([System.StringComparer]::Ordinal)
    $entries = [System.Collections.Generic.List[object]]::new()
    $canonical = [System.Text.StringBuilder]::new()
    foreach ($relative in $paths) {
        $path = Join-Path $root ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$entries.Add([pscustomobject]@{ path = $relative; sha256 = $hash })
        [void]$canonical.Append($relative).Append([char]0).Append($hash).Append([char]10)
    }

    if (-not ($paths -contains 'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj')) {
        throw 'The managed DevOps host project is missing from the selected checkout.'
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $sourceFingerprint = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($utf8.GetBytes($canonical.ToString()))).ToLowerInvariant()
    return [pscustomobject]@{ sourceFingerprint = $sourceFingerprint; sourceFiles = @($entries.ToArray()) }
}

function Get-ManagedHostManifestPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ExecutablePath
    )

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot)
    $executable = [System.IO.Path]::GetFullPath($ExecutablePath)
    $rootExecutable = Join-Path $root (Get-ManagedHostExecutableName)
    if ([string]::Equals($executable, $rootExecutable, (Get-ManagedHostComparison))) {
        return Join-Path $root 'artifacts/devops/managed-host.json'
    }

    $stageRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/devops/bootstrap-staging'))
    $stagePrefix = [System.IO.Path]::TrimEndingDirectorySeparator($stageRoot) + [System.IO.Path]::DirectorySeparatorChar
    if ($executable.StartsWith($stagePrefix, (Get-ManagedHostComparison))) {
        return Join-Path ([System.IO.Path]::GetDirectoryName($executable)) 'managed-host.json'
    }

    return $null
}

function Write-ManagedHostManifest {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $source = Get-ManagedHostSourceEntries -RepositoryRoot $RepositoryRoot
    $exeHash = (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [pscustomobject]@{
        schemaVersion = 1
        executableSha256 = $exeHash
        sourceFingerprint = $source.sourceFingerprint
        sourceFiles = @($source.sourceFiles)
    }
    $fullManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
    $directory = [System.IO.Path]::GetDirectoryName($fullManifestPath)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = "$fullManifestPath.$PID.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = ConvertTo-Json -InputObject $manifest -Depth 8
        [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $fullManifestPath, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Test-ManagedHost {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ExecutablePath
    )

    $fullExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $fullExecutable -PathType Leaf)) {
        return [pscustomobject]@{ IsCurrent = $false; Reason = "Managed DevOps apphost is missing: $fullExecutable" }
    }

    $manifestPath = Get-ManagedHostManifestPath -RepositoryRoot $RepositoryRoot -ExecutablePath $fullExecutable
    if ($null -eq $manifestPath -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return [pscustomobject]@{ IsCurrent = $false; Reason = 'Managed DevOps promotion manifest is missing. Run pwsh ./setup.ps1.' }
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -ne 1) {
            return [pscustomobject]@{ IsCurrent = $false; Reason = 'Managed DevOps promotion manifest has an unsupported schema.' }
        }

        $current = Get-ManagedHostSourceEntries -RepositoryRoot $RepositoryRoot
        if (-not [string]::Equals([string]$manifest.sourceFingerprint, $current.sourceFingerprint, [System.StringComparison]::Ordinal)) {
            return [pscustomobject]@{ IsCurrent = $false; Reason = 'Managed DevOps apphost is stale because its source inputs changed. Run pwsh ./setup.ps1.' }
        }

        $recorded = @($manifest.sourceFiles)
        if ($recorded.Count -ne $current.sourceFiles.Count) {
            return [pscustomobject]@{ IsCurrent = $false; Reason = 'Managed DevOps apphost source file list is stale. Run pwsh ./setup.ps1.' }
        }
        for ($index = 0; $index -lt $recorded.Count; $index++) {
            if (-not [string]::Equals([string]$recorded[$index].path, [string]$current.sourceFiles[$index].path, [System.StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$recorded[$index].sha256, [string]$current.sourceFiles[$index].sha256, [System.StringComparison]::Ordinal)) {
                return [pscustomobject]@{ IsCurrent = $false; Reason = 'Managed DevOps apphost source inputs do not match its promotion manifest. Run pwsh ./setup.ps1.' }
            }
        }

        $actualExeHash = (Get-FileHash -LiteralPath $fullExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [string]::Equals([string]$manifest.executableSha256, $actualExeHash, [System.StringComparison]::Ordinal)) {
            return [pscustomobject]@{ IsCurrent = $false; Reason = 'Managed DevOps executable does not match its promotion manifest. Run pwsh ./setup.ps1.' }
        }

        return [pscustomobject]@{ IsCurrent = $true; Reason = $null }
    } catch {
        return [pscustomobject]@{ IsCurrent = $false; Reason = "Could not validate managed DevOps promotion manifest: $($_.Exception.Message)" }
    }
}
