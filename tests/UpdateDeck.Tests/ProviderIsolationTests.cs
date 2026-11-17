using System.Diagnostics;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class ProviderIsolationTests
{
    private sealed class HungSource(bool synchronous) : IInventorySource
    {
        public string Name=>"Owned blocked source";
        public int Calls;
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Released=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);Entered.TrySetResult();
            if(synchronous)Released.Task.Wait(TimeSpan.FromSeconds(3));
            return AfterReleaseAsync();
        }
        private async Task<DiscoveryResult> AfterReleaseAsync(){await Released.Task;return new([],[new(Name,true,ProviderCapability.Inventory,"owned source returned",DateTimeOffset.UtcNow)]);}
    }
    private sealed class HealthySource : IInventorySource
    {
        public string Name=>"Healthy source";
        public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>Task.FromResult(new DiscoveryResult([Fixture.App()],[new(Name,true,ProviderCapability.Inventory,"owned fixture",DateTimeOffset.UtcNow)]));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RepeatedScansDoNotAccumulateQueriesBehindABlockedSource(bool synchronous)
    {
        var blocked=new HungSource(synchronous);var coordinator=new InventoryCoordinator([blocked,new HealthySource()],new(),new StateStore(Fixture.Temp()),TimeSpan.FromMilliseconds(60));var watch=Stopwatch.StartNew();
        try
        {
            var first=await coordinator.ScanAsync(false,new ImmediateProgress<string>(_=>{}),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));Assert.Single(first.Apps);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(2));await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second=await coordinator.ScanAsync(false,new ImmediateProgress<string>(_=>{}),CancellationToken.None);Assert.Single(second.Apps);Assert.Equal(1,blocked.Calls);Assert.Contains(second.Providers,x=>x.Name==blocked.Name&&!x.Available&&x.Message.Contains("earlier source query"));
            blocked.Released.SetResult();bool recovered=false;
            for(int i=0;i<10&&!recovered;i++){await Task.Delay(30);var next=await coordinator.ScanAsync(false,new ImmediateProgress<string>(_=>{}),CancellationToken.None);recovered=next.Providers.Any(x=>x.Name==blocked.Name&&x.Available);}
            Assert.True(recovered);Assert.True(blocked.Calls>=2);
        }
        finally{blocked.Released.TrySetResult();}
    }
}
