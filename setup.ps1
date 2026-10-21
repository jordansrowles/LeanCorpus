$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Stop-Setup {
    param([Parameter(Mandatory = $true)][string]$Message)
    throw $Message
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Stop-Setup 'LeanCorpus setup requires PowerShell 7. Run it with `pwsh ./setup.ps1`.'
}

$gitCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $gitCommand) {
    Stop-Setup 'Git was not found. Install Git, then run `pwsh ./setup.ps1` again.'
}
$gitVersion = @(& $gitCommand.Source --version 2>&1)
if ($LASTEXITCODE -ne 0 -or $gitVersion.Count -eq 0) {
    Stop-Setup 'Git was found but could not run. Repair the Git installation, then run `pwsh ./setup.ps1` again.'
}

$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $dotnetCommand) {
    Stop-Setup '.NET was not found. Install the SDK family required by global.json, then run `pwsh ./setup.ps1` again.'
}

$globalJsonPath = Join-Path $PSScriptRoot 'global.json'
if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
    Stop-Setup "Repository SDK configuration was not found: $globalJsonPath"
}

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

$manifestPath = Join-Path $PSScriptRoot '.config/dotnet-tools.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Stop-Setup "The repository tool manifest was not found: $manifestPath"
}

Push-Location $PSScriptRoot
try {
    Write-Host "Validated PowerShell $($PSVersionTable.PSVersion), $($gitVersion[0]), and .NET SDK $($selectedSdk[0]) (global.json family $sdkFamily)."
    Write-Host 'Restoring repository-pinned .NET tools...'
    & $dotnetCommand.Source tool restore
    if ($LASTEXITCODE -ne 0) {
        Stop-Setup 'Repository tool restore failed. Check NuGet connectivity and the local tool manifest, then run `pwsh ./setup.ps1` again.'
    }
    Write-Host 'LeanCorpus setup complete.' -ForegroundColor Green
} finally {
    Pop-Location
}
