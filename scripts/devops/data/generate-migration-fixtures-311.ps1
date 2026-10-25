param([string]$PackageSource = 'https://api.nuget.org/v3/index.json')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runRoot = Join-Path $repoRoot ('artifacts/temp/migration-fixtures-311/' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $runRoot 'generator'
$packageRoot = Join-Path $runRoot 'packages'
$outputRoot = Join-Path $runRoot 'output'
[void][IO.Directory]::CreateDirectory($projectRoot)
[void][IO.Directory]::CreateDirectory($outputRoot)
Copy-Item (Join-Path $PSScriptRoot 'fixtures-311/Generator.csproj.template') (Join-Path $projectRoot 'Generator.csproj')
Copy-Item (Join-Path $PSScriptRoot 'fixtures-311/Program.cs.template') (Join-Path $projectRoot 'Program.cs')
Copy-Item (Join-Path $PSScriptRoot 'fixtures-311/global.json.template') (Join-Path $projectRoot 'global.json')
$project = Join-Path $projectRoot 'Generator.csproj'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$escapedSource = [Security.SecurityElement]::Escape($PackageSource)
[IO.File]::WriteAllText($nugetConfig, "<configuration><packageSources><clear/><add key=`"pinned-release`" value=`"$escapedSource`"/></packageSources></configuration>")

$package = Join-Path $packageRoot 'leancorpus/3.1.1/leancorpus.3.1.1.nupkg'
Push-Location $projectRoot
try {
    & dotnet restore $project --packages $packageRoot --configfile $nugetConfig --disable-build-servers -p:NuGetAudit=false | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet run --project $project --configuration Release --no-restore -- $outputRoot $package | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally {
    Pop-Location
}

# The archive contains exactly the manifest's physical files. Stable gzip/tar
# metadata avoids introducing host timestamps into the archive wrapper.
$fixtureRoot = Join-Path $repoRoot 'src/devops/tests/Rowles.LeanCorpus.Tests.Shared/Fixtures/Indexes'
$encoder = @'
import base64, gzip, hashlib, io, json, pathlib, tarfile, sys
source, destination = map(pathlib.Path, sys.argv[1:])
for layout in ('loose', 'compound'):
    stem = '3.1.1-release-' + layout
    manifest = json.loads((source / (stem + '.manifest.json')).read_text())
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode='w', format=tarfile.USTAR_FORMAT) as archive:
        for item in manifest['Files']:
            path = source / layout / item['Name']
            entry = tarfile.TarInfo(item['Name'])
            entry.size = path.stat().st_size
            entry.mode = 0o644
            archive.addfile(entry, io.BytesIO(path.read_bytes()))
    data = gzip.compress(stream.getvalue(), mtime=0)
    manifest['ArchiveSha256'] = hashlib.sha256(data).hexdigest()
    (destination / (stem + '.fixture.b64')).write_text(base64.encodebytes(data).decode('ascii'))
    (destination / (stem + '.manifest.json')).write_text(json.dumps(manifest, indent=2, ensure_ascii=True) + '\n')
    print(stem + ': ' + manifest['ArchiveSha256'])
'@
& python3 -c $encoder $outputRoot $fixtureRoot | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Frozen fixtures and manifests written to $fixtureRoot. Inspect and commit them deliberately."
exit 0
