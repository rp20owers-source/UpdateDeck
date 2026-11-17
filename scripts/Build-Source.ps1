#Requires -Version 7.4
[CmdletBinding()]
param([string]$OutputPath=(Join-Path $PSScriptRoot '..\..\UpdateDeck-2.0.3-Source.zip'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest=& (Join-Path $PSScriptRoot 'Get-SourceManifest.ps1') -ProjectRoot $root
$release=Join-Path $PSScriptRoot '..\..\releases\release.json'
if(Test-Path -LiteralPath $release){$metadata=Get-Content -LiteralPath $release -Raw | ConvertFrom-Json;if(-not $metadata.PSObject.Properties['sourceTreeSha256'] -or $metadata.sourceTreeSha256 -ne $manifest.treeSha256){throw 'Source changed after the recorded release build, or that build has no source fingerprint. Rebuild the release before packaging its matching source.'}}
$destination=[IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
$temporary=$destination+'.incoming-'+[Guid]::NewGuid().ToString('N')
$archive=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in $manifest.files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $root $file.path),$file.path,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}
    $entry=$archive.CreateEntry('SOURCE-INDEX.json');$stream=$entry.Open();$writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
    try{$writer.Write(($manifest | ConvertTo-Json -Depth 6))}finally{$writer.Dispose();$stream.Dispose()}
}finally{$archive.Dispose()}
& (Join-Path $PSScriptRoot 'Verify-Source.ps1') -ArchivePath $temporary -ExpectedTreeSha256 $manifest.treeSha256
[IO.File]::Move($temporary,$destination,$true)
((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($destination)) | Set-Content -LiteralPath ($destination+'.sha256') -Encoding utf8
Write-Output "Source archive: $destination; source tree SHA-256: $($manifest.treeSha256)"
