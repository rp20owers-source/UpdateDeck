using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class MaintenanceConcurrencyTests
{
    [Fact]public async Task AnotherCommandCannotEnterBetweenRefreshAndInstallation()
    {
        var gate=new MaintenanceGate();var refreshed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releasePlan=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int executions=0,otherStarts=0;
        var first=gate.TryRunAsync(async()=>{refreshed.SetResult();await releasePlan.Task;Interlocked.Increment(ref executions);});await refreshed.Task;
        Assert.False(await gate.TryRunAsync(()=>{otherStarts++;return Task.CompletedTask;}));Assert.True(gate.IsActive);releasePlan.SetResult();Assert.True(await first);Assert.Equal(1,executions);Assert.Equal(0,otherStarts);Assert.False(gate.IsActive);
    }
    [Fact]public async Task FailedRefreshReleasesTheFlowForARealRetry()
    {
        var gate=new MaintenanceGate();await Assert.ThrowsAsync<IOException>(()=>gate.TryRunAsync(()=>throw new IOException("owned refresh failure")));int retries=0;Assert.True(await gate.TryRunAsync(()=>{retries++;return Task.CompletedTask;}));Assert.Equal(1,retries);Assert.False(gate.IsActive);
    }
}
