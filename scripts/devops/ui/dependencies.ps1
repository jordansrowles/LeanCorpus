$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:PwshSpectreConsoleVersion = '2.6.3'

function Get-DevOpsUiDependencyVersion {
    return $script:PwshSpectreConsoleVersion
}

function Test-DevOpsUiPowerShellVersion {
    $required = [version]'7.4'
    return $PSVersionTable.PSVersion -ge $required
}

function Install-DevOpsUiDependency {
    if (-not (Test-DevOpsUiPowerShellVersion)) {
        throw "PwshSpectreConsole $script:PwshSpectreConsoleVersion requires PowerShell 7.4 or newer."
    }

    $installed = Get-Module -ListAvailable -Name PwshSpectreConsole |
        Where-Object { $_.Version -eq [version]$script:PwshSpectreConsoleVersion } |
        Select-Object -First 1
    if ($null -ne $installed) {
        return $installed
    }

    $installer = Get-Command Install-Module -ErrorAction SilentlyContinue
    if ($null -eq $installer) {
        throw 'PowerShellGet is unavailable. Install the pinned module with Install-PSResource or install PowerShellGet.'
    }

    Install-Module -Name PwshSpectreConsole -RequiredVersion $script:PwshSpectreConsoleVersion `
        -Repository PSGallery -Scope CurrentUser -Force -Confirm:$false -ErrorAction Stop
    $installed = Get-Module -ListAvailable -Name PwshSpectreConsole |
        Where-Object { $_.Version -eq [version]$script:PwshSpectreConsoleVersion } |
        Select-Object -First 1
    if ($null -eq $installed) {
        throw "PwshSpectreConsole $script:PwshSpectreConsoleVersion was not found after installation."
    }
    return $installed
}

function Test-DevOpsUiDependency {
    if (-not (Test-DevOpsUiPowerShellVersion)) { return $false }
    return $null -ne (Get-Module -ListAvailable -Name PwshSpectreConsole |
        Where-Object { $_.Version -eq [version]$script:PwshSpectreConsoleVersion } |
        Select-Object -First 1)
}

function Import-DevOpsUiDependency {
    if (-not (Test-DevOpsUiPowerShellVersion)) {
        throw "The interactive UI requires PowerShell 7.4 or newer; found $($PSVersionTable.PSVersion)."
    }

    $version = [version]$script:PwshSpectreConsoleVersion
    $installed = Get-Module -ListAvailable -Name PwshSpectreConsole |
        Where-Object { $_.Version -eq $version } |
        Select-Object -First 1
    if ($null -eq $installed) {
        throw "PwshSpectreConsole $version is not installed. Run 'pwsh ./devops.ps1 setup -InstallUi' to install the pinned UI dependency."
    }

    Import-Module PwshSpectreConsole -RequiredVersion $version -ErrorAction Stop
}
