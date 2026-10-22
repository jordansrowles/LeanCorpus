$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot/scripts/devops/managed-host.ps1"

function Stop-Setup {
    param([Parameter(Mandatory = $true)][string]$Message)
    throw $Message
}

function Assert-LeanCorpusCheckout {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot)
    $gitMarker = Join-Path $root '.git'
    $isGitDirectory = Test-Path -LiteralPath $gitMarker -PathType Container
    $isGitFile = Test-Path -LiteralPath $gitMarker -PathType Leaf
    if ($isGitFile) {
        $firstLine = Get-Content -LiteralPath $gitMarker -TotalCount 1
        $isGitFile = $firstLine -match '^gitdir:\s*.+$'
    }
    if (-not ($isGitDirectory -or $isGitFile) -or
        -not (Test-Path -LiteralPath (Join-Path $root 'Rowles.LeanCorpus.slnx') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $root 'global.json') -PathType Leaf)) {
        Stop-Setup "'$root' is not a LeanCorpus checkout. Expected .git, Rowles.LeanCorpus.slnx and global.json at the repository root."
    }

    $gitRootLines = @(& $script:GitCommand.Source -C $root rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0 -or $gitRootLines.Count -eq 0) {
        Stop-Setup 'Git could not identify the setup script checkout root.'
    }
    $gitRoot = [System.IO.Path]::GetFullPath([string]$gitRootLines[-1])
    if (-not [string]::Equals([System.IO.Path]::TrimEndingDirectorySeparator($gitRoot), [System.IO.Path]::TrimEndingDirectorySeparator($root), (Get-ManagedHostComparison))) {
        Stop-Setup "The setup script directory '$root' does not match the Git checkout root '$gitRoot'."
    }
}

function Get-CurrentRuntimeIdentifier {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $platform = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        'win'
    } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        'linux'
    } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        'osx'
    } else {
        Stop-Setup 'The current operating system does not have a supported managed DevOps RID.'
    }

    if ($architecture -notin @('x64', 'arm64')) {
        Stop-Setup "The current architecture '$architecture' does not have a supported managed DevOps RID."
    }
    return "$platform-$architecture"
}

function Invoke-SetupCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Write-Host "$Description..."
    & $Executable @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Stop-Setup "$Description failed with exit code $exitCode. The previous managed apphost was left in place."
    }
}

function Invoke-ManagedHostProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$ExpectedText
    )

    Write-Host "$Description..."
    $lines = @(& $Executable @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $lines) { Write-Host $line }
    if ($exitCode -ne 0) {
        Stop-Setup "$Description failed with exit code $exitCode."
    }
    if ($ExpectedText -and -not (($lines -join [Environment]::NewLine).Contains($ExpectedText, [StringComparison]::OrdinalIgnoreCase))) {
        Stop-Setup "$Description returned successfully but did not include the expected response '$ExpectedText'."
    }
}

function Set-ManagedHostExecutableMode {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not $IsWindows) {
        $mode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::UserExecute -bor
            [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::GroupExecute -bor
            [System.IO.UnixFileMode]::OtherRead -bor [System.IO.UnixFileMode]::OtherExecute
        [System.IO.File]::SetUnixFileMode($Path, $mode)
    }
}

function Test-ProcessMarkerLive {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $marker = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        $processId = [int]$marker.processId
        $startedUtc = [DateTimeOffset]::Parse([string]$marker.processStartedUtc).UtcDateTime
    } catch {
        return $true
    }

    try {
        $process = Get-Process -Id $processId -ErrorAction Stop
        return [Math]::Abs(($process.StartTime.ToUniversalTime() - $startedUtc).TotalSeconds) -lt 2
    } catch {
        return $false
    }
}

function Enter-ManagedHostPromotionLease {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $leaseDirectory = Join-Path $RepositoryRoot 'artifacts/devops/leases'
    [System.IO.Directory]::CreateDirectory($leaseDirectory) | Out-Null
    $promotionPath = Join-Path $leaseDirectory 'promotion.lock'
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timeout = [TimeSpan]::FromMinutes(30)
    $ownsPromotionGate = $false

    try {
        while ($stopwatch.Elapsed -lt $timeout) {
            if ((Test-Path -LiteralPath $promotionPath -PathType Leaf) -and -not (Test-ProcessMarkerLive $promotionPath)) {
                Remove-Item -LiteralPath $promotionPath -Force
            }
            foreach ($readerPath in Get-ChildItem -LiteralPath $leaseDirectory -Filter 'reader-*.json' -File -ErrorAction SilentlyContinue) {
                if (-not (Test-ProcessMarkerLive $readerPath.FullName)) {
                    Remove-Item -LiteralPath $readerPath.FullName -Force -ErrorAction SilentlyContinue
                }
            }

            if (-not $ownsPromotionGate) {
                $process = [System.Diagnostics.Process]::GetCurrentProcess()
                $marker = [pscustomobject]@{ processId = $process.Id; processStartedUtc = $process.StartTime.ToUniversalTime() }
                $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $marker -Compress))
                try {
                    $stream = [System.IO.FileStream]::new(
                        $promotionPath,
                        [System.IO.FileMode]::CreateNew,
                        [System.IO.FileAccess]::Write,
                        [System.IO.FileShare]::None,
                        1024,
                        [System.IO.FileOptions]::WriteThrough)
                    try {
                        $stream.Write($bytes, 0, $bytes.Length)
                        $stream.Flush($true)
                    } finally {
                        $stream.Dispose()
                    }
                    $ownsPromotionGate = $true
                } catch [System.IO.IOException] {
                    Start-Sleep -Milliseconds 100
                    continue
                }
            }

            $liveReaders = @(
                Get-ChildItem -LiteralPath $leaseDirectory -Filter 'reader-*.json' -File -ErrorAction SilentlyContinue |
                    Where-Object { Test-ProcessMarkerLive $_.FullName }
            )
            if ($liveReaders.Count -eq 0) {
                return $promotionPath
            }
            Start-Sleep -Milliseconds 100
        }

        Stop-Setup "Could not acquire the exclusive apphost promotion lease within $timeout."
    } catch {
        if ($ownsPromotionGate -and (Test-Path -LiteralPath $promotionPath -PathType Leaf) -and (Test-ProcessMarkerLive $promotionPath)) {
            Remove-Item -LiteralPath $promotionPath -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Test-PublishedHostDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDirectory,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutable
    )

    if (-not (Test-Path -LiteralPath $ExpectedExecutable -PathType Leaf)) {
        Stop-Setup "The single-file publish did not produce the expected apphost '$ExpectedExecutable'."
    }
    $unexpected = @(Get-ChildItem -LiteralPath $PublishDirectory -Force | Where-Object { -not [string]::Equals($_.FullName, $ExpectedExecutable, (Get-ManagedHostComparison)) })
    if ($unexpected.Count -gt 0) {
        $names = ($unexpected | ForEach-Object { $_.Name }) -join ', '
        Stop-Setup "The managed host publish produced unexpected sidecars: $names"
    }
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Stop-Setup 'LeanCorpus setup requires PowerShell 7. Run it with `pwsh ./setup.ps1`.'
}

$script:GitCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $script:GitCommand) {
    Stop-Setup 'Git was not found. Install Git, then run `pwsh ./setup.ps1` again.'
}
$gitVersion = @(& $script:GitCommand.Source --version 2>&1)
if ($LASTEXITCODE -ne 0 -or $gitVersion.Count -eq 0) {
    Stop-Setup 'Git was found but could not run. Repair the Git installation, then run `pwsh ./setup.ps1` again.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
Assert-LeanCorpusCheckout -RepositoryRoot $repositoryRoot

$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $dotnetCommand) {
    Stop-Setup '.NET was not found. Install the SDK family required by global.json, then run `pwsh ./setup.ps1` again.'
}

$userDotnetRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.dotnet'
$userDotnetFileName = if ($IsWindows) { 'dotnet.exe' } else { 'dotnet' }
$userDotnetExecutable = Join-Path $userDotnetRoot $userDotnetFileName
if (-not $IsWindows -and (Test-Path -LiteralPath $userDotnetExecutable -PathType Leaf)) {
    $env:DOTNET_ROOT = $userDotnetRoot
    $env:DOTNET_ROOT_X64 = $userDotnetRoot
    $env:DOTNET_HOST_PATH = $userDotnetExecutable
    $env:PATH = $userDotnetRoot + [System.IO.Path]::PathSeparator + (Join-Path $userDotnetRoot 'tools') + [System.IO.Path]::PathSeparator + $env:PATH
    $dotnetCommand = Get-Command dotnet -CommandType Application | Select-Object -First 1
}

$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$globalConfig = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$requiredSdk = [string]$globalConfig.sdk.version
$requiredParts = @($requiredSdk -split '\.')
if ($requiredParts.Count -lt 2) {
    Stop-Setup "global.json contains an invalid SDK version '$requiredSdk'."
}
$sdkFamily = "$($requiredParts[0]).$($requiredParts[1])"

$installedSdks = @(& $dotnetCommand.Source --list-sdks 2>&1)
if ($LASTEXITCODE -ne 0) {
    Stop-Setup 'The .NET CLI could not list installed SDKs. Repair the .NET SDK installation and retry.'
}
$sdkFamilyPattern = [regex]::Escape($sdkFamily)
$matchingSdk = @($installedSdks | Where-Object { $_ -match "^\s*$sdkFamilyPattern\." })
if ($matchingSdk.Count -eq 0) {
    Stop-Setup "The .NET $sdkFamily SDK required by global.json was not found. Install that SDK family, then run `pwsh ./setup.ps1` again."
}

$selectedSdk = @(& $dotnetCommand.Source --version 2>&1)
if ($LASTEXITCODE -ne 0 -or $selectedSdk.Count -eq 0 -or $selectedSdk[0] -notmatch "^$sdkFamilyPattern\.") {
    Stop-Setup "The .NET SDK selected by global.json is not from the required $sdkFamily family. Install a compatible SDK and check global.json rollForward settings."
}

$manifestPath = Join-Path $repositoryRoot '.config/dotnet-tools.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Stop-Setup "The repository tool manifest was not found: $manifestPath"
}

$testProject = Join-Path $repositoryRoot 'src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj'
$hostProject = Join-Path $repositoryRoot 'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj'
$requiredBootstrapInputs = @(
    'Directory.Build.props',
    'Directory.Packages.props',
    'src/devops/Directory.Build.props',
    'src/devops/Directory.Packages.props',
    'src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj',
    'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj',
    '.config/devops/benchmark-datasets.json',
    '.config/devops/benchmark-groups.json',
    '.config/devops/benchmark-strategies.json',
    '.config/devops/benchmark-suites.json',
    '.config/devops/code-areas.json',
    '.config/devops/test-suites.json'
)
foreach ($relativePath in $requiredBootstrapInputs) {
    $inputPath = Join-Path $repositoryRoot ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        Stop-Setup "A required bootstrap input was not found: $relativePath"
    }
}

$rid = Get-CurrentRuntimeIdentifier
$testApphostName = if ($IsWindows) { 'Rowles.LeanCorpus.DevOps.Tests.exe' } else { 'Rowles.LeanCorpus.DevOps.Tests' }
$testApphost = Join-Path $repositoryRoot (Join-Path 'artifacts/devops/bin/Rowles.LeanCorpus.DevOps.Tests/release' $testApphostName)
$outputRoot = Join-Path $repositoryRoot 'artifacts/devops'
$stagingRoot = Join-Path $outputRoot 'bootstrap-staging'
$stageDirectory = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('D'))
$publishDirectory = Join-Path $stageDirectory 'publish'
$stagedExecutable = Join-Path $stageDirectory (Get-ManagedHostExecutableName)
$publishedHostName = if ($IsWindows) { 'leancorpus-devops.exe' } else { 'leancorpus-devops' }
$publishedHost = Join-Path $publishDirectory $publishedHostName
$rootExecutable = Join-Path $repositoryRoot (Get-ManagedHostExecutableName)
$rootManifest = Join-Path $outputRoot 'managed-host.json'
$leasePath = $null
$hadPreviousExecutable = Test-Path -LiteralPath $rootExecutable -PathType Leaf
$oldExecutable = Join-Path $stageDirectory 'previous-managed-host'
$oldManifest = Join-Path $stageDirectory 'previous-managed-host.json'
$hadPreviousManifest = Test-Path -LiteralPath $rootManifest -PathType Leaf

Push-Location $repositoryRoot
try {
    Write-Host "Validated PowerShell $($PSVersionTable.PSVersion), $($gitVersion[0]), SDK $($selectedSdk[0]) and runtime identifier $rid."
    Write-Host 'Restoring repository-pinned .NET tools...'
    & $dotnetCommand.Source tool restore
    if ($LASTEXITCODE -ne 0) {
        Stop-Setup 'Repository tool restore failed. Check NuGet connectivity and the local tool manifest, then retry setup.'
    }

    Invoke-SetupCommand $dotnetCommand.Source @('restore', $testProject, '--locked-mode', '--disable-build-servers') 'Restoring the managed DevOps test project graph in locked mode'
    Invoke-SetupCommand $dotnetCommand.Source @('restore', $hostProject, '--locked-mode', '--disable-build-servers') 'Verifying the managed host multi-RID lock in locked mode'
    Invoke-SetupCommand $dotnetCommand.Source @('build', $testProject, '--configuration', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '--tl:off') 'Building the managed DevOps test project graph'

    if (-not (Test-Path -LiteralPath $testApphost -PathType Leaf)) {
        Stop-Setup "The managed DevOps test apphost was not produced: $testApphost"
    }
    $previousTestingTelemetry = $env:TESTINGPLATFORM_TELEMETRY_OPTOUT
    try {
        $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
        Invoke-SetupCommand $testApphost @() 'Running the managed DevOps test suite before publish'
    } finally {
        $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = $previousTestingTelemetry
    }

    [System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null
    $publishArguments = @(
        'publish', $hostProject, '--configuration', 'Release', '--framework', 'net10.0', '--runtime', $rid,
        '--output', $publishDirectory, '--no-restore', '--self-contained', 'false',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishAot=false', '-p:PublishTrimmed=false', '-p:DebugSymbols=false', '-p:DebugType=None',
        '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '--tl:off'
    )
    Invoke-SetupCommand $dotnetCommand.Source $publishArguments "Publishing the managed DevOps host for $rid"
    Test-PublishedHostDirectory -PublishDirectory $publishDirectory -ExpectedExecutable $publishedHost
    [System.IO.File]::Move($publishedHost, $stagedExecutable)
    Set-ManagedHostExecutableMode -Path $stagedExecutable

    $stageManifest = Join-Path $stageDirectory 'managed-host.json'
    Write-ManagedHostManifest -RepositoryRoot $repositoryRoot -ExecutablePath $stagedExecutable -ManifestPath $stageManifest
    $stageStatus = Test-ManagedHost -RepositoryRoot $repositoryRoot -ExecutablePath $stagedExecutable
    if (-not $stageStatus.IsCurrent) {
        Stop-Setup "Staged apphost fingerprint validation failed: $($stageStatus.Reason)"
    }

    Invoke-ManagedHostProbe $stagedExecutable @('--help') 'Staged managed host --help probe' 'LeanCorpus managed DevOps'
    Invoke-ManagedHostProbe $stagedExecutable @('--version') 'Staged managed host --version probe'
    Invoke-ManagedHostProbe $stagedExecutable @('--repo', $repositoryRoot, 'build', '--project', 'src/devops/Rowles.LeanCorpus.DevOps.ProcessTestChild/Rowles.LeanCorpus.DevOps.ProcessTestChild.csproj', '--framework', 'net10.0') 'Staged managed build smoke'

    $currentStageStatus = Test-ManagedHost -RepositoryRoot $repositoryRoot -ExecutablePath $stagedExecutable
    if (-not $currentStageStatus.IsCurrent) {
        Stop-Setup "Source changed during staged verification: $($currentStageStatus.Reason)"
    }

    $leasePath = Enter-ManagedHostPromotionLease -RepositoryRoot $repositoryRoot
    if ($hadPreviousManifest) {
        [System.IO.File]::Copy($rootManifest, $oldManifest, $true)
    }
    if ($hadPreviousExecutable) {
        [System.IO.File]::Replace($stagedExecutable, $rootExecutable, $oldExecutable, $true)
    } else {
        [System.IO.File]::Move($stagedExecutable, $rootExecutable)
    }
    Set-ManagedHostExecutableMode -Path $rootExecutable

    try {
        Write-ManagedHostManifest -RepositoryRoot $repositoryRoot -ExecutablePath $rootExecutable -ManifestPath $rootManifest
        $rootStatus = Test-ManagedHost -RepositoryRoot $repositoryRoot -ExecutablePath $rootExecutable
        if (-not $rootStatus.IsCurrent) {
            Stop-Setup "Promoted apphost fingerprint validation failed: $($rootStatus.Reason)"
        }
        Invoke-ManagedHostProbe $rootExecutable @('--help') 'Promoted managed host --help probe' 'LeanCorpus managed DevOps'
        Invoke-ManagedHostProbe $rootExecutable @('--version') 'Promoted managed host --version probe'
    } catch {
        if (Test-Path -LiteralPath $oldExecutable -PathType Leaf) {
            [System.IO.File]::Move($oldExecutable, $rootExecutable, $true)
        } elseif (-not $hadPreviousExecutable) {
            Remove-Item -LiteralPath $rootExecutable -Force -ErrorAction SilentlyContinue
        }
        if ($hadPreviousManifest -and (Test-Path -LiteralPath $oldManifest -PathType Leaf)) {
            [System.IO.File]::Move($oldManifest, $rootManifest, $true)
        } elseif (-not $hadPreviousManifest) {
            Remove-Item -LiteralPath $rootManifest -Force -ErrorAction SilentlyContinue
        }
        throw
    }

    Remove-Item -LiteralPath $oldExecutable, $oldManifest -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    Write-Host "LeanCorpus managed DevOps setup completed for $rid."
} finally {
    if ($leasePath -and (Test-Path -LiteralPath $leasePath -PathType Leaf) -and (Test-ProcessMarkerLive $leasePath)) {
        Remove-Item -LiteralPath $leasePath -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
}
