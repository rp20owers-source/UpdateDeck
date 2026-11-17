using System.Text.Json;
using Microsoft.Management.Deployment;
using UpdateDeck.Core;
using DeploymentManager=Microsoft.Management.Deployment.PackageManager;

namespace UpdateDeck.Windows;

/// <summary>Official in-process WinGet COM projection for structured inventory; exact CLI operations for execution.</summary>
public sealed class WinGetProvider(IProcessRunner process,Func<string?>? cliLocator=null,Func<CancellationToken,Task<IReadOnlyList<Software>>>? installedReader=null,Func<Software,string?>? executionBoundary=null,Func<WinGetPinStorage>? pinStorage=null) : IInventorySource,IUpdateExecutor,ISupervisedUpdateExecutor
{
    public string Name=>"WinGet";
    private readonly RetainedQuery<DiscoveryResult> catalogQuery=new();
    private IReadOnlyList<Software> census=[];
    private string? Cli()=>cliLocator!=null?cliLocator():WinGetCliLocator.Find();
    public async Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!catalogQuery.TryStart(()=>DiscoverCore(checkUpdates,cancellation),out var work))return Unavailable("An earlier native catalog query is still finishing. This invocation was skipped; try later.");
        try{return await work.WaitAsync(TimeSpan.FromSeconds(110),cancellation);}
        catch(TimeoutException){return Unavailable("The catalog query exceeded its scan limit. Native work remains supervised in the background; another query will not start until it finishes.");}
    }
    private DiscoveryResult Unavailable(string message)=>new([],[new(Name,false,ProviderCapability.None,message,DateTimeOffset.UtcNow)]);
    private async Task<DiscoveryResult> DiscoverCore(bool remote,CancellationToken cancellation)
    {
        var apps=new List<Software>();
        using var protectedPins=pinStorage?.Invoke()??WinGetPinStorage.Acquire();
        if(!protectedPins.Safe)return new(apps,[new(Name,false,ProviderCapability.None,"Existing WinGet pin storage is unreadable or uses an unreviewed schema. It was not repaired or deleted. Inspect pins in WinGet before updating.",DateTimeOffset.UtcNow)]);
        try
        {
            var manager=new DeploymentManager();
            var reference=remote?manager.CreateCompositePackageCatalog(Composite(manager)):manager.GetLocalPackageCatalog(LocalPackageCatalog.InstalledPackages);
            // Do not release protected pin storage merely because the caller stops waiting.
            // Some native COM operations take time to acknowledge cancellation. The outer
            // retained query bounds the UI wait while this lease lasts until actual completion.
            var connected=await reference.ConnectAsync();cancellation.ThrowIfCancellationRequested();
            if(connected.Status!=ConnectResultStatus.Ok)throw new IOException($"Catalog connection: {connected.Status}. Open WinGet to review any required source agreements; UpdateDeck does not accept them for you.");
            var found=await connected.PackageCatalog.FindPackagesAsync(new FindPackagesOptions());cancellation.ThrowIfCancellationRequested();
            if(found.Status!=FindPackagesResultStatus.Ok)throw new IOException($"Catalog search: {found.Status}");
            // The official COM projection exposes indexed vectors; not every vector implements IIterable.
            for(int matchIndex=0;matchIndex<found.Matches.Count;matchIndex++)
            {
                var match=found.Matches[matchIndex];
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var p=match.CatalogPackage;var installed=p.InstalledVersion;if(installed==null)continue;
                    // Dedicated Windows package discovery owns MSIX filtering, signature kind and handoffs.
                    // Reintroducing all MSIX catalog entries here would leak frameworks and system dependencies.
                    if(installed.PackageFamilyNames.Count>0)continue;
                    string Meta(PackageVersionMetadataField field){try{return installed.GetMetadata(field)??"";}catch{return "";}}
                    var available=remote?p.DefaultInstallVersion:null;
                    string latest=available?.Version??"";
                    var scope=Meta(PackageVersionMetadataField.InstalledScope).Trim().ToLowerInvariant() switch {"user"=>"User","machine"=>"Machine",_=>"Unknown"};
                    var state=UpdateState.Unknown;
                    string explanation=remote?"This registration could not be matched to a verifiable catalog release.":"Local inventory only. Check updates to verify availability.";
                    bool recognized=available!=null&&PackageIdentifiers.IsWinGet(p.Id);
                    if(remote&&recognized)
                    {
                        var comparison=Versioning.Compare(latest,installed.Version);
                        if(comparison>0&&p.IsUpdateAvailable){state=UpdateState.Available;explanation="A newer version is reported by the WinGet catalog.";}
                        else if(comparison<=0){state=UpdateState.Current;explanation="Installed version is at least the latest applicable release reported by this catalog.";}
                        else explanation="Cannot verify: the installed or available version has an unsupported format.";
                    }
                    var location=Meta(PackageVersionMetadataField.InstalledLocation);
                    string arch="Unknown";
                    var codes=new string[installed.ProductCodes.Count];for(int i=0;i<codes.Length;i++)codes[i]=installed.ProductCodes[i];
                    var software=new Software{Id=Software.Key($"winget|{installed.Id}|{scope}|{location}|{installed.Version}"),Name=p.Name,Publisher=Meta(PackageVersionMetadataField.PublisherDisplayName),InstalledVersion=installed.Version,AvailableVersion=latest,PackageId=recognized?p.Id:"",ProviderInstanceId=installed.Id,ProductCodes=codes,FamilyName=installed.PackageFamilyNames.Count>0?installed.PackageFamilyNames[0]:"",InstallLocation=location,Architecture=arch,Scope=scope,InstallerType=Meta(PackageVersionMetadataField.InstallerType),Provider=recognized?"WinGet":"Registry",State=state,CheckedAt=remote&&recognized?DateTimeOffset.UtcNow:null,Evidence=["WinGet COM installed catalog"],Explanation=explanation};
                    var catalogUpdated=CatalogUpdateTime(available);
                    apps.Add(software with{InstalledChannel=installed.Channel??"",AvailableChannel=available?.Channel??"",CatalogUpdatedAt=catalogUpdated,
                        Explanation=remote&&recognized&&!CatalogFreshness.IsFresh(catalogUpdated,DateTimeOffset.UtcNow)?software.Explanation+" The catalog cache refresh is old or unverified. Reported releases remain visible, but fresh current/automatic status is disabled.":software.Explanation});
                }
                catch(Exception ex) when(ex is not OperationCanceledException){/* Preserve independent catalog records. */}
            }
            // Pin APIs are not exposed by COM. Use an isolated conservative CLI adapter, never package-table parsing.
            if(remote)
            {
                census=apps.ToArray();
                var pins=await ReadPinsAsync(cancellation);
                apps=apps.Select(x=>x with {PinVerified=pins.Verified,Pinned=WinGetPins.IsPinned(x,pins.Ids),Explanation=WinGetPins.IsPinned(x,pins.Ids)?"Pinned in WinGet. UpdateDeck will not override this pin.":!pins.Verified&&x.State==UpdateState.Available?"Update found, but pin status could not be verified. Automatic updates are disabled.":x.Explanation}).ToList();
            }
            DateTimeOffset? updated=apps.Select(x=>x.CatalogUpdatedAt).Max();
            bool fresh=remote&&CatalogFreshness.IsFresh(updated,DateTimeOffset.UtcNow);
            return new(apps,[new(Name,true,ProviderCapability.Inventory|ProviderCapability.Check|ProviderCapability.Install|ProviderCapability.Verify|ProviderCapability.Versions|ProviderCapability.Download|ProviderCapability.AutomaticInstall|ProviderCapability.SilentInstall|ProviderCapability.InteractiveInstall|ProviderCapability.RestartDetection,$"{apps.Count} structured package records. "+(fresh?"Catalog check completed with recent source data.":remote?"Catalog queried; source refresh time is old or unverified. Automatic updates are disabled.":"Local inventory only."),DateTimeOffset.UtcNow){RemoteCheckCompleted=fresh,DataUpdatedAt=updated}]);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new(apps,[new(Name,false,ProviderCapability.None,$"WinGet unavailable: {ex.Message}. Registry and Store discovery remain available.",DateTimeOffset.UtcNow)]);
        }
    }
    private static CreateCompositePackageCatalogOptions Composite(DeploymentManager manager)
    {
        var options=new CreateCompositePackageCatalogOptions {CompositeSearchBehavior=CompositeSearchBehavior.LocalCatalogs};
        // Only the built-in community catalog is used for automatic updates. Store remains its own handoff.
        var source=manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
        source.AcceptSourceAgreements=false;
        options.Catalogs.Add(source);return options;
    }
    private static DateTimeOffset? CatalogUpdateTime(PackageVersionInfo? version)
    {try{var updated=version?.PackageCatalog?.Info?.LastUpdateTime;return updated?.Year>=2000?updated:null;}catch{return null;}}
    public async Task<(bool Verified,HashSet<string> Ids)> ReadPinsAsync(CancellationToken cancellation,Software? target=null)
    {
        using var protectedPins=pinStorage?.Invoke()??WinGetPinStorage.Acquire();
        var path=Cli();var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(path==null||!protectedPins.Safe)return(false,ids);
        try
        {
            var result=await process.RunAsync(path,["pin","list","--disable-interactivity"],TimeSpan.FromSeconds(35),cancellation,ProcessPurpose.ReadOnly);
            var known=census.Concat(target==null?[]:[target]).SelectMany(WinGetPins.Keys);
            return WinGetPins.Parse(result,known);
        }
        catch(Exception ex) when(ex is not OperationCanceledException){return(false,ids);}
    }
    public static (bool Verified,HashSet<string> Ids) ParsePins(ProcessResult result)=>WinGetPins.Parse(result);
    public Task<ExecutionResult> ExecuteAsync(Software app,IProgress<string> progress,CancellationToken cancellation)=>ExecuteCoreAsync(app,progress,false,cancellation);
    public Task<ExecutionResult> ExecuteSupervisedAsync(Software app,IProgress<string> progress,CancellationToken cancellation)=>ExecuteCoreAsync(app,progress,true,cancellation);
    private async Task<ExecutionResult> ExecuteCoreAsync(Software app,IProgress<string> progress,bool supervised,CancellationToken cancellation)
    {
        var risk=UpdatePolicy.Classify(app);
        if(!PackageIdentifiers.IsWinGet(app.PackageId)||!(supervised?app.CanSupervise:app.CanUpdate)||risk is Risk.High or Risk.SystemSensitive||!supervised&&risk!=Risk.Routine||!app.HasFreshCheck||app.Scope is not ("User" or "Machine")||app.Architecture is not ("X64" or "X86" or "Arm64")||app.InstallLocation.Length==0||!Versioning.Known(app.InstalledVersion)||Versioning.Compare(app.AvailableVersion,app.InstalledVersion) is not >0||!Versioning.SameReleaseTrack(app.InstalledVersion,app.AvailableVersion)||!app.InstalledChannel.Equals(app.AvailableChannel,StringComparison.OrdinalIgnoreCase)||app.InstalledChannel.Length>0)return new(OperationState.Deferred,"This package, installation identity, release channel or target version is not eligible for this update method.");
        var path=Cli();if(path==null)return new(OperationState.Failed,"A validated Microsoft WinGet executable is unavailable. Repair App Installer from Microsoft, then recheck.");
        var pins=await ReadPinsAsync(cancellation,app);
        if(!pins.Verified||WinGetPins.IsPinned(app,pins.Ids))return new(OperationState.Deferred,"The package is pinned or its pins could not be checked. No installer was started.");
        var source=await process.RunAsync(path,["source","export","--name","winget","--disable-interactivity"],TimeSpan.FromSeconds(20),cancellation,ProcessPurpose.ReadOnly);
        if(!TrustedSource(source))return new(OperationState.Deferred,"The WinGet source does not match the reviewed Microsoft community catalog, or could not be verified. No installer was started.");
        var installed=MatchingInstallations(app,await ReadInstalledAsync(cancellation));
        if(installed.Length!=1||Versioning.Compare(installed[0].InstalledVersion,app.InstalledVersion)!=0)return new(OperationState.Deferred,"Fresh installed metadata changed or no longer identifies one matching installation. Recheck before updating; no installer was started.");
        string? boundary=executionBoundary!=null?executionBoundary(app):UpdatePolicy.BlockReason(app,new(),DateTimeOffset.UtcNow,MachineServices.InstallationRestartRequired(),MachineServices.Running(app),MachineServices.FreeSpace(app),supervised);
        if(boundary!=null)return new(OperationState.Deferred,boundary);
        progress.Report(supervised?"WinGet is downloading or opening the interactive installer. Complete publisher and Windows prompts. Required CLI agreements are never accepted automatically.":"WinGet is downloading or installing. Progress is indeterminate; Windows may request elevation.");
        var args=new List<string>{"upgrade","--id",app.PackageId,"--exact","--source","winget","--version",app.AvailableVersion,supervised?"--interactive":"--silent","--disable-interactivity"};
        if(app.Scope is "User" or "Machine"){args.Add("--scope");args.Add(app.Scope.ToLowerInvariant());}
        if(app.Architecture is "X64" or "X86" or "Arm64"){args.Add("--architecture");args.Add(app.Architecture.ToLowerInvariant());}
        var result=await process.RunAsync(path,args,TimeSpan.FromHours(6),cancellation,ProcessPurpose.Installer);
        return InterpretExit(result);
    }
    public static bool TrustedSource(ProcessResult result)
    {
        if(result.ExitCode!=0||result.TimedOut||result.OutputIncomplete||result.OutputTruncated)return false;
        try{using var json=JsonDocument.Parse(result.Output);var item=json.RootElement;return item.GetProperty("Name").GetString()=="winget"&&item.GetProperty("Arg").GetString()=="https://cdn.winget.microsoft.com/cache"&&item.GetProperty("Type").GetString()=="Microsoft.PreIndexed.Package";}catch(JsonException){return false;}catch(KeyNotFoundException){return false;}catch(InvalidOperationException){return false;}
    }
    public static ExecutionResult InterpretExit(ProcessResult result)
    {
        var code=unchecked((uint)result.ExitCode);
        if(result.OutputIncomplete&&!result.TimedOut)return new(OperationState.AwaitingVerification,"The command ended but complete process output was not observed. A child installer may still be active. Pending work stops; inspect it before retrying.",result.ExitCode,StopQueue:true);
        if(result.TimedOut)return new(OperationState.AwaitingVerification,"Installer did not finish within the monitoring period. Pending work stops; inspect running installers before retrying.",result.ExitCode,StopQueue:true,ActiveProcessId:result.ProcessId,ActiveProcessStartedUtcTicks:result.StartedUtcTicks);
        if(code==0||code==3010)return new(OperationState.AwaitingVerification,"Installer finished. Checking the installed version separately.",result.ExitCode,code==3010);
        if(code is 0x8A150109 or 0x8A15010A or 0x8A15010B)return new(OperationState.InteractionRequired,"Windows must restart before this installation can finish. Restart manually and recheck.",result.ExitCode,true);
        if(code is 1618 or 0x8A150102)return new(OperationState.InteractionRequired,"Another installation is in progress. Pending work stopped; let that installer finish and inspect before retrying.",result.ExitCode,StopQueue:true);
        if(code is 0x8A150101 or 0x8A150103 or 0x8A150111)return new(OperationState.Deferred,"The package or one of its files is in use. Close the application normally, then retry.",result.ExitCode);
        if(code is 0x8A150041 or 0x8A150046)return new(OperationState.InteractionRequired,"WinGet requires an agreement or user interaction. Review this package in WinGet; no agreement was accepted automatically.",result.ExitCode);
        if(code is 0x800704C7 or 0x8A15010C)return new(OperationState.InteractionRequired,"Windows elevation or installation was cancelled. Retry when ready.",result.ExitCode);
        if(code is 0x8A150008 or 0x8A150107)return new(OperationState.Failed,"The download or network connection failed. Check connectivity and try again.",result.ExitCode,false,true);
        if(code is 0x8A15001A or 0x8A15002D or 0x8A150011)return new(OperationState.Failed,"A source, signature, or integrity check failed. The update was stopped; inspect the source.",result.ExitCode);
        return new(OperationState.Failed,$"WinGet could not finish the update (0x{code:X8}). Review WinGet's diagnostic log for the package; policy, running software, or installer requirements may prevent it.",result.ExitCode);
    }
    public async Task<ExecutionResult> VerifyAsync(Software app,CancellationToken cancellation)
    {
        // New catalog every time: a cached COM package object is never evidence of installation success.
        for(int attempt=0;attempt<3;attempt++)
        {
            if(attempt>0)await Task.Delay(1500,cancellation);
            var matches=MatchingInstallations(app,await ReadInstalledAsync(cancellation));
            if(matches.Length==1&&Versioning.Compare(matches[0].InstalledVersion,app.AvailableVersion) is >=0)return new(OperationState.Verified,$"Installed version {matches[0].InstalledVersion} verified against target {app.AvailableVersion}.");
        }
        return new(OperationState.AwaitingVerification,"Installer execution ended, but a fresh inventory could not verify the target version. Recheck after the publisher finishes or Windows restarts.");
    }
    private async Task<IReadOnlyList<Software>> ReadInstalledAsync(CancellationToken cancellation)
    {
        if(installedReader!=null)return await installedReader(cancellation);
        var catalog=await DiscoverAsync(false,cancellation);
        if(!catalog.Providers.Any(x=>x.Name==Name&&x.Available))return [];
        // The local COM catalog can omit scope. A fresh registry correlation supplies it from the hive,
        // rather than guessing from the display name or parsing an opaque provider instance ID.
        var registry=await new RegistryInventory().DiscoverAsync(false,cancellation);
        return IdentityResolver.Resolve(registry.Apps.Concat(catalog.Apps));
    }
    public static Software[] MatchingInstallations(Software app,IReadOnlyList<Software> installed)=>installed.Where(x=>
                (x.ProductCodes.Intersect(app.ProductCodes,StringComparer.OrdinalIgnoreCase).Any() || x.FamilyName.Length>0&&x.FamilyName==app.FamilyName || x.PackageId.Length>0&&x.PackageId==app.PackageId || x.PrimaryExecutable.Length>0&&app.PrimaryExecutable.Length>0&&x.PrimaryExecutable.Equals(app.PrimaryExecutable,StringComparison.OrdinalIgnoreCase) || x.InstallLocation.Length>0&&app.InstallLocation.Length>0&&IdentityResolver.Normalize(x.InstallLocation).Equals(IdentityResolver.Normalize(app.InstallLocation),StringComparison.OrdinalIgnoreCase)&&x.Publisher.Length>0&&x.Publisher.Equals(app.Publisher,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(app.Name,StringComparison.OrdinalIgnoreCase))
                && (app.Scope=="Unknown"||x.Scope==app.Scope)
                && (app.Architecture=="Unknown"||x.Architecture.Equals(app.Architecture,StringComparison.OrdinalIgnoreCase))
                && !x.Ambiguous&&!x.VersionEvidenceConflict&&x.State!=UpdateState.Attention&&x.Kind==app.Kind
                && (app.Publisher.Length==0||x.Publisher.Length==0||app.Publisher.Equals(x.Publisher,StringComparison.OrdinalIgnoreCase))
                && app.InstalledChannel.Equals(x.InstalledChannel,StringComparison.OrdinalIgnoreCase)
                && (app.InstallLocation.Length==0||x.InstallLocation.Length>0&&IdentityResolver.Normalize(app.InstallLocation).Equals(IdentityResolver.Normalize(x.InstallLocation),StringComparison.OrdinalIgnoreCase))).ToArray();
}

