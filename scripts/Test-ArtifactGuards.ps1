#Requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[Parameter(Mandatory)][string]$SourceArchive,[Parameter(Mandatory)][string]$WorkDirectory,[Parameter(Mandatory)][string]$ReportPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$release=[IO.Path]::GetFullPath($ReleaseDirectory);$source=[IO.Path]::GetFullPath($SourceArchive)
$fixture=Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) ('artifact-guards-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$results=[Collections.Generic.List[object]]::new()
$names=@('UpdateDeck-2.0.3-win-x64-Setup.exe','UpdateDeck-2.0.3-win-arm64-Setup.exe','UpdateDeck-2.0.3-win-x64-Portable.zip','UpdateDeck-2.0.3-win-arm64-Portable.zip','SHA256SUMS.txt','release.json')
foreach($name in $names){Copy-Item -LiteralPath (Join-Path $release $name) -Destination (Join-Path $fixture $name)}
$zipName='UpdateDeck-2.0.3-win-x64-Portable.zip';$zipPath=Join-Path $fixture $zipName
$sourceCopy=Join-Path $fixture 'source.zip';Copy-Item -LiteralPath $source -Destination $sourceCopy
function Check-Rejected([string]$name,[scriptblock]$verify){
    $rejected=$false;$reason=''
    try{& $verify | Out-Null}catch{$rejected=$true;$reason=$_.Exception.Message}
    $results.Add([ordered]@{name=$name;passed=$rejected;reason=$reason})
    if(-not $rejected){throw "Artifact guard accepted the adversarial fixture: $name"}
}
function Verify-ReleaseFixture {& (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ReleaseDirectory $fixture -ReportPath (Join-Path $fixture 'release-check.json')}
function Verify-SourceFixture {& (Join-Path $PSScriptRoot 'Verify-Source.ps1') -ArchivePath $sourceCopy -ReportPath (Join-Path $fixture 'source-check.json')}
function Reset-Release {foreach($name in @($zipName,'SHA256SUMS.txt','release.json')){Copy-Item -LiteralPath (Join-Path $release $name) -Destination (Join-Path $fixture $name) -Force}}
function Rehash-Zip {
    $lines=Get-Content -LiteralPath (Join-Path $fixture 'SHA256SUMS.txt')
    $hash=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines | ForEach-Object {if($_.EndsWith('  '+$zipName)){$hash+'  '+$zipName}else{$_}} | Set-Content -LiteralPath (Join-Path $fixture 'SHA256SUMS.txt') -Encoding utf8
}
function Edit-Zip([string]$path,[scriptblock]$change){$archive=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Update);try{& $change $archive}finally{$archive.Dispose()}}
function Write-Entry($archive,[string]$name,[byte[]]$bytes){$entry=$archive.CreateEntry($name);$stream=$entry.Open();try{$stream.Write($bytes)}finally{$stream.Dispose()}}
function Read-Entry($entry){$stream=$entry.Open();$memory=[IO.MemoryStream]::new();try{$stream.CopyTo($memory);return ,$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}}
function Mutate-CentralField([string]$path,[string]$entryName,[string]$field){
    # Only the disposable, small, non-ZIP64 fixture archives produced above are modified.
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{
        $tailLength=[int][Math]::Min(65557,$stream.Length);$tail=[byte[]]::new($tailLength);$stream.Position=$stream.Length-$tailLength;$stream.ReadExactly($tail,0,$tail.Length)
        $end=-1;for($i=$tail.Length-22;$i -ge 0;$i--){if([BitConverter]::ToUInt32($tail,$i) -eq 0x06054b50 -and $i+22+[BitConverter]::ToUInt16($tail,$i+20) -eq $tail.Length){$end=$i;break}}
        if($end -lt 0){throw 'Fixture end-of-directory record missing'}
        $count=[BitConverter]::ToUInt16($tail,$end+10);$position=[long][BitConverter]::ToUInt32($tail,$end+16)
        for($i=0;$i -lt $count;$i++){
            $stream.Position=$position;$header=[byte[]]::new(46);$stream.ReadExactly($header,0,46)
            if([BitConverter]::ToUInt32($header,0) -ne 0x02014b50){throw 'Fixture directory record invalid'}
            $nameLength=[BitConverter]::ToUInt16($header,28);$extra=[BitConverter]::ToUInt16($header,30);$comment=[BitConverter]::ToUInt16($header,32);$nameBytes=[byte[]]::new($nameLength);$stream.ReadExactly($nameBytes,0,$nameLength)
            if([Text.Encoding]::UTF8.GetString($nameBytes) -ceq $entryName){
                $offset=if($field -eq 'crc'){16}else{24};$old=[BitConverter]::ToUInt32($header,$offset);$replacement=if($field -eq 'crc'){[uint32]($old -bxor 1)}else{[uint32]($old-1)}
                $stream.Position=$position+$offset;$bytes=[BitConverter]::GetBytes($replacement);$stream.Write($bytes,0,$bytes.Length);return
            }
            $position+=46+$nameLength+$extra+$comment
        }
        throw 'Fixture entry not found'
    }finally{$stream.Dispose()}
}
function Write-SourceIndex($archive,$manifest){
    $archive.GetEntry('SOURCE-INDEX.json').Delete();Write-Entry $archive 'SOURCE-INDEX.json' ([Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 8)))
}
Verify-ReleaseFixture | Out-Null;Verify-SourceFixture | Out-Null
$results.Add([ordered]@{name='Unmodified release and source accepted';passed=$true;reason='Baseline verified before mutation'})
try{
    [IO.File]::AppendAllText((Join-Path $fixture 'SHA256SUMS.txt'),"`n"+('0'*64)+'  '+$zipName)
    Check-Rejected 'Duplicate checksum' {Verify-ReleaseFixture};Reset-Release
    $stream=[IO.File]::OpenWrite($zipPath);try{$stream.Position=20;$stream.WriteByte(255)}finally{$stream.Dispose()}
    Check-Rejected 'Changed archive without matching checksum' {Verify-ReleaseFixture};Reset-Release
    $unsafeNames=@('../escape.txt','Data/local.db','UpdateDeck.exe','UPDATEDECK.EXE','dir./file.txt','dir /file.txt','NUL.txt','nested/COM1.txt','nested/file:stream','nested/back\..\..\escape.txt','nested//file.txt','NUL .txt','README.md/child.txt','portable.mode/')+@(('x'*256)+'.txt')
    foreach($unsafe in $unsafeNames){
        Edit-Zip $zipPath {param($archive) Write-Entry $archive $unsafe ([Text.Encoding]::UTF8.GetBytes('adversarial fixture'))};Rehash-Zip
        Check-Rejected ('Archive entry: '+$unsafe) {Verify-ReleaseFixture};Reset-Release
    }
    Edit-Zip $zipPath {param($archive) $entry=$archive.GetEntry('UpdateDeck.exe');$bytes=Read-Entry $entry;$entry.Delete();$offset=[BitConverter]::ToInt32($bytes,60);[BitConverter]::GetBytes([uint16]0xAA64).CopyTo($bytes,$offset+4);Write-Entry $archive 'UpdateDeck.exe' $bytes};Rehash-Zip
    Check-Rejected 'ARM64 executable inside x64 release' {Verify-ReleaseFixture};Reset-Release
    Edit-Zip $zipPath {param($archive) $entry=$archive.GetEntry('PenImc_cor3.dll');$bytes=Read-Entry $entry;$entry.Delete();$offset=[BitConverter]::ToInt32($bytes,60);[BitConverter]::GetBytes([uint16]0xAA64).CopyTo($bytes,$offset+4);Write-Entry $archive 'PenImc_cor3.dll' $bytes};Rehash-Zip
    Check-Rejected 'Wrong architecture in an additional native runtime DLL' {Verify-ReleaseFixture};Reset-Release
    Edit-Zip $zipPath {param($archive)
        $entry=$archive.GetEntry('UpdateDeck.Core.dll');$bytes=Read-Entry $entry;$entry.Delete();$memory=[IO.MemoryStream]::new($bytes,$false);$reader=[System.Reflection.PortableExecutable.PEReader]::new($memory)
        try{$offset=$reader.PEHeaders.CorHeaderStartOffset+16}finally{$reader.Dispose();$memory.Dispose()}
        $flags=[BitConverter]::ToUInt32($bytes,$offset);[BitConverter]::GetBytes([uint32]($flags -bor 2)).CopyTo($bytes,$offset);Write-Entry $archive 'UpdateDeck.Core.dll' $bytes
    };Rehash-Zip
    Check-Rejected '32-bit-required managed DLL inside x64 release' {Verify-ReleaseFixture};Reset-Release
    Edit-Zip $zipPath {param($archive)
        $archive.GetEntry('PenImc_cor3.dll').Delete();$coff=[byte[]]::new(20);[BitConverter]::GetBytes([uint16]0x8664).CopyTo($coff,0);Write-Entry $archive 'PenImc_cor3.dll' $coff
    };Rehash-Zip
    Check-Rejected 'COFF object masquerading as a portable DLL' {Verify-ReleaseFixture};Reset-Release
    Edit-Zip $zipPath {param($archive)
        $entry=$archive.GetEntry('UpdateDeck.Core.dll');$bytes=Read-Entry $entry;$entry.Delete();$offset=[BitConverter]::ToInt32($bytes,60);[BitConverter]::GetBytes([uint16]0x14c).CopyTo($bytes,$offset+4);Write-Entry $archive 'UpdateDeck.Core.dll' $bytes
    };Rehash-Zip
    Check-Rejected 'Managed PE machine and optional-header bitness disagree' {Verify-ReleaseFixture};Reset-Release
    Edit-Zip $zipPath {param($archive) $entry=$archive.GetEntry('UpdateDeck.runtimeconfig.json');$runtime=[Text.Encoding]::UTF8.GetString((Read-Entry $entry)) | ConvertFrom-Json;$entry.Delete();$runtime.runtimeOptions.includedFrameworks=@($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App');Write-Entry $archive 'UpdateDeck.runtimeconfig.json' ([Text.Encoding]::UTF8.GetBytes(($runtime | ConvertTo-Json -Depth 8)))};Rehash-Zip
    Check-Rejected 'Missing Windows Desktop runtime' {Verify-ReleaseFixture};Reset-Release
    $metadata=Get-Content -LiteralPath (Join-Path $fixture 'release.json') -Raw | ConvertFrom-Json;$metadata.signed='false';$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture 'release.json')
    Check-Rejected 'String used as signing Boolean' {Verify-ReleaseFixture};Reset-Release
    foreach($field in @('crc','length')){
        Mutate-CentralField $zipPath 'README.md' $field;Rehash-Zip
        Check-Rejected ('Portable '+$field+' metadata differs from actual content') {Verify-ReleaseFixture};Reset-Release
    }
    Edit-Zip $zipPath {param($archive) $entry=$archive.CreateEntry('symlink');$entry.ExternalAttributes=-1577123840;$stream=$entry.Open();try{$stream.Write([byte[]](1,2,3))}finally{$stream.Dispose()}};Rehash-Zip
    Check-Rejected 'Portable symbolic link entry' {Verify-ReleaseFixture};Reset-Release
    Edit-Zip $zipPath {param($archive) $entry=$archive.GetEntry('README.md');$entry.ExternalAttributes=$entry.ExternalAttributes -bor 0x400};Rehash-Zip
    Check-Rejected 'Portable reparse-point attribute' {Verify-ReleaseFixture};Reset-Release
    $metadata=Get-Content -LiteralPath (Join-Path $fixture 'release.json') -Raw | ConvertFrom-Json;$metadata.schema='1';$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture 'release.json')
    Check-Rejected 'String used as release schema' {Verify-ReleaseFixture};Reset-Release
    $metadataText=Get-Content -LiteralPath (Join-Path $fixture 'release.json') -Raw;$metadataText.Insert(1,'"signed":false,') | Set-Content -LiteralPath (Join-Path $fixture 'release.json')
    Check-Rejected 'Duplicate release JSON property' {Verify-ReleaseFixture};Reset-Release
    foreach($case in @('tamper','extra','missing-index','unsafe')){
        Copy-Item -LiteralPath $source -Destination $sourceCopy -Force
        Edit-Zip $sourceCopy {param($archive)
            switch($case){
                'tamper' {$entry=$archive.GetEntry('README.md');$bytes=Read-Entry $entry;$bytes[0]=$bytes[0] -bxor 1;$entry.Delete();Write-Entry $archive 'README.md' $bytes}
                'extra' {Write-Entry $archive 'unindexed.txt' ([byte[]](1,2,3))}
                'missing-index' {$archive.GetEntry('SOURCE-INDEX.json').Delete()}
                'unsafe' {Write-Entry $archive '../escape.cs' ([byte[]](1,2,3))}
            }
        }
        Check-Rejected ('Source '+$case) {Verify-SourceFixture}
    }
    foreach($case in @('path-case','index-case-alias','schema-string','byte-string','duplicate-json')){
        Copy-Item -LiteralPath $source -Destination $sourceCopy -Force
        Edit-Zip $sourceCopy {param($archive)
            $manifest=[Text.Encoding]::UTF8.GetString((Read-Entry $archive.GetEntry('SOURCE-INDEX.json'))) | ConvertFrom-Json
            switch($case){
                'path-case' {$entry=$archive.GetEntry('README.md');$bytes=Read-Entry $entry;$entry.Delete();Write-Entry $archive 'readme.md' $bytes}
                'index-case-alias' {
                    $readme=$manifest.files | Where-Object path -CEQ 'README.md';$manifest.files+=@([pscustomobject]@{path='readme.md';bytes=$readme.bytes;sha256=$readme.sha256});$manifest.fileCount=$manifest.files.Count
                    Write-Entry $archive 'unindexed-file.cs' ([byte[]](1,2,3))
                    $ordered=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal);foreach($file in $manifest.files){$ordered.Add($file.path,$file.sha256)}
                    $lines=($ordered.GetEnumerator() | ForEach-Object {$_.Value+'  '+$_.Key+"`n"}) -join ''; $manifest.treeSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($lines))).ToLowerInvariant();Write-SourceIndex $archive $manifest
                }
                'schema-string' {$manifest.schema='1';Write-SourceIndex $archive $manifest}
                'byte-string' {$manifest.files[0].bytes=[string]$manifest.files[0].bytes;Write-SourceIndex $archive $manifest}
                'duplicate-json' {$entry=$archive.GetEntry('SOURCE-INDEX.json');$json=[Text.Encoding]::UTF8.GetString((Read-Entry $entry));$entry.Delete();Write-Entry $archive 'SOURCE-INDEX.json' ([Text.Encoding]::UTF8.GetBytes($json.Insert(1,'"schema":1,')))}
            }
        }
        Check-Rejected ('Source '+$case) {Verify-SourceFixture}
    }
    foreach($field in @('crc','length')){
        Copy-Item -LiteralPath $source -Destination $sourceCopy -Force;Mutate-CentralField $sourceCopy 'SOURCE-INDEX.json' $field
        Check-Rejected ('Source index '+$field+' metadata differs from content') {Verify-SourceFixture}
    }
    [ordered]@{atUtc=[DateTime]::UtcNow.ToString('o');passed=$true;cases=$results.Count;results=$results;scope='Mutated copies only. Original release and source archives were not changed.'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Output "Passed $($results.Count) artifact guard cases. Report: $ReportPath"
}finally{Write-Verbose "Disposable adversarial copies retained for inspection: $fixture"}
