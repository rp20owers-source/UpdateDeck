#Requires -Version 7.4
[CmdletBinding()]
param([string]$ReleaseDirectory=(Join-Path $PSScriptRoot '..\..\releases'),[string]$ReportPath=(Join-Path $PSScriptRoot '..\..\verification\release-validation.json'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath($ReleaseDirectory)
Add-Type -AssemblyName System.IO.Compression
. (Join-Path $PSScriptRoot 'Archive-Paths.ps1')
function Read-Bytes($entry){return ,(Read-ArchiveEntryBytes $entry 64MB)}
function Machine-Of([byte[]]$bytes){if($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes,0) -ne 0x5A4D){throw 'Truncated or invalid PE file'};$offset=[BitConverter]::ToInt32($bytes,60);if($offset -lt 64 -or $offset -gt $bytes.Length-6 -or [BitConverter]::ToUInt32($bytes,$offset) -ne 0x4550){throw 'Invalid PE signature'};return [BitConverter]::ToUInt16($bytes,$offset+4)}
function Setup-Machine([string]$path){$stream=[IO.File]::OpenRead($path);$reader=[IO.BinaryReader]::new($stream);try{if($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D){throw 'Invalid setup PE header'};$stream.Position=60;$offset=$reader.ReadInt32();if($offset -lt 64 -or $offset -gt $stream.Length-6){throw 'Invalid setup PE offset'};$stream.Position=$offset;if($reader.ReadUInt32() -ne 0x4550){throw 'Invalid setup PE signature'};return $reader.ReadUInt16()}finally{$reader.Dispose();$stream.Dispose()}}
if((Get-Item -LiteralPath (Join-Path $root 'release.json')).Length -gt 1MB -or (Get-Item -LiteralPath (Join-Path $root 'SHA256SUMS.txt')).Length -gt 64KB){throw 'Oversized release metadata'}
$metadata=ConvertFrom-UniqueJson ([IO.File]::ReadAllBytes((Join-Path $root 'release.json')))
if($metadata.product -ne 'UpdateDeck' -or $metadata.version -ne '2.0.1' -or $metadata.runtime -ne '10.0.15'){throw 'Unexpected release identity or runtime'}
if(($metadata.schema -isnot [long] -and $metadata.schema -isnot [int]) -or ($metadata.minimumWindowsBuild -isnot [long] -and $metadata.minimumWindowsBuild -isnot [int]) -or $metadata.schema -ne 1 -or $metadata.minimumWindowsBuild -ne 19041 -or $metadata.sourceTreeSha256 -isnot [string] -or $metadata.sourceTreeSha256 -notmatch '^[a-fA-F0-9]{64}$' -or $metadata.selfContained -isnot [bool] -or -not $metadata.selfContained -or $metadata.signed -isnot [bool] -or $metadata.buildId -isnot [string] -or $metadata.buildId -notmatch '^\d{8}-\d{6}-[a-f0-9]{8}$'){throw 'Invalid release compatibility, source fingerprint, build or signing metadata'}
if(@(Compare-Object @($metadata.architectures | Sort-Object) @('arm64','x64')).Count -ne 0){throw 'Unexpected architecture list'}
if($metadata.signed -and $metadata.signingThumbprint -notmatch '^[a-fA-F0-9]{40}$'){throw 'A signed release must identify its exact signing certificate'}
$required=@('UpdateDeck-2.0.1-win-x64-Setup.exe','UpdateDeck-2.0.1-win-arm64-Setup.exe','UpdateDeck-2.0.1-win-x64-Portable.zip','UpdateDeck-2.0.1-win-arm64-Portable.zip')
if(@(Compare-Object @($metadata.files | Sort-Object) @($required | Sort-Object)).Count -ne 0){throw 'Release manifest file list is incomplete or unexpected'}
$checks=[Collections.Generic.List[object]]::new()
$payloads=[Collections.Generic.List[object]]::new()
$hashed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($line in Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt')){
    if($line.Trim().Length -eq 0){continue}
    $hash,$name=$line -split '  ',2
    if($hash -notmatch '^[0-9a-fA-F]{64}$' -or $name -notin $required -or -not $hashed.Add($name)){throw 'Invalid, duplicate or unexpected checksum entry'}
    $actual=(Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash
    if($actual -ne $hash){throw "Checksum mismatch: $name"}
    $checks.Add([ordered]@{file=$name;check='SHA256';passed=$true})
}
if($hashed.Count -ne $required.Count){throw 'Missing checksum entries'}
foreach($arch in @('x64','arm64')){
    $setup=Join-Path $root "UpdateDeck-2.0.1-win-$arch-Setup.exe";$setupMachine=Setup-Machine $setup;$expectedSetup=if($arch -eq 'x64'){0x8664}else{0x14c}
    if($setupMachine -ne $expectedSetup){throw 'Setup bootstrapper architecture mismatch'}
    $checks.Add([ordered]@{file=[IO.Path]::GetFileName($setup);check='Setup bootstrapper PE';peMachine=('0x{0:X4}' -f $setupMachine);passed=$true})
    $path=Join-Path $root "UpdateDeck-2.0.1-win-$arch-Portable.zip"
    $zip=[IO.Compression.ZipFile]::OpenRead($path)
    try{
        if($zip.Entries.Count -gt 5000){throw 'Unexpected release archive entry count'}
        $entries=@{};$expandedBytes=0L
        foreach($entry in $zip.Entries){
            $name=$entry.FullName.Replace('\','/')
            Assert-SafeArchivePath $name
            if($entries.ContainsKey($name) -or ($entry.ExternalAttributes -shr 16 -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400) -ne 0){throw 'Duplicate archive entry or filesystem link'}
            $expandedBytes+=$entry.Length;if($expandedBytes -gt 1GB){throw 'Unexpected archive expansion size'}
            $entries[$name]=$entry
        }
        [UpdateDeck.Packaging.ArchiveIntegrity]::ValidatePaths([string[]]@($entries.Keys))
        if(-not $entries.ContainsKey('portable.mode')){throw 'Portable marker missing'}
        if(-not $entries.ContainsKey('Assets/UpdateDeck.ico')){throw 'Explicit shortcut icon missing'}
        $icon=Read-Bytes $entries['Assets/UpdateDeck.ico']
        if($icon.Length -lt 22 -or [BitConverter]::ToUInt16($icon,0) -ne 0 -or [BitConverter]::ToUInt16($icon,2) -ne 1 -or [BitConverter]::ToUInt16($icon,4) -lt 1){throw 'Invalid shortcut icon resource'}
        $checks.Add([ordered]@{file='Assets/UpdateDeck.ico';check='Standalone shortcut icon';passed=$true})
        if(@($entries.Keys | Where-Object {$_ -match '(^|/)(Data|helpers|TestResults)/|\.pdb$|\.db$|TestInstaller|UpdateDeck\.Tests'}).Count -gt 0){throw 'Development or local state leaked into release'}
        $expected=if($arch -eq 'x64'){0x8664}else{0xAA64}
        foreach($name in @('UpdateDeck.exe','coreclr.dll','hostfxr.dll','hostpolicy.dll','clrjit.dll','wpfgfx_cor3.dll','Microsoft.Management.Deployment.dll','WindowsPackageManager.dll','e_sqlite3.dll')){
            if(-not $entries.ContainsKey($name)){throw "Missing native dependency: $name"}
            $machine=Machine-Of (Read-Bytes $entries[$name])
            if($machine -ne $expected){throw "Native architecture mismatch: $arch / $name (0x$($machine.ToString('X4')))"}
            $checks.Add([ordered]@{file=$name;architecture=$arch;peMachine=('0x{0:X4}' -f $machine);passed=$true})
        }
        $runtime=ConvertFrom-UniqueJson (Read-Bytes $entries['UpdateDeck.runtimeconfig.json'])
        if($runtime.runtimeOptions.tfm -ne 'net10.0' -or @($runtime.runtimeOptions.includedFrameworks).Count -ne 2 -or @(Compare-Object @($runtime.runtimeOptions.includedFrameworks.name | Sort-Object) @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')).Count -ne 0){throw 'The exact self-contained Core and Desktop framework set is required'}
        if(@($runtime.runtimeOptions.includedFrameworks | Where-Object {$_.version -ne '10.0.15'}).Count -gt 0){throw 'Unserviced runtime found'}
        if($metadata.signed){
            foreach($firstParty in @('UpdateDeck.exe','UpdateDeck.dll','UpdateDeck.Core.dll','UpdateDeck.Windows.dll')){
                $signaturePath=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) ([Guid]::NewGuid().ToString('N')+[IO.Path]::GetExtension($firstParty))
                New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($signaturePath)) | Out-Null
                try{[IO.File]::WriteAllBytes($signaturePath,(Read-Bytes $entries[$firstParty]));$signature=Get-AuthenticodeSignature -LiteralPath $signaturePath;if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $metadata.signingThumbprint){throw 'First-party signature or expected certificate validation failed'}}finally{if(Test-Path -LiteralPath $signaturePath){Remove-Item -LiteralPath $signaturePath}}
            }
            $setupSignature=Get-AuthenticodeSignature -LiteralPath (Join-Path $root "UpdateDeck-2.0.1-win-$arch-Setup.exe")
            if($setupSignature.Status -ne 'Valid' -or $setupSignature.SignerCertificate.Thumbprint -ne $metadata.signingThumbprint){throw 'Setup signature or expected certificate validation failed'}
            $checks.Add([ordered]@{architecture=$arch;check='Windows Authenticode';passed=$true})
        }
        foreach($entry in $zip.Entries){
            $inspectPe=[IO.Path]::GetExtension($entry.FullName) -in '.dll','.exe'
            $evidence=[UpdateDeck.Packaging.ArchiveIntegrity]::Read($entry,64MB,$inspectPe)
            if(-not $entry.FullName.EndsWith('/')){
                $payload=[ordered]@{architecture=$arch;path=$entry.FullName;bytes=$evidence.Bytes;sha256=$evidence.Sha256}
                if($inspectPe){$pe=[UpdateDeck.Packaging.ArchiveIntegrity]::InspectPe($evidence.Content,$expected);$payload.peMachine=$pe.Machine;$payload.managedAnyCpu=$pe.ManagedAnyCpu}
                $payloads.Add($payload)
            }
        }
        $checks.Add([ordered]@{file=[IO.Path]::GetFileName($path);check='Portable marker, runtime, clean payload, bounded expansion and CRC-32';passed=$true;entries=$entries.Count})
    }finally{$zip.Dispose()}
}
$signing=if($metadata.signed){'Signed; Windows trust validation passed'}else{'Unsigned development release'}
$report=[ordered]@{atUtc=[DateTime]::UtcNow.ToString('o');version='2.0.1';sourceTreeSha256=$metadata.sourceTreeSha256;buildId=$metadata.buildId;passed=$true;signing=$signing;arm64RuntimeTested=$false;checks=$checks;payloadFilesAudited=$payloads.Count;peFilesAudited=@($payloads | Where-Object {$_.Contains('peMachine')}).Count;payloads=$payloads}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
Write-Output "Verified $($checks.Count) release checks. Report: $ReportPath"
