param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'make-icon.ps1')
$out = Join-Path $root 'artifacts\standalone'
New-Item -ItemType Directory -Path $out -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $PSScriptRoot 'payload.zip'
if (Test-Path $payload) { Remove-Item $payload -Force }
$staging = Join-Path $out 'payload-staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item (Join-Path $root 'scripts') -Destination (Join-Path $staging 'scripts') -Recurse
Copy-Item (Join-Path $root 'resources') -Destination (Join-Path $staging 'resources') -Recurse
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $staging -Recurse -Force
dotnet publish (Join-Path $PSScriptRoot 'ClaudeZhStandalone.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:PublishDir="$out\" `
    -p:UseAppHost=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item (Join-Path $out 'ClaudeDesktop-ZH-CN.exe') | Select-Object FullName,Length
