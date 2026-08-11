$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DotNet {
    param(
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $argsLog = ($Arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '

    if ($WorkingDirectory) {
        Write-Host "  dotnet $argsLog" -ForegroundColor DarkGray
        Push-Location $WorkingDirectory
        try {
            dotnet @Arguments
        } finally {
            Pop-Location
        }
    } else {
        Write-Host "  dotnet $argsLog" -ForegroundColor DarkGray
        dotnet @Arguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exit code ${LASTEXITCODE}: $argsLog"
    }
}

function Invoke-ExternalCommand {
    param(
        [string]$Executable,
        [string[]]$Arguments
    )

    $argsLog = ($Arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    Write-Host "  $Executable $argsLog" -ForegroundColor DarkGray

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable exit code ${LASTEXITCODE}: $argsLog"
    }
}
