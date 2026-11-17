using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;

namespace UpdateDeck.Tests;

public sealed class OfficialInstallerTests
{
    [Fact]public async Task CancellationAtTheFinalPinBoundaryCannotLaunchTheInstaller()
    {
        int checks=0,launches=0;string directory=Fixture.Temp();using var cancel=new CancellationTokenSource();
        using var provider=Provider(directory,Standard(),launch:(_,_,_)=>{launches++;return Task.FromResult(new ProcessResult(0,"",""));},pinStatus:(_,_)=>{if(++checks==3)cancel.Cancel();return Task.FromResult<bool?>(false);});
        var app=Assert.Single((await provider.AttachAsync([App()],true,CancellationToken.None)).Apps);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),cancel.Token));Assert.Equal(0,launches);Assert.Empty(Directory.GetFiles(Path.Combine(directory,"Downloads")));
    }
    [Fact]public async Task ALaunchExceptionCannotBeReportedAsAnOrdinaryFinishedFailure()
    {
        string directory=Fixture.Temp();using var provider=Provider(directory,Standard(),launch:(_,_,_)=>throw new IOException("fixture lost process observation"));
        var app=Assert.Single((await provider.AttachAsync([App()],true,CancellationToken.None)).Apps);var result=await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);
        Assert.True(result.StopQueue);Assert.Equal(OperationState.AwaitingVerification,result.State);string payload=Assert.Single(Directory.GetFiles(Path.Combine(directory,"Downloads")));Assert.Throws<IOException>(()=>File.OpenWrite(payload).Dispose());
    }
    private static readonly byte[] Payload=Encoding.UTF8.GetBytes(new string('x',110000));
    private static string Manifest(string arch="X64",string? digest=null,string? url=null,bool prerelease=false,bool duplicate=false)
    {
        string suffix=arch switch{"X64"=>".x64","Arm64"=>".arm64",_=>""};string name=$"npp.2.0.Installer{suffix}.exe";
        var asset=new{name,state="uploaded",size=Payload.Length,digest=digest??"sha256:"+Convert.ToHexString(SHA256.HashData(Payload)),browser_download_url=url??"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v2.0/"+name};
        return JsonSerializer.Serialize(new{draft=false,prerelease,tag_name="v2.0",html_url="https://github.com/notepad-plus-plus/notepad-plus-plus/releases/tag/v2.0",assets=duplicate?new[]{asset,asset}:new[]{asset}});
    }
    private static Software App()=>Fixture.App() with{Name="Notepad++",Publisher="Notepad++ Team",Provider="Registry",PackageId="",Scope="Machine",InstalledVersion="1.0",PrimaryExecutable=Path.Combine(Fixture.Temp(),"notepad++.exe"),State=UpdateState.Unknown};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){Requests.Add(request.RequestUri!);return Task.FromResult(respond(request));}
    }
    private static HttpResponseMessage Json(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text)};
    private static Handler Standard(string? metadata=null,byte[]? payload=null)=>new(r=>r.RequestUri==OfficialInstallerProvider.MetadataUri?Json(metadata??Manifest()):new(HttpStatusCode.OK){Content=new ByteArrayContent(payload??Payload)});
    private static OfficialInstallerProvider Provider(string directory,Handler handler,Func<string,string,CancellationToken,Task<ProcessResult>>? launch=null,Func<string,OfficialRelease,bool>? payloadCheck=null,Func<Software,string?>? boundary=null,Func<Software,CancellationToken,Task<bool?>>? pinStatus=null)=>
        new(directory,new OfficialDownloads(handler),_=>true,launch??((_,_,_)=>throw new InvalidOperationException("Unexpected installer launch")),app=>app,payloadCheck??((_,_)=>true),boundary??(_=>null),pinStatus??((_,_)=>Task.FromResult<bool?>(false)));
    [Theory][InlineData(true)][InlineData(null)]public async Task PinnedOrUnverifiedFallbackNeverRequestsMetadata(bool? pinned)
    {var handler=Standard();using var provider=Provider(Fixture.Temp(),handler,pinStatus:(_,_)=>Task.FromResult(pinned));var result=await provider.AttachAsync([App()],true,CancellationToken.None);Assert.Empty(handler.Requests);Assert.Equal(UpdateState.Attention,Assert.Single(result.Apps).State);Assert.False(result.Report.Available);}
    [Fact]public async Task PinAddedDuringDownloadBlocksDirectInstaller()
    {int checks=0;using var provider=Provider(Fixture.Temp(),Standard(),pinStatus:(_,_)=>Task.FromResult<bool?>(++checks>=3));var app=Assert.Single((await provider.AttachAsync([App()],true,CancellationToken.None)).Apps);var result=await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.Deferred,result.State);Assert.Contains("pin policy changed",result.Message);Assert.Equal(3,checks);}
    [Fact]public async Task DuplicateScopeAndArchitectureCannotAuthorizeDirectInstallers()
    {var handler=Standard();using var provider=Provider(Fixture.Temp(),handler);var result=await provider.AttachAsync([App() with{Id="first"},App() with{Id="second"}],true,CancellationToken.None);Assert.Empty(handler.Requests);Assert.All(result.Apps,x=>{Assert.True(x.Ambiguous);Assert.Equal(UpdateState.Attention,x.State);Assert.False(x.CanSupervise);});}
    [Fact]public async Task FutureCheckTimestampCannotAuthorizeDirectInstaller()
    {var handler=Standard();using var provider=Provider(Fixture.Temp(),handler);var app=Assert.Single((await provider.AttachAsync([App()],true,CancellationToken.None)).Apps);handler.Requests.Clear();Assert.Equal(OperationState.Deferred,(await provider.ExecuteAsync(app with{CheckedAt=DateTimeOffset.UtcNow.AddHours(1)},new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);Assert.Empty(handler.Requests);}
    [Theory][InlineData("X64")][InlineData("X86")][InlineData("Arm64")]
    public void ReleaseIsSelectedForInstalledArchitecture(string arch){var release=OfficialInstallerProvider.ParseRelease(Manifest(arch),arch);Assert.NotNull(release);Assert.Equal(arch,release.Architecture);Assert.Equal("2.0",release.Version);}
    [Fact]public void PrereleaseOrDuplicateAssetsCannotAuthorizeInstaller(){Assert.Null(OfficialInstallerProvider.ParseRelease(Manifest(prerelease:true),"X64"));Assert.Null(OfficialInstallerProvider.ParseRelease(Manifest(duplicate:true),"X64"));}
    [Theory][InlineData("https://evil.example/npp.exe")][InlineData("http://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v2.0/npp.2.0.Installer.x64.exe")][InlineData("https://github.com/other/repo/releases/download/v2.0/npp.2.0.Installer.x64.exe")]
    public void MetadataCannotReplaceReviewedAssetOrigin(string url)=>Assert.Null(OfficialInstallerProvider.ParseRelease(Manifest(url:url),"X64"));
    [Theory][InlineData("md5:1234")][InlineData("sha256:1234")][InlineData("")]
    public void MissingOrWeakDigestIsRejected(string digest)=>Assert.Null(OfficialInstallerProvider.ParseRelease(Manifest(digest:digest),"X64"));
    [Theory][InlineData("https://release-assets.githubusercontent.com/a?token=x",true)][InlineData("https://objects.githubusercontent.com/a",true)][InlineData("https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v2.0/a.exe",true)][InlineData("https://release-assets.githubusercontent.com.evil.example/a",false)][InlineData("https://user@release-assets.githubusercontent.com/a",false)][InlineData("http://objects.githubusercontent.com/a",false)][InlineData("https://objects.githubusercontent.com:8443/a",false)][InlineData("file:///C:/a.exe",false)]
    public void EveryRedirectHasAnExactHttpsHostBoundary(string uri,bool allowed)=>Assert.Equal(allowed,OfficialDownloads.AllowedAssetUri(new(uri)));
    [Theory][InlineData("Store")][InlineData("MSIX")][InlineData("Scoop")][InlineData("Chocolatey")][InlineData("WinGet")][InlineData("Portable")]
    public async Task EstablishedProviderOwnershipNeverBecomesOfficialFallback(string owner){var handler=Standard();using var p=Provider(Fixture.Temp(),handler);var result=await p.AttachAsync([App() with{Provider=owner}],true,CancellationToken.None);Assert.Empty(handler.Requests);Assert.NotEqual("Official installer",Assert.Single(result.Apps).Provider);}
    [Theory][InlineData("User")][InlineData("Unknown")]
    public void UnreviewedInstallationScopeIsRejected(string scope)=>Assert.False(OfficialInstallerProvider.IsCandidate(App() with{Scope=scope}));
    [Fact]public async Task UnsignedImpersonatorIsNotRecognized(){var app=App();File.WriteAllText(app.PrimaryExecutable,"unsigned fake executable");using var p=new OfficialInstallerProvider(Fixture.Temp(),new OfficialDownloads(Standard()));var result=await p.AttachAsync([app],true,CancellationToken.None);Assert.Equal("Registry",Assert.Single(result.Apps).Provider);Assert.False(OfficialInstallerProvider.IsNotepadSigned(app.PrimaryExecutable));}
    [Fact]public async Task OfflineCheckNeverRequestsMetadata(){var handler=Standard();using var p=Provider(Fixture.Temp(),handler);await p.AttachAsync([App()],false,CancellationToken.None);Assert.Empty(handler.Requests);}
    [Fact]public async Task OfficialReleaseRequiresIndividualReviewAndCannotEnterUpdateAll()
    {
        using var p=Provider(Fixture.Temp(),Standard());var checkedApp=Assert.Single((await p.AttachAsync([App()],true,CancellationToken.None)).Apps);
        Assert.True(checkedApp.CanSupervise);Assert.False(checkedApp.CanUpdate);
        var plan=UpdatePolicy.Plan([checkedApp],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue);
        Assert.Empty(plan.Automatic);Assert.Contains("individually",Assert.Single(plan.Deferred).Reason);
        Assert.Null(UpdatePolicy.BlockReason(checkedApp,new(),DateTimeOffset.UtcNow,false,false,long.MaxValue,true));
    }
    [Fact]public async Task DownloadHashMismatchNeverLaunchesAndIsRemoved()
    {
        string directory=Fixture.Temp();using var p=Provider(directory,Standard(digestManifest(),Payload));
        var app=Assert.Single((await p.AttachAsync([App()],true,CancellationToken.None)).Apps);
        var result=await p.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);
        Assert.Equal(OperationState.Failed,result.State);Assert.Contains("SHA-256",result.Message);Assert.Empty(Directory.GetFiles(Path.Combine(directory,"Downloads")));
        static string digestManifest()=>Manifest(digest:"sha256:"+new string('0',64));
    }
    [Fact]public async Task InvalidPayloadSignerCannotLaunchEvenWithCorrectHash()
    {
        using var p=Provider(Fixture.Temp(),Standard(),payloadCheck:(_,_)=>false);var app=Assert.Single((await p.AttachAsync([App()],true,CancellationToken.None)).Apps);
        var result=await p.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.Failed,result.State);Assert.Contains("signature",result.Message);
    }
    [Fact]public async Task AppStartingDuringDownloadBlocksInstaller()
    {
        using var p=Provider(Fixture.Temp(),Standard(),boundary:_=>"Application started during download");var app=Assert.Single((await p.AttachAsync([App()],true,CancellationToken.None)).Apps);
        var result=await p.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.Deferred,result.State);Assert.Contains("during download",result.Message);
    }
    [Fact]public async Task PayloadIsLockedAgainstReplacementUntilInstallerCompletes()
    {
        string directory=Fixture.Temp();int launched=0;
        using var p=Provider(directory,Standard(),(file,location,_)=>{launched++;Assert.Throws<IOException>(()=>{using var write=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});Assert.Throws<IOException>(()=>File.Delete(file));return Task.FromResult(new ProcessResult(0,"",""));});
        var app=Assert.Single((await p.AttachAsync([App()],true,CancellationToken.None)).Apps);var result=await p.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);
        Assert.Equal(1,launched);Assert.Equal(OperationState.AwaitingVerification,result.State);Assert.Empty(Directory.GetFiles(Path.Combine(directory,"Downloads")));
    }
    [Fact]public async Task PersistedOrTamperedTargetCannotAuthorizeExecution()
    {
        var handler=Standard();using var p=Provider(Fixture.Temp(),handler);var app=Assert.Single((await p.AttachAsync([App()],true,CancellationToken.None)).Apps);handler.Requests.Clear();
        Assert.Equal(OperationState.Deferred,(await p.ExecuteAsync(app with{AvailableVersion="9.0"},new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);Assert.Empty(handler.Requests);
        using var fresh=Provider(Fixture.Temp(),Standard());Assert.Equal(OperationState.Deferred,(await fresh.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);
    }
    [Fact]public async Task RedirectToUnreviewedHostIsStoppedBeforeRequest()
    {
        var handler=new Handler(_=>{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new("https://evil.example/payload.exe");return response;});using var download=new OfficialDownloads(handler);
        var release=OfficialInstallerProvider.ParseRelease(Manifest(),"X64")!;
        await Assert.ThrowsAsync<InvalidDataException>(()=>download.DownloadAsync(release,new MemoryStream(),new ImmediateProgress<string>(_=>{}),CancellationToken.None));Assert.Single(handler.Requests);
    }
    [Fact]public async Task TruncatedAndOversizedBodiesAreRejected()
    {
        var release=OfficialInstallerProvider.ParseRelease(Manifest(),"X64")!;
        foreach(var bytes in new[]{Payload[..^1],Payload.Concat(new byte[]{1}).ToArray()})
        {using var download=new OfficialDownloads(Standard(payload:bytes));await Assert.ThrowsAsync<InvalidDataException>(()=>download.DownloadAsync(release,new MemoryStream(),new ImmediateProgress<string>(_=>{}),CancellationToken.None));}
    }
    [Fact]public async Task RequestFailureDegradesToInspectionOnly(){using var p=Provider(Fixture.Temp(),new Handler(_=>new(HttpStatusCode.Forbidden)));var checkedApps=await p.AttachAsync([App()],true,CancellationToken.None);Assert.False(checkedApps.Report.Available);Assert.False(Assert.Single(checkedApps.Apps).CanSupervise);}
    [Theory][InlineData("C:\\Program Files\\Notepad++",true)][InlineData("C:\\",false)][InlineData("\\\\server\\share\\app",false)][InlineData("C:\\App\" /S",false)][InlineData("relative\\app",false)]
    public void InstallerDestinationCannotSupplyAnotherArgument(string path,bool expected)=>Assert.Equal(expected,OfficialInstallerProvider.SafeInstallDirectory(path));
}
