# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$SkipDependencyDownload)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipDependencyDownload) { & (Join-Path $PSScriptRoot 'Get-Dependencies.ps1') }
$project = Join-Path $repoRoot 'src/LJLinkMidiClock.csproj'
$destination = Join-Path $repoRoot 'artifacts/win-x64'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None --configfile (Join-Path $repoRoot 'NuGet.Config') -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item (Join-Path $repoRoot 'LICENSE') -Destination $destination
Copy-Item (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $destination
Copy-Item (Join-Path $repoRoot 'README.md') -Destination $destination
Copy-Item (Join-Path $repoRoot 'licenses') -Destination $destination -Recurse -Force
Write-Host "Built: $destination/LJLinkMidiClock.exe"
