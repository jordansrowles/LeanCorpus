$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsData {
    param([string[]]$Arguments = @())

    $scriptsPath = Get-ScriptsPath
    $parsed = ConvertFrom-DevOpsArguments $Arguments

    $dataset = $parsed.Positionals[0]
    if (-not $dataset) {
        Write-Error "Usage: devops data <gutenberg|news|wikipedia> [options]"
        exit 1
    }

    $valid = @('gutenberg', 'news', 'wikipedia')
    if ($dataset -notin $valid) {
        Write-Error "Unknown dataset '$dataset'. Valid: $($valid -join ', ')"
        exit 1
    }

    $scriptName = "download-$dataset.ps1"
    $scriptPath = Join-Path $scriptsPath "data/$scriptName"
    if (-not (Test-Path $scriptPath)) {
        Write-Error "Script not found: $scriptPath"
        exit 1
    }

    Write-Heading "Downloading $dataset data..."
    if ($parsed.PassThrough.Count -gt 0) { & $scriptPath @($parsed.PassThrough) } else { & $scriptPath }
    exit $LASTEXITCODE
}
