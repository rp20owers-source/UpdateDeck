using UpdateDeck.Core;
namespace UpdateDeck.Windows;

public sealed class InventoryCoordinator(IReadOnlyList<IInventorySource> sources,VendorLaunchers vendors,StateStore store,TimeSpan? providerTimeout=null,OfficialInstallerProvider? official=null)
{
    private readonly object queryGate=new();
    private readonly Dictionary<IInventorySource,InventoryQuery> queries=new(ReferenceEqualityComparer.Instance);
    public async Task<DiscoveryResult> ScanAsync(bool checkUpdates,IProgress<string> progress,CancellationToken cancellation)
    {
        progress.Report(checkUpdates?"Reading installed applications and checking the official catalog":"Reading installed applications");
        var tasks=sources.Select(source=>QuerySourceAsync(source,checkUpdates,cancellation));
        var results=await Task.WhenAll(tasks);progress.Report("Reconciling installation identities and official managers");
        store.Log("inventory.resolve","Starting installation reconciliation");
        var apps=IdentityResolver.Resolve(results.SelectMany(x=>x.Apps).Select(GamePlatforms.Categorize));
        // Manager ownership is positive path evidence; a name resemblance alone only blocks, never merges.
        var owned=apps.Where(x=>x.Provider is "Scoop" or "Chocolatey" or "Steam" or "Epic").ToArray();
        apps=apps.Select(app=>
        {
            if(app.Provider!="WinGet")return app;
            var conflict=owned.FirstOrDefault(x=>x.Name.Equals(app.Name,StringComparison.OrdinalIgnoreCase)||app.InstallLocation.Length>0&&app.InstallLocation.StartsWith(x.InstallLocation.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase));
            return conflict==null?app:app with{Kind=GamePlatforms.IsGaming(conflict)?GamePlatforms.Categorize(conflict).Kind:app.Kind,VendorId=GamePlatforms.IsGaming(conflict)?conflict.VendorId:app.VendorId,State=UpdateState.Attention,Ambiguous=true,Explanation=$"Possible {conflict.Provider} ownership. Update in the established manager; provider migration is disabled."};
        }).ToArray();
        apps=apps.Select(GamePlatforms.Categorize).ToArray();
        var prior=store.LoadInventory();
        apps=InstallationIdentity.Continue(apps,prior);
        var priorById=prior.GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.First());
        apps=apps.Select(app=>ManagerOwnership.PreserveWhenUnverified(app,priorById.GetValueOrDefault(app.Id))).ToArray();
        apps=apps.Select(app=>!app.PinVerified&&priorById.TryGetValue(app.Id,out var old)&&old.Pinned?app with{Pinned=true,State=UpdateState.Attention,Explanation="This installation was previously pinned. Fresh pin verification is unavailable, so its update hold is preserved."}:app).ToArray();
        store.Log("inventory.vendors","Validating reviewed manager handoffs");
        apps=await Task.Run(()=>vendors.Attach(apps,message=>store.Log("vendor.trust",message)),cancellation);
        ProviderReport? officialReport=null;
        if(official!=null)
        {
            store.Log("inventory.official","Checking uncovered installations against the reviewed publisher catalog");
            var checkedOfficial=await official.AttachAsync(apps,checkUpdates,cancellation);apps=checkedOfficial.Apps;officialReport=checkedOfficial.Report;
        }
        apps=InstallationIdentity.Continue(apps,prior);
        var cached=prior.GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.First());
        apps=apps.Select(x=>
        {
            if(x.State==UpdateState.Unknown&&x.CheckedAt==null&&cached.TryGetValue(x.Id,out var prior)&&prior.CheckedAt.HasValue&&Versioning.Compare(x.InstalledVersion,prior.InstalledVersion)==0)
                return x with {Risk=UpdatePolicy.Classify(x),AvailableVersion=prior.AvailableVersion,PackageId=prior.PackageId,CheckedAt=prior.CheckedAt,CatalogUpdatedAt=prior.CatalogUpdatedAt,Explanation=$"Cached catalog result from {prior.CheckedAt.Value.ToLocalTime():g}. Fresh verification is unavailable; automatic updates are disabled."};
            var risk=UpdatePolicy.Classify(x);
            return x with {Risk=risk,Explanation=x.State==UpdateState.Available&&risk!=Risk.Routine?x.Explanation+" A supervised update is recommended for this compatibility-sensitive application; use the publisher's workflow.":x.Explanation};
        }).ToArray();
        var reports=results.SelectMany(x=>x.Providers).Concat(officialReport==null?[]:[officialReport]).ToArray();
        store.Log("inventory.persist","Saving reconciled inventory");store.SaveInventory(apps);store.SaveReports(reports);store.Log("scan",$"{apps.Count} applications; {reports.Count(x=>x.Available)} available providers; online check={checkUpdates}");
        return new(apps,reports);
    }
    private async Task<DiscoveryResult> QuerySourceAsync(IInventorySource source,bool remote,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();InventoryQuery query;
        lock(queryGate)
        {
            if(queries.TryGetValue(source,out var previous)&&!previous.Completion.IsCompleted)
            {
                store.Log("provider.busy",source.Name);
                return new([],[new(source.Name,false,ProviderCapability.None,"An earlier source query is still finishing. This scan skipped another invocation; independent sources remain available.",DateTimeOffset.UtcNow)]);
            }
            query=new(source,remote);queries[source]=query;
        }
        var watch=System.Diagnostics.Stopwatch.StartNew();store.Log("provider.start",source.Name);
        try
        {
            var result=await query.Work.WaitAsync(providerTimeout??TimeSpan.FromSeconds(110),cancellation);
            store.Log("provider.complete",$"{source.Name}; records={result.Apps.Count}; elapsed_ms={watch.ElapsedMilliseconds}");
            var observed=DateTimeOffset.UtcNow;return result with{Apps=result.Apps.Select(app=>app with{InventoryObservedAt=observed}).ToArray()};
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){query.RequestCancellation();throw;}
        catch(Exception ex)
        {
            query.RequestCancellation();store.Log("provider.failure",$"{source.Name}; {ex.GetType().Name}; elapsed_ms={watch.ElapsedMilliseconds}");
            return new([],[new(source.Name,false,ProviderCapability.None,ex is TimeoutException or OperationCanceledException?"This source did not finish within its scan limit. Independent discovery remains available.":ex.Message,DateTimeOffset.UtcNow)]);
        }
    }
}
