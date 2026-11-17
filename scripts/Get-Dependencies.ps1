# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = Split-Path $PSScriptRoot -Parent
$depsRoot = Join-Path $repoRoot '.deps'
$downloads = Join-Path $depsRoot 'downloads'
New-Item -ItemType Directory -Force $downloads | Out-Null

function Get-VerifiedArchive([string]$Name, [string]$Url, [string]$Sha256) {
    $archivePath = Join-Path $downloads $Name
    if (-not (Test-Path -LiteralPath $archivePath)) {
        Write-Host "Downloading $Name from its official release..."
        Invoke-WebRequest -Uri $Url -OutFile $archivePath
    }
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Checksum mismatch: $archivePath. Remove this cached file and try again."
    }
    return $archivePath
}

function Copy-ZipEntry([string]$ArchivePath, [string]$EntryName, [string]$Destination) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) { throw "Missing archive entry: $EntryName" }
        New-Item -ItemType Directory -Force (Split-Path $Destination -Parent) | Out-Null
        $sourceStream = $entry.Open()
        try {
            $destinationStream = [System.IO.File]::Create($Destination)
            try { $sourceStream.CopyTo($destinationStream) } finally { $destinationStream.Dispose() }
        } finally { $sourceStream.Dispose() }
    } finally { $archive.Dispose() }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$carabiner = Get-VerifiedArchive 'Carabiner_Win_x64-v1.2.0.zip' 'https://github.com/Deep-Symmetry/carabiner/releases/download/v1.2.0/Carabiner_Win_x64.zip' 'D0ACD5F739CBED3CFF9DDDD5E816761D4FEE5C9E8716A5F948FA4CE396C6157A'
Copy-ZipEntry $carabiner 'Carabiner.exe' (Join-Path $depsRoot 'carabiner/Carabiner.exe')
$sdk = Get-VerifiedArchive 'Windows.Devices.Midi2.0.99.92-preview.11.nupkg' 'https://github.com/microsoft/MIDI/releases/download/inbox-preview-11/Windows.Devices.Midi2.0.99.92-preview.11.nupkg' '13AFC6E6DA2ADAC309AB8C174352F46F1C0EBE2D79E65475BD6274DEFB73DF74'
Copy-ZipEntry $sdk 'ref/net10.0/Windows.Devices.Midi2.winmd' (Join-Path $depsRoot 'sdk/Windows.Devices.Midi2.winmd')
Copy-ZipEntry $sdk 'runtimes/win-x64/native/Windows.Devices.Midi2.dll' (Join-Path $depsRoot 'sdk/Windows.Devices.Midi2.dll')
Copy-ZipEntry $sdk 'runtimes/win-x64/native/Windows.Devices.Midi2.pri' (Join-Path $depsRoot 'sdk/Windows.Devices.Midi2.pri')
Write-Host 'Verified dependencies are ready.'
