using System.Diagnostics;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class ProcessMonitoringTests
{
    private static string Dotnet=>ProcessRunner.FindExecutable("dotnet.exe")!;
    private static string Helper=>Path.Combine(AppContext.BaseDirectory,"helpers","UpdateDeck.TestInstaller.dll");
    private static string[] Args(string mode,string path)=>[Helper,"--process-fixture",mode,path];
    private static async Task WaitForFile(string path)
    {for(int i=0;i<60&&!File.Exists(path);i++)await Task.Delay(50);Assert.True(File.Exists(path),"The harmless fixture should finish naturally.");}
    [Fact]public async Task InstallerIntentSurvivesAShortMonitoringTimeoutWithoutTermination()
    {
        string marker=Path.Combine(Fixture.Temp(),"installer-completed.txt");
        var result=await new ProcessRunner().RunAsync(Dotnet,Args("delayed-write",marker),TimeSpan.FromMilliseconds(100),CancellationToken.None,ProcessPurpose.Installer);
        Assert.True(result.TimedOut);Assert.NotNull(result.ProcessId);Assert.NotNull(result.StartedUtcTicks);Assert.False(result.WasCancelled);
        await WaitForFile(marker);Assert.Equal("child completed",File.ReadAllText(marker));
    }
    [Fact]public async Task CancellationEndsMonitoringWithoutKillingAnInstaller()
    {
        string marker=Path.Combine(Fixture.Temp(),"after-cancellation.txt");using var cancel=new CancellationTokenSource(100);
        var result=await new ProcessRunner().RunAsync(Dotnet,Args("delayed-write",marker),TimeSpan.FromSeconds(10),cancel.Token,ProcessPurpose.Installer);
        Assert.True(result.WasCancelled);Assert.True(result.TimedOut);await WaitForFile(marker);
    }
    [Fact]public async Task CancelledBeforeStartNeverLaunchesAProcess()
    {
        string marker=Path.Combine(Fixture.Temp(),"never-started.txt");using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ProcessRunner().RunAsync(Dotnet,Args("delayed-write",marker),TimeSpan.FromSeconds(10),cancel.Token,ProcessPurpose.Installer));Assert.False(File.Exists(marker));
    }
    [Fact]public async Task ReadOnlyTimeoutTerminatesOnlyTheOwnedQuery()
    {
        string marker=Path.Combine(Fixture.Temp(),"query-must-not-complete.txt");
        var result=await new ProcessRunner().RunAsync(Dotnet,Args("delayed-write",marker),TimeSpan.FromMilliseconds(100),CancellationToken.None,ProcessPurpose.ReadOnly);
        Assert.True(result.TimedOut);await Task.Delay(1100);Assert.False(File.Exists(marker));
    }
    [Fact]public async Task ParentExitWithInheritedPipesCannotHangOrClaimInstallerCompletion()
    {
        string marker=Path.Combine(Fixture.Temp(),"inherited-child.txt");var watch=Stopwatch.StartNew();
        var result=await new ProcessRunner(TimeSpan.FromMilliseconds(100)).RunAsync(Dotnet,Args("inherited-pipes",marker),TimeSpan.FromSeconds(10),CancellationToken.None,ProcessPurpose.Installer).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(result.OutputIncomplete);Assert.False(result.TimedOut);Assert.Null(result.ProcessId);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(3));
        var interpreted=WinGetProvider.InterpretExit(result);Assert.True(interpreted.StopQueue);Assert.Equal(OperationState.AwaitingVerification,interpreted.State);await WaitForFile(marker);
    }
    [Fact]public async Task BoundedTruncatedOutputCannotAuthorizePinsOrSources()
    {
        var result=await new ProcessRunner().RunAsync(Dotnet,Args("large-output",Path.Combine(Fixture.Temp(),"unused.txt")),TimeSpan.FromSeconds(10),CancellationToken.None,ProcessPurpose.ReadOnly);
        Assert.Equal(0,result.ExitCode);Assert.True(result.OutputTruncated);Assert.InRange(result.Output.Length,1,32768);Assert.False(WinGetPins.Parse(result).Verified);
        Assert.False(WinGetPins.Parse(new(0,"There are no pins configured.","",OutputIncomplete:true)).Verified);
        Assert.False(WinGetProvider.TrustedSource(new(0,"{\"Name\":\"winget\",\"Arg\":\"https://cdn.winget.microsoft.com/cache\",\"Type\":\"Microsoft.PreIndexed.Package\"}","",OutputIncomplete:true)));
    }
    [Fact]public async Task UncertainActiveInstallerIsNeverSentToAVerifierEvenIfItWouldReportSuccess()
    {
        var executor=new SimulatedExecutor{Verification=OperationState.Verified};executor.Outcomes["one"]=new([new(OperationState.AwaitingVerification,"monitor uncertainty",StopQueue:true)]);
        var queue=new QueueEngine(executor,new StateStore(Fixture.Temp()),new SimulatedVendor());var result=await queue.RunAsync(Fixture.Plan(Fixture.App("one"),Fixture.App("two")),false,new ImmediateProgress<QueueProgress>(_=>{}),_=>null);
        Assert.Equal(0,executor.VerifyCount);Assert.Equal(OperationState.AwaitingVerification,result[0].State);Assert.True(result[0].RequiresInspection);Assert.Equal(OperationState.Cancelled,result[1].State);
    }
}
