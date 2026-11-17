using UpdateDeck.Core;
namespace UpdateDeck.Windows;

/// <summary>Keeps one source invocation alive until its work and asynchronous cancellation callbacks finish.</summary>
internal sealed class InventoryQuery
{
    private readonly CancellationTokenSource cancellation=new();
    private readonly object gate=new();
    private Task cancelling=Task.CompletedTask;
    private bool finished;
    public Task<DiscoveryResult> Work {get;}
    public Task Completion {get;}
    public InventoryQuery(IInventorySource source,bool remote)
    {
        var token=cancellation.Token;
        Work=Task.Run(()=>source.DiscoverAsync(remote,token));
        Completion=ReleaseAsync();
    }
    public void RequestCancellation()
    {
        lock(gate){if(!finished)cancelling=cancellation.CancelAsync();}
    }
    private async Task ReleaseAsync()
    {
        try{await Work;}catch(Exception){} // The foreground scan reports failures; observe late failures too.
        Task pending;
        lock(gate){finished=true;pending=cancelling;}
        try{await pending;}catch(Exception){}
        cancellation.Dispose();
    }
}
