$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DevOpsConfigurationDocument {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('benchmark-projects', 'benchmark-suites', 'benchmark-strategies', 'benchmark-datasets', 'benchmark-groups', 'test-suites', 'code-areas')]
        [string]$ConfigurationName
    )

    $path = Join-Path (Get-RepoRoot) ".config/devops/$ConfigurationName.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Managed DevOps configuration file is missing: $path"
    }

    try {
        $document = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    }
    catch {
        throw "Managed DevOps configuration file is invalid: $path. $($_.Exception.Message)"
    }

    if ($null -eq $document -or $document.schemaVersion -ne 1 -or $null -eq $document['data']) {
        throw "Managed DevOps configuration file has an unsupported or incomplete schema: $path"
    }

    return $document
}

function Get-DevOpsConfigurationData {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('benchmark-projects', 'benchmark-suites', 'benchmark-strategies', 'benchmark-datasets', 'benchmark-groups', 'test-suites', 'code-areas')]
        [string]$ConfigurationName
    )

    $document = Get-DevOpsConfigurationDocument -ConfigurationName $ConfigurationName
    return ,$document.data
}
