using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public sealed class RecordingRunner : IProcessRunner
{
    public List<string[]> Arguments=[];
    public Task<ProcessResult> RunAsync(string executable,IEnumerable<string> arguments,TimeSpan timeout,CancellationToken cancellation,ProcessPurpose purpose){var args=arguments.ToArray();Arguments.Add(args);return Task.FromResult(args[0] switch{"pin"=>new ProcessResult(0,"There are no pins configured.",""),"source"=>new ProcessResult(0,"{\"Name\":\"winget\",\"Arg\":\"https://cdn.winget.microsoft.com/cache\",\"Type\":\"Microsoft.PreIndexed.Package\"}",""),_=>new ProcessResult(0,"fixture update", "")});}
}
public class WindowsTests
{
    private sealed class FixtureSource(bool hangs) : IInventorySource
    {
        public string Name=>hangs?"Unresponsive fixture":"Healthy fixture";
        public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>hangs?new TaskCompletionSource<DiscoveryResult>().Task:Task.FromResult(new DiscoveryResult([Fixture.App()],[new(Name,true,ProviderCapability.Inventory,"fixture",DateTimeOffset.UtcNow)]));
    }
    [Fact]public async Task UnresponsiveProviderCannotBlockHealthyInventory(){var c=new InventoryCoordinator([new FixtureSource(true),new FixtureSource(false)],new VendorLaunchers(),new StateStore(Fixture.Temp()),TimeSpan.FromMilliseconds(40));var result=await c.ScanAsync(false,new ImmediateProgress<string>(_=>{}),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));Assert.Single(result.Apps);Assert.Contains(result.Providers,x=>x.Name=="Unresponsive fixture"&&!x.Available);Assert.Contains(result.Providers,x=>x.Name=="Healthy fixture"&&x.Available);}
    [Theory][InlineData(0,OperationState.AwaitingVerification)][InlineData(3010,OperationState.AwaitingVerification)][InlineData(unchecked((int)0x8A150041),OperationState.InteractionRequired)][InlineData(unchecked((int)0x8A150046),OperationState.InteractionRequired)][InlineData(unchecked((int)0x8A150011),OperationState.Failed)][InlineData(unchecked((int)0x800704C7),OperationState.InteractionRequired)]
    public void ExitCodesHaveAccurateStates(int code,OperationState expected)=>Assert.Equal(expected,WinGetProvider.InterpretExit(new(code,"","")).State);
    [Fact]public void PinAdapterFailsClosedOnUnexpectedOrMalformedOutput(){Assert.False(WinGetProvider.ParsePins(new(0,"No se encontraron pines","")).Verified);Assert.False(WinGetProvider.ParsePins(new(1,"There are no pins configured.","")).Verified);Assert.True(WinGetProvider.ParsePins(new(0,"There are no pins configured.","")).Verified);var pins=WinGetPins.Parse(new(0,"Name    Id    Version    Source    Pin type    Pinned version\n------------------------------------------------------------\nExample App Example.One  1.0  winget  Blocking\n",""),["Example.One"]);Assert.True(pins.Verified);Assert.Contains("Example.One",pins.Ids);Assert.False(WinGetPins.Parse(new(0,"Name    Id    Version    Source    Pin type    Pinned version\n------------------------------------------------------------\nExample App  Example…  1.0  winget  Blocking\n",""),["Example.One"]).Verified);}
    [Fact]public async Task ExactUpgradeArgumentsNeverIncludeBypassOrGlobalFlags(){var r=new RecordingRunner();var app=Fixture.App();var provider=new WinGetProvider(r,()=>"winget-fixture.exe",_=>Task.FromResult<IReadOnlyList<Software>>([app]),_=>null);var result=await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.AwaitingVerification,result.State);var args=r.Arguments.Last();Assert.Contains("--exact",args);Assert.Contains(app.PackageId,args);Assert.Contains(app.AvailableVersion,args);Assert.Contains("--scope",args);Assert.DoesNotContain("--all",args);Assert.DoesNotContain("--force",args);Assert.DoesNotContain("--accept-package-agreements",args);Assert.DoesNotContain("--ignore-security-hash",args);}
    [Fact]public async Task InvalidPackageIdCannotBecomeAnArgumentInjection(){var r=new RecordingRunner();var p=new WinGetProvider(r);var result=await p.ExecuteAsync(Fixture.App() with{PackageId="--all & calc.exe"},new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.Deferred,result.State);Assert.Empty(r.Arguments);}
    [Theory][InlineData("--force")][InlineData("0.9")][InlineData("Unknown")]
    public async Task InvalidTargetVersionNeverReachesAProviderProcess(string target){var r=new RecordingRunner();var p=new WinGetProvider(r,()=>"winget-fixture.exe");var result=await p.ExecuteAsync(Fixture.App() with{AvailableVersion=target},new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.Deferred,result.State);Assert.Empty(r.Arguments);}
    [Fact]public void RenamedUnsignedExecutableCannotImpersonateWinGet(){var path=Path.Combine(Fixture.Temp(),"winget.exe");File.WriteAllText(path,"not a Microsoft executable");Assert.False(WinGetCliLocator.IsMicrosoftExecutable(path));}
    [Theory][InlineData("https://evil.example/setup.exe",false)][InlineData("file:///C:/Windows/System32/cmd.exe",false)][InlineData("ms-windows-store://downloadsandupdates",true)][InlineData("https://www.nvidia.com/en-us/software/nvidia-app/",true)][InlineData("https://www.nvidia.com.evil.example/",false)]
    public void VendorHandoffsUseExactReviewedDestinations(string url,bool expected)=>Assert.Equal(expected,VendorLaunchers.IsReviewedUrl(url));
    [Fact]public void SourceValidationRejectsRemappedRepository(){Assert.False(WinGetProvider.TrustedSource(new(0,"{\"Name\":\"winget\",\"Arg\":\"https://evil.example/cache\",\"Type\":\"Microsoft.PreIndexed.Package\"}","")));Assert.False(WinGetProvider.TrustedSource(new(0,"not json", "")));}
    [Fact]public async Task RegistryIntegrationIsReadOnlyAndPortable(){var r=await new RegistryInventory().DiscoverAsync(false,CancellationToken.None);Assert.True(Assert.Single(r.Providers).Available);Assert.NotEmpty(r.Apps);Assert.All(r.Apps,a=>Assert.NotEmpty(a.Id));}
    [Fact]public async Task HarmlessInstallerProcessAndVerificationUseUnicodePaths()
    {
        var directory=Path.Combine(Fixture.Temp(),"Unicode Ω folder with spaces");Directory.CreateDirectory(directory);var file=Path.Combine(directory,"version.txt");
        var helper=Path.Combine(AppContext.BaseDirectory,"helpers","UpdateDeck.TestInstaller.dll");Assert.True(File.Exists(helper));
        var runner=new ProcessRunner();var dotnet=ProcessRunner.FindExecutable("dotnet.exe")!;
        var failure=await runner.RunAsync(dotnet,[helper,file,"2.0","23"],TimeSpan.FromSeconds(20),CancellationToken.None,ProcessPurpose.Installer);Assert.Equal(23,failure.ExitCode);Assert.False(File.Exists(file));
        var success=await runner.RunAsync(dotnet,[helper,file,"2.0","0"],TimeSpan.FromSeconds(20),CancellationToken.None,ProcessPurpose.Installer);Assert.Equal(0,success.ExitCode);Assert.Equal("2.0",File.ReadAllText(file));
    }
}
