$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $PSScriptRoot 'EngineTests.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /langversion:5 "/out:$testExe" "/reference:$PSScriptRoot\CDReady.exe" /reference:System.Core.dll (Join-Path $PSScriptRoot 'validation\EngineTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
$runDir = Join-Path $PSScriptRoot ('validation\fixtures-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $testExe $runDir
if ($LASTEXITCODE -ne 0) { throw "Tests failed: $runDir" }
