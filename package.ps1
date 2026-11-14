$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $dist 'CDReady-1.0.0-win-x64'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
# Explicit allowlist: local configuration, audio, and logs cannot enter the release.
$names = @('CDReady.exe','CDReady.exe.config','README.md','使用说明.txt','local-ai.example.json')
foreach ($name in $names) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name) -Force
}
$zip = Join-Path $dist 'CDReady-1.0.0-win-x64.zip'
$releaseFiles = $names | ForEach-Object { Join-Path $stage $_ }
Compress-Archive -LiteralPath $releaseFiles -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  CDReady-1.0.0-win-x64.zip`n", [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $zip | Select-Object FullName,Length
