using UpdateDeck.Core;
using Windows.Management.Deployment;
namespace UpdateDeck.Windows;

public sealed class StoreInventory : IInventorySource
{
    public string Name=>"Microsoft Store";
    public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>Task.Run(()=>
    {
        var apps=new List<Software>();
        try
        {
            var manager=new PackageManager();
            foreach(var p in manager.FindPackagesForUser(""))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if(p.IsFramework||p.IsResourcePackage||p.IsBundle||p.IsOptional||p.SignatureKind==global::Windows.ApplicationModel.PackageSignatureKind.System)continue;
                    // Packages without launchable entries are dependencies / infrastructure, not ordinary apps.
                    var entries=p.GetAppListEntriesAsync().AsTask(cancellation).WaitAsync(TimeSpan.FromSeconds(3),cancellation).GetAwaiter().GetResult();
                    if(entries.Count==0)continue;
                    var v=p.Id.Version;
                    bool storeSigned=p.SignatureKind==global::Windows.ApplicationModel.PackageSignatureKind.Store;
                    apps.Add(new Software {Id=Software.Key("appx|"+p.Id.FamilyName),Name=string.IsNullOrWhiteSpace(p.DisplayName)?p.Id.Name:p.DisplayName,Publisher=p.PublisherDisplayName,InstalledVersion=$"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",FamilyName=p.Id.FamilyName,InstallLocation=p.InstalledLocation.Path,Architecture=p.Id.Architecture.ToString(),Scope="User",InstallerType="MSIX/AppX",Provider=storeSigned?"Store":"MSIX",State=storeSigned?UpdateState.VendorManaged:UpdateState.Unknown,VendorId=storeSigned?"store":"",Evidence=["PackageManager.FindPackagesForUser",$"Package signature kind: {p.SignatureKind}"],Explanation=storeSigned?"Store-signed application. Open Microsoft Store Downloads and recheck afterward; signing alone does not guarantee Store update availability.":"This package was not signed by Microsoft Store. Use its publisher or organization deployment channel; Store ownership has not been assumed."});
                }
                catch(Exception ex) when(ex is not OperationCanceledException){/* An inaccessible package must not abort independent discovery. */}
            }
            return new DiscoveryResult(apps,[new(Name,true,ProviderCapability.Inventory|ProviderCapability.Handoff,$"{apps.Count} launchable user packages. Store update control is a handoff.",DateTimeOffset.UtcNow)]);
        }
        catch(Exception ex) when(ex is not OperationCanceledException){return new DiscoveryResult(apps,[new(Name,false,ProviderCapability.None,$"Store inventory unavailable: {ex.Message}",DateTimeOffset.UtcNow)]);}
    },cancellation);
}
