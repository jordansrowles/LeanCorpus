[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectOutputDirectory,

    [string] $ExpectedImageVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$centralPackagesPath = Join-Path $repositoryRoot 'Directory.Packages.props'
if (-not (Test-Path -LiteralPath $centralPackagesPath -PathType Leaf)) {
    throw "Central package version file is missing: $centralPackagesPath"
}

[xml] $centralPackages = Get-Content -LiteralPath $centralPackagesPath -Raw
$packageVersionNode = $centralPackages.SelectSingleNode("//*[local-name()='PackageVersion' and @Include='Microsoft.Playwright']")
if ($null -eq $packageVersionNode -or [string]::IsNullOrWhiteSpace($packageVersionNode.GetAttribute('Version'))) {
    throw 'Directory.Packages.props does not define a pinned Microsoft.Playwright version.'
}
$expectedPackageVersion = $packageVersionNode.GetAttribute('Version')
if (-not [string]::IsNullOrWhiteSpace($ExpectedImageVersion) -and $ExpectedImageVersion -ne $expectedPackageVersion) {
    throw "Playwright image version '$ExpectedImageVersion' does not match Microsoft.Playwright '$expectedPackageVersion'."
}

$outputDirectory = if ([System.IO.Path]::IsPathRooted($ProjectOutputDirectory)) {
    [System.IO.Path]::GetFullPath($ProjectOutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ProjectOutputDirectory))
}
$assemblyPath = Join-Path $outputDirectory 'Microsoft.Playwright.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Microsoft.Playwright.dll is missing from the built target: $assemblyPath"
}

$dependencyManifests = @(Get-ChildItem -LiteralPath $outputDirectory -Filter '*.deps.json' -File)
$packageIdentity = "Microsoft.Playwright/$expectedPackageVersion"
$resolvedManifests = @(
    foreach ($manifest in $dependencyManifests) {
        $dependencyManifest = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
        $resolvedPackageIdentities = @($dependencyManifest.libraries.PSObject.Properties.Name)
        if ($packageIdentity -in $resolvedPackageIdentities) {
            $manifest.FullName
        }
    }
)
if ($resolvedManifests.Count -ne 1) {
    throw "Expected one built dependency manifest resolving '$packageIdentity' under '$outputDirectory', found $($resolvedManifests.Count)."
}

Add-Type -Path $assemblyPath

$playwright = $null
$browser = $null
try {
    $playwright = [Microsoft.Playwright.Playwright]::CreateAsync().GetAwaiter().GetResult()
    $executablePath = $playwright.Chromium.ExecutablePath
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "The Chromium executable required by Microsoft.Playwright $expectedPackageVersion is missing: $executablePath"
    }

    $launchOptions = [Microsoft.Playwright.BrowserTypeLaunchOptions]::new()
    $launchOptions.Headless = $true
    $browser = $playwright.Chromium.LaunchAsync($launchOptions).GetAwaiter().GetResult()
    Write-Output "Playwright package: $expectedPackageVersion"
    Write-Output "Chromium version: $($browser.Version)"
    Write-Output "Chromium executable: $executablePath"
}
catch {
    throw "Playwright Chromium preflight failed for '$outputDirectory': $($_.Exception.Message)"
}
finally {
    if ($null -ne $browser) {
        $browser.CloseAsync().GetAwaiter().GetResult()
        $browser.DisposeAsync().GetAwaiter().GetResult()
    }
    if ($null -ne $playwright) {
        $playwright.Dispose()
    }
}
