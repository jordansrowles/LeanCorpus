$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsClean {
    param([string[]]$Arguments = @())
    try {
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $target = if ($parsed.Positionals.Count -gt 0) { [string]$parsed.Positionals[0] } else { 'default' }
        Write-Heading "Cleaning LeanCorpus artefacts: $target"
        Invoke-ArtifactClean -Target $target
        Write-Success 'Artefact cleaning completed.'
        return 0
    } catch {
        Write-Failure "Clean command failed: $($_.Exception.Message)"
        return 1
    }
}
