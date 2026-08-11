$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-DotNetTool {
    param(
        [string]$ToolName,
        [string]$PackageName
    )

    if (-not $PackageName) {
        $PackageName = $ToolName
    }

    if (Get-Command $ToolName -ErrorAction SilentlyContinue) {
        return
    }

    Write-Host "  Installing $PackageName..." -ForegroundColor DarkGray
    dotnet tool install -g $PackageName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install dotnet tool '$PackageName'."
    }
}
