namespace UpdateDeck.Core;

/// <summary>Owns native work until its real completion, independently of a caller's bounded wait.</summary>
public sealed class RetainedQuery<T>
{
    private readonly object gate=new();
    private Task<T>? running;
    public bool TryStart(Func<Task<T>> operation,out Task<T> work)
    {
        lock(gate)
        {
            if(running is {IsCompleted:false}){work=running;return false;}
            running=Task.Run(operation);work=running;
            _=running.ContinueWith(t=>_ = t.Exception,CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
            return true;
        }
    }
}
