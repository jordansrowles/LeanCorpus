$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot/dependencies.ps1"
. "$PSScriptRoot/components/commands.ps1"
. "$PSScriptRoot/pages/tests.ps1"
. "$PSScriptRoot/pages/benchmarks.ps1"
. "$PSScriptRoot/pages/observe.ps1"
. "$PSScriptRoot/pages/dataforge.ps1"
. "$PSScriptRoot/pages/surface.ps1"

function Invoke-DevOpsUi {
    param([string[]]$Arguments = @())

    if ($null -eq $Arguments) { $Arguments = @() }
    else { $Arguments = @($Arguments) }
    if ($Arguments.Count -gt 0 -and $Arguments[0] -in @('-h', '--help', '-Help', '--Help')) {
        Write-Host "Interactive DevOps workbench. Requires PowerShell 7.4+ and PwshSpectreConsole $(Get-DevOpsUiDependencyVersion)."
        Write-Host "Install the pinned dependency with: pwsh ./devops.ps1 setup -InstallUi"
        return 0
    }
    if ($Arguments.Count -gt 0) {
        Write-Failure "The UI command does not accept arguments: $($Arguments -join ' ')"
        return 2
    }
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        Write-Failure 'The DevOps UI requires an interactive terminal. Use ./devops for build, clean and server, or pwsh ./devops.ps1 for other commands.'
        return 2
    }

    try {
        Import-DevOpsUiDependency | Out-Null
        Set-SpectreColors -AccentColor Cyan1 -DefaultValueColor Grey -DefaultTableHeaderColor Cyan1
        return Invoke-DevOpsUiHome
    } catch {
        $message = $_.Exception.Message
        if ($message -match 'not installed|Install-Module|PowerShell 7\.4') {
            Write-Failure "Missing UI dependency or runtime: $message"
        } elseif ($_.Exception -is [System.Management.Automation.PipelineStoppedException] -or
            $_.Exception -is [OperationCanceledException]) {
            Write-Warn 'The interactive operation was cancelled. Existing DevOps artefacts were retained.'
            return 130
        } else {
            Write-Failure "Unexpected UI exception: $message"
        }
        return 1
    }
}

function Invoke-DevOpsUiHome {
    $choices = @(
        [pscustomobject]@{ Key = 'Run & Validate'; Label = 'Run & Validate       Tests, coverage, AOT and benchmarks' },
        [pscustomobject]@{ Key = 'Observe'; Label = 'Observe              Runs, artefacts, diagnostics and repository health' },
        [pscustomobject]@{ Key = 'Data'; Label = 'Data                 DataForge profiles, datasets and migration fixtures' },
        [pscustomobject]@{ Key = 'Build & Distribution'; Label = 'Build & Distribution Builds, NuGet packages and documentation' },
        [pscustomobject]@{ Key = 'Server'; Label = 'Server               Start the Community Server' },
        [pscustomobject]@{ Key = 'Maintenance'; Label = 'Maintenance          Setup, dependencies and artefact cleanup' },
        [pscustomobject]@{ Key = 'Exit'; Label = 'Exit                 Return to your shell' }
    )

    while ($true) {
        Show-DevOpsUiHeader -Description 'Choose a workflow. Use arrow keys to move and Enter to select.'
        $category = Read-DevOpsUiSelection -Message 'Choose a workflow' -Choices $choices -ChoiceLabelProperty Label -PageSize 10
        if ($null -eq $category -or $category.Key -eq 'Exit') {
            return 0
        }
        Invoke-DevOpsUiCategory -Category ([string]$category.Key)
    }
}

function Invoke-DevOpsUiCategory {
    param([Parameter(Mandatory = $true)][string]$Category)

    switch ($Category) {
        'Run & Validate' { Invoke-DevOpsUiRunMenu }
        'Observe' { Invoke-DevOpsUiObserveMenu }
        'Data' { Invoke-DevOpsUiDataMenu }
        'Build & Distribution' { Invoke-DevOpsUiBuildMenu }
        'Server' { Invoke-DevOpsUiServerMenu }
        'Maintenance' { Invoke-DevOpsUiMaintenanceMenu }
    }
}
