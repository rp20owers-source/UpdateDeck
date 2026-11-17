using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public sealed class ProcessQueueIntegrationTests
{
    private sealed class FixtureExecutor(string directory,StateStore store) : IUpdateExecutor
    {
        public int Starts,Verifications;
        public async Task<ExecutionResult> ExecuteAsync(Software app,IProgress<string> progress,CancellationToken cancellation)
        {
            Assert.All(store.LoadOperations(),x=>Assert.True(x.State is OperationState.Pending or OperationState.Running or OperationState.Failed or OperationState.Verified));
            Assert.Equal(2,store.LoadOperations().Count);Starts++;
            string helper=Path.Combine(AppContext.BaseDirectory,"helpers","UpdateDeck.TestInstaller.dll");
            var result=await new ProcessRunner().RunAsync(ProcessRunner.FindExecutable("dotnet.exe")!,[helper,Path.Combine(directory,app.Id+".txt"),app.AvailableVersion,app.Id=="failure"?"23":"0"],TimeSpan.FromSeconds(20),cancellation,ProcessPurpose.Installer);
            return new(result.ExitCode==0?OperationState.AwaitingVerification:OperationState.Failed,"Owned fixture child ended",result.ExitCode);
        }
        public Task<ExecutionResult> VerifyAsync(Software app,CancellationToken cancellation)
        {
            Verifications++;string file=Path.Combine(directory,app.Id+".txt");bool verified=File.Exists(file)&&Versioning.Compare(File.ReadAllText(file),app.AvailableVersion)==0;
            return Task.FromResult(new ExecutionResult(verified?OperationState.Verified:OperationState.AwaitingVerification,"Independent test-file version check"));
        }
    }
    [Fact]public async Task DurableQueueRunsRealOwnedChildrenAndIndependentlyVerifiesVersions()
    {
        string directory=Path.Combine(Fixture.Temp(),"Ω child process tests with spaces");Directory.CreateDirectory(directory);
        var store=new StateStore(Fixture.Temp());var executor=new FixtureExecutor(directory,store);var queue=new QueueEngine(executor,store,new SimulatedVendor());
        var result=await queue.RunAsync(Fixture.Plan(Fixture.App("failure"),Fixture.App("success")),false,new ImmediateProgress<QueueProgress>(_=>{}),_=>null);
        Assert.Equal(2,executor.Starts);Assert.Equal(1,executor.Verifications);Assert.Equal(OperationState.Failed,result[0].State);Assert.Equal(OperationState.Verified,result[1].State);
        Assert.False(File.Exists(Path.Combine(directory,"failure.txt")));Assert.Equal("1.1.0",File.ReadAllText(Path.Combine(directory,"success.txt")));Assert.All(store.LoadOperations(),x=>Assert.NotNull(x.FinishedAt));Assert.False(queue.IsRunning);
    }
}
