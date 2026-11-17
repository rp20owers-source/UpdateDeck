#Requires -Version 7.4
[CmdletBinding()]
param([string]$ProjectRoot=(Join-Path $PSScriptRoot '..'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath($ProjectRoot)
$files=@(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
    $relative=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
    if($relative -eq 'SOURCE-INDEX.json'){return} # Archive provenance is generated metadata, never a source input.
    if($relative -match '(^|/)(bin|obj|artifacts|\.git|\.vs|Data|TestResults)/'){return}
    if(($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Source inputs cannot be filesystem links'}
    if($_.Extension -in '.exe','.dll','.pdb','.db','.zip','.nupkg','.trx','.log'){throw "Unexpected generated or binary source input: $relative"}
    [pscustomobject]@{path=$relative;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$ordered=[Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
foreach($file in $files){$ordered.Add($file.path,$file)}
$files=@($ordered.Values)
$index=($files | ForEach-Object { $_.sha256+'  '+$_.path+"`n" }) -join ''
$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($index))).ToLowerInvariant()
[pscustomobject]@{schema=1;product='UpdateDeck';version='2.0.1';treeSha256=$hash;fileCount=$files.Count;files=$files}
