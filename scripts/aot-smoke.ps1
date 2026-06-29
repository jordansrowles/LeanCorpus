param(
    [string]$RuntimeIdentifier
)

if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = if ($IsLinux) { "linux-x64" } elseif ($IsMacOS) { "osx-x64" } else { "win-x64" }
}

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\examples\Rowles.LeanCorpus.Example.NativeAot\Rowles.LeanCorpus.Example.NativeAot.csproj"

dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true -p:PublishAot=true

$publishDirectory = Join-Path $repoRoot "src\examples\Rowles.LeanCorpus.Example.NativeAot\bin\Release\net10.0\$RuntimeIdentifier\publish"
$executable = if ($RuntimeIdentifier.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
    Join-Path $publishDirectory "Rowles.LeanCorpus.Example.NativeAot.exe"
} else {
    Join-Path $publishDirectory "Rowles.LeanCorpus.Example.NativeAot"
}

& $executable
if ($LASTEXITCODE -ne 0) {
    throw "Native AOT smoke executable failed with exit code $LASTEXITCODE."
}
