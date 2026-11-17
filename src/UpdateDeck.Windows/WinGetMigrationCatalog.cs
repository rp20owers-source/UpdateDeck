using Microsoft.Management.Deployment;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

/// <summary>Metadata-only preview against the built-in community catalog on this PC. Never installs or downloads packages.</summary>
public sealed class WinGetMigrationCatalog : IMigrationCatalog
{
    private static int querying;
    public async Task<CatalogPreview> ResolveAsync(string packageId,string preferredScope,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!PackageIdentifiers.IsWinGet(packageId))return new(packageId,CatalogPreviewState.NeedsReview,Message:"The saved package ID cannot be queried safely.");
        if(Interlocked.CompareExchange(ref querying,1,0)!=0)return new(packageId,CatalogPreviewState.Unavailable,Message:"An earlier catalog query is still finishing. Close this preview and try later.",StopBatch:true);
        // A native operation can outlive cancellation of its caller. Keep its pin-storage lease and
        // single-flight ownership until the actual work ends; do not pile up replacement queries.
        var work=Task.Run(async()=>
        {
            try{return await ResolveCore(packageId,preferredScope,cancellation);}
            finally{Volatile.Write(ref querying,0);}
        },CancellationToken.None);
        _=work.ContinueWith(t=>_ = t.Exception,CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
        try{return await work.WaitAsync(TimeSpan.FromSeconds(90),cancellation);}
        catch(TimeoutException){return new(packageId,CatalogPreviewState.Unavailable,Message:"The catalog query exceeded 90 seconds. Remaining checks stopped; try later.",StopBatch:true);}
    }
    private static async Task<CatalogPreview> ResolveCore(string id,string preferredScope,CancellationToken cancellation)
    {
        using var pins=WinGetPinStorage.Acquire();
        if(!pins.Safe)return new(id,CatalogPreviewState.Unavailable,Message:"Existing WinGet pin storage needs inspection. It was not modified.",StopBatch:true);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var manager=new PackageManager();
            var reference=manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);reference.AcceptSourceAgreements=false;
            var connection=await reference.ConnectAsync();cancellation.ThrowIfCancellationRequested();
            if(connection.Status!=ConnectResultStatus.Ok)return new(id,CatalogPreviewState.Unavailable,Message:$"Catalog connection: {connection.Status}. Review any required agreements in WinGet yourself.",StopBatch:true);
            var options=new FindPackagesOptions{ResultLimit=2};options.Filters.Add(new PackageMatchFilter{Field=PackageMatchField.Id,Option=PackageFieldMatchOption.EqualsCaseInsensitive,Value=id});
            var found=await connection.PackageCatalog.FindPackagesAsync(options);cancellation.ThrowIfCancellationRequested();
            if(found.Status!=FindPackagesResultStatus.Ok)return new(id,CatalogPreviewState.Unavailable,Message:$"Catalog search: {found.Status}.",StopBatch:true);
            if(found.Matches.Count==0)return new(id,CatalogPreviewState.NotFound,Message:"This exact ID was not found in the community catalog. Use its publisher or established manager.");
            if(found.Matches.Count!=1||found.WasLimitExceeded||!found.Matches[0].CatalogPackage.Id.Equals(id,StringComparison.OrdinalIgnoreCase))return new(id,CatalogPreviewState.NeedsReview,Message:"The source did not identify exactly one matching package.");
            var version=found.Matches[0].CatalogPackage.DefaultInstallVersion;
            if(version==null)return new(id,CatalogPreviewState.NoApplicableInstaller,Message:"No default release is available for this PC.");
            var install=new InstallOptions{PackageInstallScope=preferredScope switch{"User"=>PackageInstallScope.User,"Machine"=>PackageInstallScope.System,_=>PackageInstallScope.Any}};
            var installer=version.GetApplicableInstaller(install);cancellation.ThrowIfCancellationRequested();
            DateTimeOffset? updated=version.PackageCatalog?.Info?.LastUpdateTime;
            if(installer==null)return new(id,CatalogPreviewState.NoApplicableInstaller,Version:version.Version,Message:"The catalog found no applicable installer for this PC and requested scope.",CatalogUpdatedAt:updated);
            string architecture=installer.Architecture.ToString(),scope=installer.Scope.ToString(),type=installer.InstallerType.ToString();
            bool fresh=CatalogFreshness.IsFresh(updated,DateTimeOffset.UtcNow),known=architecture is "X64" or "X86" or "Arm64" or "Neutral";
            return new(id,fresh&&known?CatalogPreviewState.Applicable:CatalogPreviewState.NeedsReview,version.Version,architecture,scope,InstallerType:type,CatalogUpdatedAt:updated,
                Message:fresh&&known?"Catalog metadata reports an applicable installer for this PC. Review publisher, license, dependencies and compatibility before obtaining it separately.":"An installer was reported, but catalog freshness or architecture could not be verified. Recheck in WinGet before proceeding.");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){return new(id,CatalogPreviewState.Unavailable,Message:$"Catalog metadata could not be checked ({ex.GetType().Name}). No installer was started.",StopBatch:true);}
    }
}
