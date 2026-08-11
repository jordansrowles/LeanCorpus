$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Heading {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Text)
    Write-Host $Text -ForegroundColor DarkGray
}

function Write-Success {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Green
}

function Write-Failure {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Red
}

function Write-Warn {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Yellow
}
