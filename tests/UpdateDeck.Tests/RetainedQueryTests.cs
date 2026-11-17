using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class RetainedQueryTests
{
    [Fact]public async Task CallerCancellationCannotReleaseOwnershipOfNativeWork()
    {
        var query=new RetainedQuery<int>();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);int leases=0,calls=0;
        Assert.True(query.TryStart(async()=>{Interlocked.Increment(ref leases);entered.SetResult();try{return await finish.Task;}finally{Interlocked.Decrement(ref leases);}},out var first));await entered.Task;
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first.WaitAsync(cancel.Token));
        Assert.False(query.TryStart(()=>{calls++;return Task.FromResult(2);},out var skipped));Assert.Same(first,skipped);Assert.Equal(1,leases);Assert.Equal(0,calls);
        finish.SetResult(1);Assert.Equal(1,await first);Assert.Equal(0,leases);
        Assert.True(query.TryStart(()=>{calls++;return Task.FromResult(2);},out var next));Assert.Equal(2,await next);Assert.Equal(1,calls);
    }
    [Fact]public async Task SynchronousNativeFailureDoesNotPermanentlyLockTheSource()
    {
        var query=new RetainedQuery<int>();Assert.True(query.TryStart(()=>throw new IOException("fixture native failure"),out var failed));await Assert.ThrowsAsync<IOException>(()=>failed);
        Assert.True(query.TryStart(()=>Task.FromResult(3),out var recovered));Assert.Equal(3,await recovered);
    }
    [Fact]public async Task CallerTimeoutDoesNotStartAnotherOperationOrDisposeItsLease()
    {
        var query=new RetainedQuery<int>();var finish=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);Assert.True(query.TryStart(()=>finish.Task,out var work));
        await Assert.ThrowsAsync<TimeoutException>(()=>work.WaitAsync(TimeSpan.FromMilliseconds(30)));Assert.False(query.TryStart(()=>Task.FromResult(9),out _));finish.SetResult(1);Assert.Equal(1,await work);
    }
}
