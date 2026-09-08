$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-ArtifactJson {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 30)
}

function Write-AtomicTextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        [void][System.IO.Directory]::CreateDirectory($parent)
    }

    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText($temporaryPath, $Content, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    } catch {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
        throw
    }
}

function Write-AtomicJsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )
    Write-AtomicTextFile -Path $Path -Content (ConvertTo-ArtifactJson $Value)
}

function Update-ArtifactRunManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,
        [Parameter(Mandatory = $true)]
        [hashtable]$Values
    )

    $path = Join-Path $RunDirectory 'run.json'
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Run manifest not found: $path"
    }

    $manifest = Get-Content -Raw $path | ConvertFrom-Json -AsHashtable
    foreach ($key in $Values.Keys) {
        $manifest[$key] = $Values[$key]
    }
    Write-AtomicJsonFile -Path $path -Value $manifest
}
