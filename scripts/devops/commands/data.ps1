$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-GutenbergDownloadArguments {
    param([object]$BookCount = 200)

    $parsedCount = 0
    if (-not [int]::TryParse([string]$BookCount, [ref]$parsedCount) -or $parsedCount -lt 1) {
        throw 'Gutenberg -BookCount must be a positive integer.'
    }

    return @('-BookCount', $parsedCount.ToString([Globalization.CultureInfo]::InvariantCulture))
}

function Invoke-DevOpsData {
    param([string[]]$Arguments = @())

    $scriptsPath = Get-ScriptsPath
    $parsed = ConvertFrom-DevOpsArguments $Arguments

    $dataset = $parsed.Positionals[0]
    if (-not $dataset) {
        Write-Error "Usage: pwsh ./devops.ps1 data gutenberg [options]"
        exit 1
    }

    if ($dataset -eq 'migration-fixtures-311') {
        $scriptPath = Join-Path $scriptsPath 'devops/data/generate-migration-fixtures-311.ps1'
        & $scriptPath -PackageSource ([string]$parsed.Get('PackageSource', 'https://api.nuget.org/v3/index.json'))
        return $LASTEXITCODE
    }

    $valid = @('gutenberg', 'migration-fixtures-311')
    if ($dataset -notin $valid) {
        Write-Error "Unknown dataset '$dataset'. Valid: $($valid -join ', ')"
        exit 1
    }

    $scriptName = 'download-gutenberg.ps1'
    $scriptPath = Join-Path $scriptsPath "data/$scriptName"
    if (-not (Test-Path $scriptPath)) {
        Write-Error "Script not found: $scriptPath"
        exit 1
    }

    Write-Heading "Downloading $dataset data..."
    $downloadArguments = @(Get-GutenbergDownloadArguments -BookCount ($parsed.Get('BookCount', '200')))
    if ($parsed.PassThrough.Count -gt 0) { $downloadArguments += @($parsed.PassThrough) }
    & $scriptPath @downloadArguments
    exit $LASTEXITCODE
}
