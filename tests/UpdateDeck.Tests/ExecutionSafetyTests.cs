using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public sealed class ExecutionSafetyTests
{
    [Fact]public async Task ReviewedInteractivePlanUsesItsExplicitRouteAndIndependentVerification(){var app=Fixture.App() with{Risk=Risk.Caution,Name="Example SDK"};int reads=0;var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe",_=>Task.FromResult<IReadOnlyList<Software>>([++reads==1?app:app with{InstalledVersion=app.AvailableVersion}]),_=>null);var queue=new QueueEngine(new UpdateExecutor(provider,provider),new StateStore(Fixture.Temp()),new SimulatedVendor());var result=await queue.RunAsync(new([],[],[]){Supervised=[new(app,"Supervised","User-reviewed fixture")]},false,new ImmediateProgress<QueueProgress>(_=>{}),_=>null);Assert.Equal(OperationState.Verified,Assert.Single(result).State);Assert.Equal(2,reads);Assert.Contains("--interactive",runner.Arguments.Last());Assert.DoesNotContain("--silent",runner.Arguments.Last());}
    [Fact]public async Task SupervisedWinGetUsesInteractiveModeWithoutSilentOrAgreementFlags(){var app=Fixture.App() with{Risk=Risk.Caution,Name="Example SDK"};var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe",_=>Task.FromResult<IReadOnlyList<Software>>([app]),_=>null);Assert.Equal(OperationState.AwaitingVerification,(await provider.ExecuteSupervisedAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);var arguments=runner.Arguments.Last();Assert.Contains("--interactive",arguments);Assert.DoesNotContain("--silent",arguments);Assert.DoesNotContain("--accept-package-agreements",arguments);Assert.Contains("--exact",arguments);Assert.Contains("--scope",arguments);Assert.Contains("--architecture",arguments);}
    [Fact]public void SupervisedEligibilityDoesNotMakeCautionSoftwareAutomatic(){var app=Fixture.App() with{Risk=Risk.Caution,Name="Example SDK"};Assert.True(app.CanSupervise);Assert.False(app.CanUpdate);Assert.Empty(UpdatePolicy.Plan([app],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue).Automatic);Assert.Null(UpdatePolicy.BlockReason(app,new(),DateTimeOffset.UtcNow,false,false,long.MaxValue,true));}
    [Theory][InlineData("Firmware utility")][InlineData("Graphics Driver")][InlineData("Oracle VirtualBox")][InlineData("Tailscale")]
    public async Task InteractiveModeCannotOverrideSystemSensitiveOrHighRiskClassification(string name){var app=Fixture.App() with{Name=name,Risk=Risk.Caution};var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe");Assert.Equal(OperationState.Deferred,(await provider.ExecuteSupervisedAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);Assert.Empty(runner.Arguments);}
    [Theory][InlineData("Intel;1033","X86")][InlineData(";1033","X86")][InlineData("x64;1033","X64")][InlineData("Arm64;0","Arm64")][InlineData("Intel64;1033",null)][InlineData("Intel,x64;1033",null)][InlineData("",null)]
    public void MsiPlatformMetadataDoesNotConfuseItaniumOrMixedPlatforms(string template,string? expected)=>Assert.Equal(expected,MsiMetadata.ArchitectureFromTemplate(template));
    [Theory][InlineData("Mozilla","Mozilla Corporation","Firefox",true)][InlineData("Example, Inc.","Example LLC","Example App",true)][InlineData("Notepad++ Team","Don HO don.h@free.fr","Notepad++",true)][InlineData("Notepad++ Team","Unknown Publisher","Notepad++",false)][InlineData("Microsoft","Microsoftee Inc.","Example",false)]
    public void ReviewedPublisherTextRelationshipsDoNotReplaceSignatureValidation(string publisher,string company,string product,bool expected)=>Assert.Equal(expected,ExecutableMetadata.PublisherTextMatches(publisher,company,product));
    [Theory][InlineData("scope")][InlineData("architecture")][InlineData("publisher")][InlineData("path")]
    public void FreshInstalledEvidenceRejectsChangedInstallation(string changed)
    {
        var app=Fixture.App();var installed=changed switch{"scope"=>app with{Scope="Machine"},"architecture"=>app with{Architecture="Arm64"},"publisher"=>app with{Publisher="Different Publisher"},_=>app with{InstallLocation=app.InstallLocation+"-other"}};
        Assert.Empty(WinGetProvider.MatchingInstallations(app,[installed]));
    }
    [Theory][InlineData("changed")][InlineData("removed")][InlineData("duplicate")]
    public async Task CurrentVersionIsRevalidatedImmediatelyBeforeUpgrade(string mode)
    {
        var app=Fixture.App();IReadOnlyList<Software> installed=mode switch{"removed"=>[],"duplicate"=>[app,app with{Id="other"}],_=>[app with{InstalledVersion="1.2.0"}]};
        var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe",_=>Task.FromResult(installed),_=>null);
        var result=await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);
        Assert.Equal(OperationState.Deferred,result.State);Assert.DoesNotContain(runner.Arguments,x=>x[0]=="upgrade");
    }
    [Fact]public async Task BoundaryChangesDuringSourceValidationPreventUpgrade(){var app=Fixture.App();var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe",_=>Task.FromResult<IReadOnlyList<Software>>([app]),_=>"Application started while source was being checked");var result=await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Equal(OperationState.Deferred,result.State);Assert.DoesNotContain(runner.Arguments,x=>x[0]=="upgrade");}
    [Theory][InlineData("architecture")][InlineData("location")][InlineData("channel")]
    public async Task FreshEvidenceCannotLoseKnownArchitecturePathOrChangeChannel(string field)
    {
        var app=Fixture.App();var fresh=field switch{"architecture"=>app with{Architecture="Unknown"},"location"=>app with{InstallLocation=""},_=>app with{InstalledChannel="beta"}};
        var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe",_=>Task.FromResult<IReadOnlyList<Software>>([fresh]),_=>null);
        Assert.Equal(OperationState.Deferred,(await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);Assert.DoesNotContain(runner.Arguments,x=>x[0]=="upgrade");
    }
    [Theory][InlineData(true)][InlineData(false)]public async Task FreshConflictingVersionEvidenceCannotReachAnUpgrade(bool flag)
    {
        var app=Fixture.App();var fresh=app with{VersionEvidenceConflict=flag,State=flag?UpdateState.Available:UpdateState.Attention};var runner=new RecordingRunner();
        var provider=new WinGetProvider(runner,()=>"fixture-winget.exe",_=>Task.FromResult<IReadOnlyList<Software>>([fresh]),_=>null);
        Assert.Equal(OperationState.Deferred,(await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);Assert.DoesNotContain(runner.Arguments,x=>x[0]=="upgrade");
    }
    [Theory][InlineData("scope")][InlineData("architecture")][InlineData("future")][InlineData("channel")]
    public async Task UncertainIdentityOrClockCannotReachProviderProcess(string condition){var app=Fixture.App();app=condition switch{"scope"=>app with{Scope="Unknown"},"architecture"=>app with{Architecture="Unknown"},"future"=>app with{CheckedAt=DateTimeOffset.UtcNow.AddHours(1)},_=>app with{InstalledChannel="beta",AvailableChannel="stable"}};var runner=new RecordingRunner();var provider=new WinGetProvider(runner,()=>"fixture-winget.exe");Assert.Equal(OperationState.Deferred,(await provider.ExecuteAsync(app,new ImmediateProgress<string>(_=>{}),CancellationToken.None)).State);Assert.Empty(runner.Arguments);}
    [Theory][InlineData(1618)][InlineData(unchecked((int)0x8A150102))]
    public void AnotherInstallerInProgressCreatesMonitoringHold(int code)=>Assert.True(WinGetProvider.InterpretExit(new(code,"","")).StopQueue);
    [Fact]public async Task VerificationUsesANewInstalledSnapshotRatherThanTheExecutionExitCode(){var app=Fixture.App();int reads=0;var provider=new WinGetProvider(new RecordingRunner(),()=>"fixture-winget.exe",_=>{reads++;return Task.FromResult<IReadOnlyList<Software>>([app with{InstalledVersion=app.AvailableVersion}]);},_=>null);Assert.Equal(OperationState.Verified,(await provider.VerifyAsync(app,CancellationToken.None)).State);Assert.Equal(1,reads);}
}
