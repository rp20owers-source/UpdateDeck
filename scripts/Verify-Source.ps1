#Requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ArchivePath,[string]$ExpectedTreeSha256,[string]$ReportPath=(Join-Path $PSScriptRoot '..\..\verification\source-validation.json'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Archive-Paths.ps1')
$zip=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
try{
    if($zip.Entries.Count -gt 5000){throw 'Unexpected source archive entry count'}
    $entries=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase);$expanded=0L
    foreach($entry in $zip.Entries){
        $name=$entry.FullName.Replace('\','/');Assert-SafeArchivePath $name
        if(-not $entries.TryAdd($name,$entry)){throw 'Duplicate source archive path'}
        if(($entry.ExternalAttributes -shr 16 -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400) -ne 0){throw 'Source links are not allowed'}
        if($name -match '(^|/)(bin|obj|artifacts|\.git|\.vs|Data|TestResults)/|\.(exe|dll|pdb|db|trx|nupkg|zip|log)$'){throw 'Generated or local data in source archive'}
        $expanded+=$entry.Length;if($entry.Length -gt 10MB -or $expanded -gt 50MB){throw 'Unexpected source archive size'}
    }
    [UpdateDeck.Packaging.ArchiveIntegrity]::ValidatePaths([string[]]@($entries.Keys))
    if(-not $entries.ContainsKey('SOURCE-INDEX.json') -or $entries['SOURCE-INDEX.json'].Length -gt 2MB){throw 'Missing or oversized source index'}
    $manifest=ConvertFrom-UniqueJson (Read-ArchiveEntryBytes $entries['SOURCE-INDEX.json'] 2MB)
    if(($manifest.schema -isnot [long] -and $manifest.schema -isnot [int]) -or ($manifest.fileCount -isnot [long] -and $manifest.fileCount -isnot [int]) -or $manifest.files -isnot [array] -or $manifest.schema -ne 1 -or $manifest.product -cne 'UpdateDeck' -or $manifest.version -cne '2.0.1' -or $manifest.fileCount -ne @($manifest.files).Count){throw 'Invalid source index identity or count'}
    $ordered=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
    $indexed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $manifest.files){
        if($file.path -isnot [string] -or ($file.bytes -isnot [long] -and $file.bytes -isnot [int]) -or $file.bytes -lt 0){throw 'Invalid source path or byte-count type'}
        Assert-SafeArchivePath $file.path
        if($file.path -ieq 'SOURCE-INDEX.json' -or $file.sha256 -isnot [string] -or $file.sha256 -cnotmatch '^[a-f0-9]{64}$' -or -not $entries.ContainsKey($file.path) -or -not $indexed.Add($file.path)){throw 'Invalid, duplicate or missing indexed source file'}
        $entry=$entries[$file.path];if($entry.FullName.Replace('\','/') -cne $file.path -or $entry.Length -ne $file.bytes){throw 'Indexed source path or size mismatch'}
        $hash=[UpdateDeck.Packaging.ArchiveIntegrity]::Read($entry,10MB,$false).Sha256
        if($hash -ne $file.sha256){throw "Source checksum mismatch: $($file.path)"}
        $ordered.Add($file.path,$hash)
    }
    if(@($zip.Entries | Where-Object {-not $_.FullName.EndsWith('/')}).Count -ne $ordered.Count+1 -or @($entries.Keys | Where-Object {-not $_.EndsWith('/') -and $_ -ine 'SOURCE-INDEX.json' -and -not $indexed.Contains($_)}).Count -gt 0){throw 'Unindexed source files found'}
    $text=($ordered.GetEnumerator() | ForEach-Object {$_.Value+'  '+$_.Key+"`n"}) -join ''
    $tree=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
    if($tree -ne $manifest.treeSha256 -or $ExpectedTreeSha256 -and $tree -ne $ExpectedTreeSha256){throw 'Source tree does not match the index or recorded release build'}
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) | Out-Null
    [ordered]@{atUtc=[DateTime]::UtcNow.ToString('o');passed=$true;sourceTreeSha256=$tree;sourceFiles=$ordered.Count;sourceArchiveSha256=(Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant();cleanSourcePayload=$true} | ConvertTo-Json | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Output "Verified $($ordered.Count) source files and source tree $tree"
}finally{$zip.Dispose()}
