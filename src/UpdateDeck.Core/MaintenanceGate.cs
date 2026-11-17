namespace UpdateDeck.Core;

/// <summary>One user maintenance flow owns refresh, planning, review and execution as a single operation.</summary>
public sealed class MaintenanceGate
{
    private int active;
    public bool IsActive=>Volatile.Read(ref active)!=0;
    public async Task<bool> TryRunAsync(Func<Task> operation)
    {
        if(Interlocked.CompareExchange(ref active,1,0)!=0)return false;
        try{await operation();return true;}
        finally{Interlocked.Exchange(ref active,0);}
    }
}
