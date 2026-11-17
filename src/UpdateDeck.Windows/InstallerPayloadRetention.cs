namespace UpdateDeck.Windows;

/// <summary>Keeps validated payloads protected while a launched installer is still being monitored.</summary>
public static class InstallerPayloadRetention
{
    private static readonly object gate=new();
    private static readonly List<FileStream> unresolved=[];
    public static void Retain(string path,FileStream lease,Task? completion)
    {
        if(completion==null){lock(gate)unresolved.Add(lease);return;}
        _=ReleaseWhenFinishedAsync(path,lease,completion);
    }
    private static async Task ReleaseWhenFinishedAsync(string path,FileStream lease,Task completion)
    {
        try{await completion;}
        catch(Exception){lock(gate)unresolved.Add(lease);return;} // Unknown outcome: retain protection until this app exits.
        lease.Dispose();
        try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
