namespace UpdateDeck.Core;

/// <summary>Sequential installation, durable state before every side effect, safe boundary controls.</summary>
public sealed class QueueEngine(IUpdateExecutor executor,IOperationStore store,IVendorLauncher vendors,TimeSpan? retryDelay=null)
{
    private readonly TimeSpan retryWait=retryDelay??TimeSpan.FromMilliseconds(800);
    private volatile bool paused,cancelPending;
    private readonly HashSet<string> skipped=[];
    private int running;
    public bool IsRunning=>Volatile.Read(ref running)==1;
    public bool IsPaused=>paused;
    public void Pause()=>paused=true;
    public void Resume()=>paused=false;
    public void CancelPending(){cancelPending=true; paused=false;}
    public void Skip(string appId){lock(skipped) skipped.Add(appId);}
    public async Task<IReadOnlyList<UpdateOperation>> RunAsync(QueuePlan plan,bool launchVendors,IProgress<QueueProgress> progress,Func<Software,string?> preflight,CancellationToken cancellation=default)
    {
        if(Interlocked.CompareExchange(ref running,1,0)!=0) throw new InvalidOperationException("An update queue is already active.");
        cancelPending=false; paused=false; lock(skipped) skipped.Clear();
        var results=new List<UpdateOperation>();
        var items=plan.Automatic.Concat(plan.Supervised).Concat(launchVendors?plan.Handoffs:[]).ToArray();
        try
        {
            if(store.LoadOperations().Any(x=>x.RequiresInspection))throw new InvalidOperationException("A previous installer outcome needs inspection. Open History and resolve its monitoring notice before starting another queue.");
            if(items.Select(x=>x.App.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=items.Length)throw new InvalidOperationException("The update plan contains duplicate installation operations. Refresh the plan before running it.");
            var pending=items.Select(item=>new UpdateOperation {AppId=item.App.Id,Name=item.App.Name,Provider=item.Action=="Handoff"?item.App.VendorId:item.App.Provider,PreviousVersion=item.App.InstalledVersion,TargetVersion=item.App.AvailableVersion,Installation=item.Action=="Handoff"?null:InstallationSnapshot.Capture(item.App)}).ToArray();
            foreach(var operation in pending)store.SaveOperation(operation);
            int position=0;
            foreach(var item in items)
            {
                while(paused&&!cancelPending&&!cancellation.IsCancellationRequested) await Task.Delay(100,CancellationToken.None);
                bool skip; lock(skipped) skip=skipped.Contains(item.App.Id);
                var op=pending[position++];
                // Saving a pending record must succeed before any process can start.
                store.SaveOperation(op);
                if(cancelPending||cancellation.IsCancellationRequested||skip)
                    op=op with {State=OperationState.Cancelled,Message=skip?"Skipped before execution.":"Cancelled before execution."};
                else
                {
                    var blocked=item.Action=="Handoff"?null:preflight(item.App);
                    if(blocked!=null) op=op with {State=OperationState.Deferred,Message=blocked};
                    else
                    {
                        op=op with {State=OperationState.Running,RequiresInspection=item.Action!="Handoff",Message=item.Action=="Handoff"?"Opening publisher workflow":"Starting update"};
                        store.SaveOperation(op); progress.Report(new(results.Count,items.Length,op.Message,op));
                        try
                        {
                            var detail=new Progress<string>(message=>progress.Report(new(results.Count,items.Length,message,op)));
                            // Cancellation controls pending work; never kill an active installer.
                            var result=item.Action=="Handoff"?await vendors.LaunchAsync(item.App.VendorId,CancellationToken.None):item.Action=="Supervised"?executor is ISupervisedUpdateExecutor interactive?await interactive.ExecuteSupervisedAsync(item.App,detail,CancellationToken.None):new ExecutionResult(OperationState.Deferred,"This provider has no reviewed supervised execution method."):await executor.ExecuteAsync(item.App,detail,CancellationToken.None);
                            if(item.Action!="Supervised"&&result.Transient && result.State==OperationState.Failed && !cancelPending && !result.StopQueue)
                            {
                                await Task.Delay(retryWait,CancellationToken.None);
                                while(paused&&!cancelPending&&!cancellation.IsCancellationRequested)await Task.Delay(100,CancellationToken.None);
                                bool skipRetry;lock(skipped)skipRetry=skipped.Contains(item.App.Id);
                                if(cancelPending||cancellation.IsCancellationRequested||skipRetry)
                                    result=new(OperationState.Cancelled,"The pending retry was cancelled after a transient download failure. No further installer was started.",result.ExitCode);
                                else
                                {
                                    var retryBlocked=preflight(item.App);
                                    result=retryBlocked!=null?new(OperationState.Deferred,retryBlocked):await executor.ExecuteAsync(item.App,detail,CancellationToken.None);
                                }
                            }
                            if(result.StopQueue)cancelPending=true;
                            op=op with {State=result.State,Message=result.Message,ExitCode=result.ExitCode,RebootRequired=result.RebootRequired,RequiresInspection=result.StopQueue,ActiveProcessId=result.ActiveProcessId,ActiveProcessStartedUtcTicks=result.ActiveProcessStartedUtcTicks};
                            if(item.Action!="Handoff"&&result.State==OperationState.AwaitingVerification&&!result.StopQueue)
                            {
                                op=op with {State=OperationState.Verifying,RequiresInspection=true}; store.SaveOperation(op);
                                progress.Report(new(results.Count,items.Length,"Checking the installed version",op));
                                var verified=await executor.VerifyAsync(item.App,CancellationToken.None);
                                bool uncertain=verified.State!=OperationState.Verified||verified.StopQueue;
                                op=op with {State=uncertain?OperationState.AwaitingVerification:OperationState.Verified,Message=verified.Message+(uncertain?" Pending work stopped because the installed target or installer completion could not be confirmed. Inspect publisher dialogs and child installers in History before starting another queue.":""),RequiresInspection=uncertain,RebootRequired=op.RebootRequired||verified.RebootRequired};
                                if(uncertain)cancelPending=true;
                            }
                        }
                        catch(Exception ex) { cancelPending=true;op=op with {State=OperationState.Failed,RequiresInspection=item.Action!="Handoff",Message=$"Operation stopped: {ex.Message}. Pending work stopped because the active outcome may be uncertain; inspect before retrying."}; }
                    }
                }
                op=op with {FinishedAt=DateTimeOffset.UtcNow}; store.SaveOperation(op); results.Add(op);
                progress.Report(new(results.Count,items.Length,op.Message,op));
                // A reboot result creates a safe boundary for all subsequent installers.
                if(op.RebootRequired) cancelPending=true;
            }
            return results;
        }
        finally { Interlocked.Exchange(ref running,0); paused=false; }
    }
    public static IReadOnlyList<UpdateOperation> Recover(IOperationStore store,IReadOnlyList<Software> inventory,bool pendingRestart=false)
    {
        var changed=new List<UpdateOperation>();
        foreach(var op in store.LoadOperations().Where(x=>x.State is OperationState.Running or OperationState.Pending or OperationState.Verifying))
        {
            bool inspection=op.RequiresInspection||op.State is OperationState.Running or OperationState.Verifying;
            bool targetObserved=VerifiedObservation(op,inventory,DateTimeOffset.UtcNow)!=null;
            bool restart=op.RebootRequired&&pendingRestart;
            var fixedOp=op with {State=targetObserved?inspection||restart?OperationState.AwaitingVerification:OperationState.Verified:OperationState.Interrupted,RequiresInspection=inspection,FinishedAt=DateTimeOffset.UtcNow,Message=targetObserved?inspection?"The target installed version was observed, but the earlier installer outcome remains uncertain. Inspect child installers and publisher dialogs before clearing the hold.":restart?"The target version was observed, but Windows still reports a pending restart. Restart manually and recheck.":"Reconciled with fresh installation selectors and the installed target version after interruption. No installer was rerun.":"Interrupted. Inspect the installation and rescan before retrying; nothing was rerun."};
            store.SaveOperation(fixedOp); changed.Add(fixedOp);
        }
        return changed;
    }
    public static void ReconcileMonitors(IOperationStore store,Func<UpdateOperation,bool?> stillRunning)
    {
        foreach(var op in store.LoadOperations().Where(x=>x.RequiresInspection&&!x.RecordedProcessEnded&&x.ActiveProcessId.HasValue&&x.ActiveProcessStartedUtcTicks.HasValue))
            if(stillRunning(op)==false)store.SaveOperation(op with{RecordedProcessEnded=true,Message=op.Message+" The recorded command process has ended. A child installer or publisher dialog may remain active; inspect them and confirm completion in History. This hold was not cleared automatically."});
    }
    /// <summary>Only a fresh observed installation matching the original selectors can resolve an ended, unverified operation.</summary>
    public static int ReconcileInstalledVersions(IOperationStore store,IReadOnlyList<Software> inventory,DateTimeOffset now,bool pendingRestart)
    {
        int changed=0;
        foreach(var operation in store.LoadOperations().Where(x=>(x.State is OperationState.AwaitingVerification or OperationState.Interrupted)&&!x.RequiresInspection&&(!x.RebootRequired||!pendingRestart)))
        {
            var app=VerifiedObservation(operation,inventory,now);if(app==null)continue;
            store.SaveOperation(operation with{State=OperationState.Verified,FinishedAt=now,Message=$"Fresh inventory verified installed version {app.InstalledVersion} against target {operation.TargetVersion} at the original provider, path, scope, architecture and channel. No installer was rerun."});changed++;
        }
        return changed;
    }
    private static Software? VerifiedObservation(UpdateOperation operation,IReadOnlyList<Software> inventory,DateTimeOffset now)
    {
        if(operation.Installation==null||Versioning.Compare(operation.TargetVersion,operation.PreviousVersion) is not >0)return null;
        var matches=inventory.Where(app=>app.Id==operation.AppId&&app.InventoryObservedAt.HasValue&&now-app.InventoryObservedAt.Value is var age&&age>=TimeSpan.FromMinutes(-2)&&age<=TimeSpan.FromMinutes(10)&&operation.Installation.Matches(app)&&Versioning.Compare(app.InstalledVersion,operation.TargetVersion) is >=0).Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }
}
