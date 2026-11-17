#Requires -Version 7.4
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\releases'),
    [Parameter(Mandatory=$true)][string]$IsccPath,
    [string]$SignToolPath,
    [string]$SigningThumbprint,
    [switch]$SkipTests
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot=[IO.Path]::GetFullPath($OutputDirectory)
$buildId=([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))+'-'+([Guid]::NewGuid().ToString('N').Substring(0,8))
# A run never republishes a directory from which the application may have been launched.
$buildRoot=Join-Path $projectRoot ('artifacts\staging\'+$buildId)
$packageRoot=Join-Path $buildRoot 'packages'
New-Item -ItemType Directory -Force -Path $releaseRoot,$buildRoot,$packageRoot | Out-Null
if(-not (Test-Path -LiteralPath $IsccPath)){throw 'Provide the path to Inno Setup 7 ISCC.exe.'}
function Invoke-Checked([string]$Executable,[string[]]$Arguments) {
    & $Executable @Arguments
    if($LASTEXITCODE -ne 0){throw "Build command failed: $Executable (exit $LASTEXITCODE)"}
}
function Sign-Artifact([string]$Path) {
    if($SigningThumbprint){
        if(-not $SignToolPath){throw 'Signing requires SignToolPath.'}
        Invoke-Checked $SignToolPath @('sign','/sha1',$SigningThumbprint,'/fd','SHA256','/tr','https://timestamp.digicert.com','/td','SHA256',$Path)
        Invoke-Checked $SignToolPath @('verify','/pa','/all',$Path)
    }
}
Push-Location $projectRoot
try {
    $sourceBefore=& (Join-Path $PSScriptRoot 'Get-SourceManifest.ps1') -ProjectRoot $projectRoot
    Invoke-Checked 'dotnet' @('restore','UpdateDeck.sln','--locked-mode')
    if(-not $SkipTests){Invoke-Checked 'dotnet' @('test','tests/UpdateDeck.Tests/UpdateDeck.Tests.csproj','-c','Release','--no-restore','--logger','trx;LogFileName=UpdateDeck-tests.trx','--results-directory','artifacts/TestResults')}
    foreach($arch in @('x64','arm64')) {
        $rid='win-'+$arch
        $publish=Join-Path $buildRoot $rid
        Invoke-Checked 'dotnet' @('publish','src/UpdateDeck.App/UpdateDeck.App.csproj','-c','Release','-r',$rid,'--self-contained','true','-o',$publish,'-p:DebugType=None','-p:DebugSymbols=false')
        $runtime=Get-Content -LiteralPath (Join-Path $publish 'UpdateDeck.runtimeconfig.json') -Raw | ConvertFrom-Json
        if(@($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.version -ne '10.0.15' }).Count -ne 0){throw 'All bundled frameworks must use .NET 10.0.15.'}
        foreach($name in @('UpdateDeck.exe','Microsoft.Management.Deployment.dll','WindowsPackageManager.dll','e_sqlite3.dll')) {
            if(-not (Test-Path -LiteralPath (Join-Path $publish $name))){throw "Missing runtime dependency: $name ($rid)"}
        }
        Copy-Item -LiteralPath 'LICENSE','README.md','THIRD-PARTY-NOTICES.md','WinGet-NOTICE.txt','WinGet-COM-NOTICE.txt' -Destination $publish
        New-Item -ItemType Directory -Force -Path (Join-Path $publish 'docs') | Out-Null
        Copy-Item -Path 'docs\*.md' -Destination (Join-Path $publish 'docs')
        New-Item -ItemType Directory -Force -Path (Join-Path $publish 'licenses') | Out-Null
        Copy-Item -Path 'licenses\*.txt' -Destination (Join-Path $publish 'licenses')
        foreach($firstParty in @('UpdateDeck.exe','UpdateDeck.dll','UpdateDeck.Core.dll','UpdateDeck.Windows.dll')){Sign-Artifact (Join-Path $publish $firstParty)}
        $forbidden=Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
            $relative=[IO.Path]::GetRelativePath($publish,$_.FullName)
            $relative -match '(^|[\\/])(Data|helpers|TestResults)[\\/]|\.pdb$|\.db$|TestInstaller|UpdateDeck\.Tests'
        }
        if(@($forbidden).Count -gt 0){throw 'Release staging contains local state or development-only files. Nothing was promoted.'}
        Invoke-Checked $IsccPath @("--define=AppArch=$arch","--define=PublishDir=$publish","--define=ArtifactDir=$packageRoot",'installer/UpdateDeck.iss')
        Sign-Artifact (Join-Path $packageRoot "UpdateDeck-2.0.0-$rid-Setup.exe")
        Set-Content -LiteralPath (Join-Path $publish 'portable.mode') -Value 'UpdateDeck portable storage enabled' -Encoding utf8
        $zip=Join-Path $packageRoot "UpdateDeck-2.0.0-$rid-Portable.zip"
        Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force -CompressionLevel Optimal
    }
    $files=Get-ChildItem -LiteralPath $packageRoot -File | Where-Object {$_.Extension -in '.zip','.exe'} | Sort-Object Name
    $hashes=foreach($file in $files){$hash=Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256;'{0}  {1}' -f $hash.Hash.ToLowerInvariant(),$file.Name}
    Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Value $hashes -Encoding utf8
    $source=& (Join-Path $PSScriptRoot 'Get-SourceManifest.ps1') -ProjectRoot $projectRoot
    if($source.treeSha256 -ne $sourceBefore.treeSha256){throw 'Source inputs changed during this release build. Nothing was promoted; build again from stable inputs.'}
    $source | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $buildRoot 'source-inputs.json') -Encoding utf8
    $metadata=[ordered]@{schema=1;product='UpdateDeck';version='2.0.0';channel='development-preview';buildId=$buildId;builtUtc=[DateTime]::UtcNow.ToString('o');sourceTreeSha256=$source.treeSha256;minimumWindowsBuild=19041;architectures=@('x64','arm64');runtime='10.0.15';selfContained=$true;signed=[bool]$SigningThumbprint;signingThumbprint=$SigningThumbprint;arm64RuntimeTested=$false;files=@($files.Name)}
    $metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packageRoot 'release.json') -Encoding utf8
    $validation=Join-Path $buildRoot 'release-validation.json'
    & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ReleaseDirectory $packageRoot -ReportPath $validation
    # Copy to a temporary name on the destination volume, then atomically replace each artifact.
    # release.json is the final commit marker; failed validation never changes published files.
    foreach($name in @($files.Name)+@('SHA256SUMS.txt','release.json')){
        $source=Join-Path $packageRoot $name
        $destination=Join-Path $releaseRoot $name
        $incoming=$destination+'.incoming-'+$buildId
        [IO.File]::Copy($source,$incoming,$false)
        if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $incoming).Hash){throw "Promotion copy failed integrity validation: $name"}
        [IO.File]::Move($incoming,$destination,$true)
    }
    $validationRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\verification'))
    New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
    Copy-Item -LiteralPath $validation -Destination (Join-Path $validationRoot 'release-validation.json') -Force
    Write-Output "Promoted verified release $buildId to $releaseRoot"
}
finally{Pop-Location}
