if(-not ('UpdateDeck.Packaging.ArchiveIntegrity' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'ArchiveIntegrity.cs')}
function Read-ArchiveEntryBytes($Entry,[long]$Maximum=64MB) {
    return ,([UpdateDeck.Packaging.ArchiveIntegrity]::Read($Entry,$Maximum,$true).Content)
}
function ConvertFrom-UniqueJson([byte[]]$Content) {
    [UpdateDeck.Packaging.ArchiveIntegrity]::ValidateJson($Content)
    return [Text.Encoding]::UTF8.GetString($Content) | ConvertFrom-Json
}
function Assert-SafeArchivePath([string]$Name) {
    if([string]::IsNullOrWhiteSpace($Name) -or $Name.Length -gt 2048 -or $Name.StartsWith('/') -or $Name.Contains('//') -or $Name.Contains(':') -or $Name.Contains('\')){throw 'Unsafe archive path'}
    $segments=$Name.TrimEnd('/').Split('/')
    foreach($segment in $segments){
        if([string]::IsNullOrWhiteSpace($segment) -or $segment.Length -gt 255 -or $segment -in '.','..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment.IndexOfAny('<>"|?*'.ToCharArray()) -ge 0 -or $segment.ToCharArray().Where({[int]$_ -lt 32}).Count -gt 0){throw 'Unsafe Windows archive path segment'}
        if(($segment -split '\.',2)[0].TrimEnd(' ','.') -match '^(?i:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$'){throw 'Reserved Windows device path in archive'}
    }
}
