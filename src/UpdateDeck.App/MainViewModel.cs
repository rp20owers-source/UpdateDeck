using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace UpdateDeck.App;

public sealed class MainViewModel : Observable
{
    private readonly StateStore store;
    private readonly WinGetProvider winget;
    private readonly OfficialInstallerProvider official;
    private readonly VendorLaunchers vendors;
    private readonly InventoryCoordinator inventory;
    private readonly QueueEngine queue;
    private readonly MachineServices machine;
    private readonly DispatcherTimer scheduler;
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim scanGate=new(1,1);
    private readonly MaintenanceGate maintenance=new();
    private bool stopped;
    private bool CanStartMaintenance=>!stopped&&!Busy&&!QueueRunning&&!maintenance.IsActive;
    public Preferences Preferences {get;}
    public Preferences Settings {get;}
    public BatchCollection<AppRow> Rows {get;}=[];
    public ObservableCollection<ProviderReport> Providers {get;}=[];
    public ObservableCollection<VendorTarget> Vendors {get;}=[];
    public ObservableCollection<VendorTarget> GameLaunchers {get;}=[];
    public ICollectionView GamesView {get;}
    private string gameSearch="";
    public string GameSearch{get=>gameSearch;set{if(Set(ref gameSearch,value))GamesView.Refresh();}}
    public string GamesSummary=>$"{Rows.Count(x=>x.App.Kind==SoftwareKind.Game):N0} installed games discovered · {GameLaunchers.Count(x=>x.Executable!=null)} validated launchers";
    public ObservableCollection<UpdateOperation> History {get;}=[];
    public ObservableCollection<UpdateOperation> Operations {get;}=[];
    public ObservableCollection<PlanItem> Deferred {get;}=[];
    public ObservableCollection<string> PreviewActions {get;}=[];
    private bool isPlanPreview;
    public bool IsPlanPreview{get=>isPlanPreview;private set{if(Set(ref isPlanPreview,value))Changed(nameof(QueueHeading));}}
    public string QueueHeading=>IsPlanPreview?"UPDATE PLAN · PREVIEW ONLY":"UPDATE QUEUE";
    public ObservableCollection<string> PortableFolders {get;}=[];
    public ObservableCollection<string> AudioPluginFolders {get;}=[];
    public ObservableCollection<string> ExcludedFolders {get;}=[];
    private string quietStartText="",quietEndText="";
    public string QuietStartText{get=>quietStartText;set{if(Set(ref quietStartText,value))ValidateSettingsInput();}}
    public string QuietEndText{get=>quietEndText;set{if(Set(ref quietEndText,value))ValidateSettingsInput();}}
    private bool ValidQuietHours=>int.TryParse(QuietStartText,out int start)&&start is >=0 and <=23&&int.TryParse(QuietEndText,out int end)&&end is >=0 and <=23;
    public string SettingsValidationMessage=>ValidQuietHours?"":"Quiet hours must be whole hours from 0 to 23.";
    public bool HasSettingsValidationError=>!ValidQuietHours;
    private void ValidateSettingsInput(){Changed(nameof(SettingsValidationMessage));Changed(nameof(HasSettingsValidationError));CommandManager.InvalidateRequerySuggested();}
    public ICollectionView DeferredView {get;}
    public ICollectionView AppsView {get;}
    public ICollectionView HistoryView {get;}
    public string[] Navigation {get;}=["Dashboard","Updates","Installed Apps","Games","Drivers","History","This PC","Settings"];
    public string[] Filters=>Preferences.SupportedFilters;
    public string[] Sorts=>Preferences.SupportedSorts;
    public string[] Themes {get;}=["Dark","Light","System"];
    public string[] Schedules {get;}=["Manual","Daily","Weekly"];
    private string page="Dashboard",search="",banner="",activity="Ready",queueText="No active update queue",lastScan="No scan yet",historySearch="";
    private bool busy,queueRunning,hasInventory,queueDetailsExpanded;
    public bool QueueDetailsExpanded{get=>queueDetailsExpanded;set{if(Set(ref queueDetailsExpanded,value)&&value)SelectedApp=null;}}
    private AppRow? selectedApp;
    private UpdateOperation? selectedOperation;
    private MachineInfo? system;
    private string driverObservation="Installed graphics versions are read from Windows. A known newer vendor release has not been independently verified.";
    public string DriverObservation{get=>driverObservation;set=>Set(ref driverObservation,value);}
    public event Action<string>? Notification;
    public event Action? SettingsSaved;
    public string Page{get=>page;set{if(Set(ref page,value)){ShowVendors=false;Changed(nameof(ShowUpdateQueue));SelectedApp=null;AppsView.Refresh();Changed(nameof(PageSubtitle));Changed(nameof(IsEmpty));Changed(nameof(ResultCount));}}}
    private bool showVendors;
    public bool ShowVendors{get=>showVendors;set{if(Set(ref showVendors,value)){SelectedApp=null;Changed(nameof(VendorButtonLabel));Changed(nameof(PageSubtitle));Changed(nameof(ShowUpdateQueue));}}}
    public bool ShowUpdateQueue=>Page=="Updates"&&!ShowVendors;
    public string VendorButtonLabel=>ShowVendors?"Application updates":"Vendor updaters";
    public string PageSubtitle=>ShowVendors?"Official managers for publisher-maintained applications.":Page switch{"Dashboard"=>"Your applications. Your drivers. Your updates. One place.","Updates"=>"Verified releases, clear sources, and a queue you control.","Installed Apps"=>"A complete view of discovered software and how it updates.","Games"=>"Your games and official launchers, in one place.","Vendor Updaters"=>"Continue in official managers, then return to verify.","Drivers"=>"Installed graphics information. Driver changes stay supervised.","History"=>"Execution and verification, recorded separately.","This PC"=>"Your hardware, firmware and Windows specifications.",_=>"Make UpdateDeck work around your routine."};
    public string Search{get=>search;set{if(Set(ref search,value)){AppsView.Refresh();Changed(nameof(IsEmpty));Changed(nameof(ResultCount));}}}
    public string HistorySearch{get=>historySearch;set{if(Set(ref historySearch,value))HistoryView.Refresh();}}
    public string Filter{get=>Preferences.Filter;set{Preferences.Filter=value;AppsView.Refresh();Changed();Changed(nameof(IsEmpty));Changed(nameof(ResultCount));}}
    public string Sort{get=>Preferences.Sort;set{if(Preferences.Sort!=value)Preferences.SortDescending=value=="Last checked";Preferences.Sort=value;ApplySort();Changed();}}
    public string Banner{get=>banner;set{Set(ref banner,value);Changed(nameof(HasBanner));}}
    public bool HasBanner=>Banner.Length>0;
    public string Activity{get=>activity;set{if(Set(ref activity,value))Changed(nameof(FooterActivity));}}
    public string QueueText{get=>queueText;set{if(Set(ref queueText,value))Changed(nameof(FooterActivity));}}
    public string FooterActivity=>QueueRunning?QueueText:Activity;
    public string LastScan{get=>lastScan;set=>Set(ref lastScan,value);}
    public bool Busy{get=>busy;set{Set(ref busy,value);Changed(nameof(ScanAnimation));CommandManager.InvalidateRequerySuggested();}}
    public bool QueueRunning{get=>queueRunning;set{Set(ref queueRunning,value);Changed(nameof(PauseLabel));Changed(nameof(QueueAnimation));Changed(nameof(FooterActivity));CommandManager.InvalidateRequerySuggested();}}
    public bool ScanAnimation=>Busy&&SystemParameters.ClientAreaAnimation;
    public bool QueueAnimation=>QueueRunning&&SystemParameters.ClientAreaAnimation;
    public string PauseLabel=>queue.IsPaused?"Resume":"Pause";
    public bool HasInventory{get=>hasInventory;set=>Set(ref hasInventory,value);}
    public bool IsEmpty=>!AppsView.Cast<object>().Any();
    public string ResultCount=>$"{AppsView.Cast<object>().Count():N0} {(Filter=="Audio plugins"?"plugin modules":Filter=="All discovered software"?"items":"applications")}";
    public AppRow? SelectedApp{get=>selectedApp;set{Set(ref selectedApp,value);Changed(nameof(HasSelection));Changed(nameof(Details));Changed(nameof(IgnoreLabel));Changed(nameof(DetailActionLabel));}}
    public bool HasSelection=>SelectedApp!=null;
    public string IgnoreLabel=>SelectedApp!=null&&Preferences.Ignored.Contains(SelectedApp.App.Id)?"Include in updates":"Ignore application";
    public string DetailActionLabel=>SelectedApp?.Action=="Inspect"?"Recheck inventory":SelectedApp?.Action??"Inspect";
    public string Details=>SelectedApp==null?"":$"{SelectedApp.App.Explanation}\n\nInstalled version\n{SelectedApp.App.InstalledVersion}\n\nRaw installed-version evidence\n{string.Join("\n",SelectedApp.App.InstalledVersionEvidence)}\n\nRelease channel\n{(SelectedApp.App.InstalledChannel.Length>0?SelectedApp.App.InstalledChannel:"Not specified by provider")} → {(SelectedApp.App.AvailableChannel.Length>0?SelectedApp.App.AvailableChannel:"Not specified by provider")}\n\nAvailable version\n{SelectedApp.Available}\n\nPackage / family\n{SelectedApp.App.PackageId}\n{SelectedApp.App.FamilyName}\n\nPublisher\n{SelectedApp.Publisher}\n\nInstallation\n{SelectedApp.App.InstallLocation}\n\nScope · architecture · type\n{SelectedApp.App.Scope} · {SelectedApp.App.Architecture} · {SelectedApp.App.InstallerType}\n\nUpdate method\n{SelectedApp.App.Provider} · {SelectedApp.App.Risk}\n\nLast catalog query\n{SelectedApp.App.CheckedAt?.ToLocalTime().ToString("g")??"Not verified"}\n\nCatalog source refresh\n{SelectedApp.App.CatalogUpdatedAt?.ToLocalTime().ToString("g")??"Not verified"}\n\nIdentity evidence\n{string.Join("\n",SelectedApp.App.Evidence)}\n\nRecent history\n{string.Join("\n",History.Where(x=>x.AppId==SelectedApp.App.Id).Take(5).Select(x=>$"{x.StartedAt.ToLocalTime():g} · {x.State}\n{x.Message}"))}";
    public UpdateOperation? SelectedOperation{get=>selectedOperation;set{Set(ref selectedOperation,value);Changed(nameof(OperationDetails));}}
    public string OperationDetails=>SelectedOperation==null?"Select a history entry to inspect execution, verification and restart information.":$"{SelectedOperation.Name}\n{SelectedOperation.PreviousVersion} → {SelectedOperation.TargetVersion}\n{SelectedOperation.Provider} · {SelectedOperation.ResultLabel}\n{SelectedOperation.StartedAt.ToLocalTime():g} → {SelectedOperation.FinishedAt?.ToLocalTime():g}\n{SelectedOperation.Message}\nExit code: {(SelectedOperation.ExitCode.HasValue?$"0x{unchecked((uint)SelectedOperation.ExitCode.Value):X8}":"not applicable")}\nRestart required: {SelectedOperation.RebootRequired}\nMonitoring hold: {(SelectedOperation.RequiresInspection?"Inspect child installers and publisher dialogs before confirming completion.":"No monitoring hold")}\nRecorded command ended: {SelectedOperation.RecordedProcessEnded}";
    public int InstalledCount=>Rows.Count(x=>x.App.Kind==SoftwareKind.Application);
    public int PluginCount=>Rows.Count(x=>x.App.Kind==SoftwareKind.AudioPlugin);
    public int UpdateCount=>Rows.Count(x=>x.App.Kind==SoftwareKind.Application&&x.App.State==UpdateState.Available);
    public int CurrentCount=>Rows.Count(x=>x.App.State==UpdateState.Current&&x.App.HasFreshCheck);
    public int UnknownCount=>Rows.Count(x=>x.App.Kind==SoftwareKind.Application&&(x.App.State is UpdateState.Unknown or UpdateState.Attention||x.App.State==UpdateState.Current&&!x.App.HasFreshCheck));
    public int EligibleCount=>Rows.Count(x=>x.App.CanUpdate&&!Preferences.Ignored.Contains(x.App.Id));
    public string HeroTitle=>UpdateCount>0?$"{UpdateCount} updates worth a look.":HasInventory?"Your software, in focus.":"Let's get your software in view.";
    public string HeroDescription=>Preferences.PauseInstallations?"Installations are paused. Review your saved update policy in Settings before resuming. Inventory checks remain available.":system?.InstallationRestartRequired==true?"Windows reports a pending restart. Restart manually before automatic installation. Available releases and supervised vendor workflows remain available for review.":HasInventory?$"{EligibleCount} routine updates can be considered for the automatic queue. Vendor-managed and sensitive software use supervised workflows.":"Scan this PC to discover installed applications and check trusted update sources.";
    public string SourceSummary=>$"{Providers.Count(x=>x.Available)} of {Providers.Count} sources available · {System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() switch{true=>"Network connected",false=>"Offline — cached results"}}";
    public string Storage=>$"{(store.Portable?"Portable":"Installed")} mode · {store.DirectoryPath}\n{store.StorageNotice}";
    public string ScheduleStatus
    {
        get
        {
            if(Preferences.Schedule=="Manual")return "Saved schedule: Manual. Checks run when you request them or at startup if enabled.";
            if(Preferences.LastScheduledAttempt is not DateTimeOffset attempt)return $"Saved schedule: {Preferences.Schedule}. No scheduled attempt has been recorded.";
            if(attempt>DateTimeOffset.UtcNow.AddMinutes(2))return $"Saved schedule: {Preferences.Schedule}. The stored attempt time is ahead of the current clock; the next eligible check will re-evaluate it.";
            return Preferences.ScheduledFailureCount>0?$"Saved schedule: {Preferences.Schedule}. Last attempt {attempt.ToLocalTime():g}; {Preferences.ScheduledFailureCount} incomplete check(s). Automatic retry after {(attempt+UpdatePolicy.ScheduledRetryDelay(Preferences)).ToLocalTime():g}, subject to quiet hours and connection policy. Check now can retry immediately.":$"Saved schedule: {Preferences.Schedule}. Last scheduled attempt {attempt.ToLocalTime():g}.";
        }
    }
    public IReadOnlyList<HardwareSection> HardwareSections=>system?.Hardware??[];
    public string SystemSummary=>system==null?"Reading system capabilities…":$"{system.OS} · {system.Architecture}\n{system.Processor}\n{(system.RestartNotice.Length>0?system.RestartNotice:system.PendingRestart?"Windows restart is recommended.":"No restart flag detected.")}";
    public string GraphicsSummary=>system==null?"Reading graphics devices…":system.Graphics.Length==0?"No graphics information could be read. Inventory may be restricted by policy.":string.Join("\n\n",system.Graphics);
    public string DriverGuidance=>system?.Graphics.Any(x=>x.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase))==true?"NVIDIA hardware detected. Use NVIDIA App for Game Ready / Studio branch selection and OEM compatibility. UpdateDeck cannot independently verify a newer driver through a supported public feed.":"NVIDIA hardware was not reported. Use Windows Update or your device manufacturer's official tool. No driver is installed automatically by Update All.";
    public bool HasVendors=>Vendors.Count>0;
    public bool HasHistory=>History.Count>0;
    public ICommand ScanCommand {get;}
    public ICommand UpdateAllCommand {get;}
    public ICommand PreviewPlanCommand {get;}
    public ICommand LeavePlanCommand {get;}
    public ICommand UpdateSelectedCommand {get;}
    public ICommand RowActionCommand {get;}
    public ICommand DetailsActionCommand {get;}
    public ICommand ReviewInteractiveCommand {get;}
    public ICommand OpenVendorCommand {get;}
    public ICommand OpenVendorsCommand {get;}
    public ICommand ToggleVendorsCommand {get;}
    public ICommand PauseCommand {get;}
    public ICommand CancelCommand {get;}
    public ICommand SkipCommand {get;}
    public ICommand RetryCommand {get;}
    public ICommand ResolveInstallerCommand {get;}
    public ICommand SaveCommand {get;}
    public ICommand IgnoreCommand {get;}
    public ICommand CloseDetailsCommand {get;}
    public ICommand ExportCommand {get;}
    public ICommand ImportCommand {get;}
    public ICommand DiagnosticsCommand {get;}
    public ICommand AddFolderCommand {get;}
    public ICommand RemoveFolderCommand {get;}
    public ICommand ExcludeFolderCommand {get;}
    public ICommand AddAudioPluginFolderCommand {get;}
    public ICommand RemoveAudioPluginFolderCommand {get;}
    public ICommand ViewAudioPluginsCommand {get;}
    public ICommand AllowFolderCommand {get;}
    public ICommand DismissCommand {get;}
    private string? activeId;
    public MainViewModel(StateStore state)
    {
        store=state;Preferences=store.LoadPreferences();
        Settings=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(Preferences,StateStore.Json),StateStore.Json)!;
        quietStartText=Settings.QuietStart.ToString(System.Globalization.CultureInfo.InvariantCulture);quietEndText=Settings.QuietEnd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var process=new ProcessRunner();winget=new(process);vendors=new();machine=new(process);
        official=new(store.DirectoryPath);
        inventory=new([new RegistryInventory(),new GameLibraryInventory(),new StoreInventory(),new AdditionalInventory(Preferences),new AudioPluginInventory(Preferences),winget],vendors,store,official:official);
        queue=new(new UpdateExecutor(winget,official),store,vendors);
        DeferredView=CollectionViewSource.GetDefaultView(Deferred);DeferredView.GroupDescriptions.Add(new PropertyGroupDescription("App",new SoftwareFamilyConverter()));
        AppsView=CollectionViewSource.GetDefaultView(Rows);AppsView.Filter=FilterRow;
        AppsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppRow.Family)){StringComparison=StringComparison.OrdinalIgnoreCase});
        GamesView=new ListCollectionView(Rows);GamesView.Filter=x=>x is AppRow r&&r.App.Kind==SoftwareKind.Game&&$"{r.Name} {r.Provider}".Contains(GameSearch,StringComparison.CurrentCultureIgnoreCase);
        GamesView.SortDescriptions.Add(new("Name",ListSortDirection.Ascending));
        HistoryView=CollectionViewSource.GetDefaultView(History);HistoryView.Filter=x=>x is UpdateOperation op&&$"{op.Name} {op.Provider} {op.State} {op.ResultLabel} {op.Message}".Contains(HistorySearch,StringComparison.CurrentCultureIgnoreCase);
        ActionCommand Command(Func<object?,Task> a,Func<bool>? enabled=null)=>new(a,ReportError,enabled);
        ScanCommand=Command(_=>ScanAsync(true),()=>CanStartMaintenance);
        UpdateAllCommand=Command(_=>UpdateAsync(null),()=>CanStartMaintenance&&HasInventory);
        PreviewPlanCommand=Command(_=>PreviewPlanAsync(),()=>CanStartMaintenance&&HasInventory);
        LeavePlanCommand=Command(_=>{IsPlanPreview=false;PreviewActions.Clear();QueueDetailsExpanded=false;Page="Installed Apps";return Task.CompletedTask;},()=>CanStartMaintenance);
        UpdateSelectedCommand=Command(_=>UpdateAsync(Rows.Where(x=>x.Selected).Select(x=>x.App.Id).ToHashSet()),()=>CanStartMaintenance);
        RowActionCommand=Command(async x=>{if(x is AppRow row){SelectedApp=row;if(row.App.VendorId.Length>0)await Handoff(row.App.VendorId);else if(row.App.CanUpdate)await UpdateAsync([row.App.Id]);else if(row.App.CanSupervise)await ReviewSupervisedAsync(row.App.Id);}},()=>CanStartMaintenance);
        DetailsActionCommand=Command(async _=>{if(SelectedApp is AppRow row){if(row.App.VendorId.Length>0)await Handoff(row.App.VendorId);else if(row.App.CanUpdate)await UpdateAsync([row.App.Id]);else if(row.App.CanSupervise)await ReviewSupervisedAsync(row.App.Id);else await ScanAsync(true);}},()=>CanStartMaintenance);
        ReviewInteractiveCommand=Command(async _=>{if(SelectedApp is AppRow row)await ReviewSupervisedAsync(row.App.Id);},()=>CanStartMaintenance&&SelectedApp?.App.CanSupervise==true);
        OpenVendorCommand=Command(x=>Handoff(x is VendorTarget t?t.Id:x?.ToString()??""),()=>CanStartMaintenance);
        ToggleVendorsCommand=Command(_=>{Page="Updates";ShowVendors=!ShowVendors;return Task.CompletedTask;});
        OpenVendorsCommand=Command(_=>{Page="Updates";ShowVendors=true;Banner="Open each manager when you're ready. Each opens once per queue; driver managers remain supervised.";return Task.CompletedTask;});
        PauseCommand=Command(_=>{if(queue.IsPaused)queue.Resume();else queue.Pause();Changed(nameof(PauseLabel));QueueText=queue.IsPaused?"Pause requested — the active installer will finish safely.":"Queue resumed";return Task.CompletedTask;},()=>QueueRunning);
        CancelCommand=Command(_=>{queue.CancelPending();QueueText="Pending work cancelled. The active installer will finish safely.";return Task.CompletedTask;},()=>QueueRunning);
        SkipCommand=Command(_=>{var pending=Operations.FirstOrDefault(x=>x.State==OperationState.Pending&&x.AppId!=activeId);if(pending!=null){queue.Skip(pending.AppId);QueueText=$"Will skip {pending.Name} at its safe boundary.";}return Task.CompletedTask;},()=>QueueRunning);
        RetryCommand=Command(_=>UpdateAsync(History.Where(x=>x.State is OperationState.Failed or OperationState.Deferred or OperationState.Interrupted or OperationState.InteractionRequired).Select(x=>x.AppId).ToHashSet()),()=>CanStartMaintenance);
        ResolveInstallerCommand=Command(_=>
        {
            if(SelectedOperation is not {RequiresInspection:true} operation)return Task.CompletedTask;
            var running=MachineServices.InstallerStillRunning(operation);
            if(running==true){Banner="The recorded installer is still running. Let it finish normally; it will not be terminated.";return Task.CompletedTask;}
            var answer=System.Windows.MessageBox.Show("Check that this installer and any Windows elevation or vendor dialogs have finished. Confirm only after you have inspected them.\n\nThis clears the queue's monitoring hold. It does not mark the update successful. A fresh inventory check is still required.","Confirm installer has ended",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
            if(answer==MessageBoxResult.Yes){store.SaveOperation(operation with{RequiresInspection=false,ActiveProcessId=null,ActiveProcessStartedUtcTicks=null,Message=operation.Message+" User confirmed that the installer has ended; update success remains subject to independent verification."});LoadHistory();Banner="Monitoring hold cleared. Recheck the installed version before retrying.";}
            return Task.CompletedTask;
        },()=>CanStartMaintenance&&SelectedOperation?.RequiresInspection==true);
        SaveCommand=Command(_=>
        {
            Settings.QuietStart=int.Parse(QuietStartText);Settings.QuietEnd=int.Parse(QuietEndText);Settings.PortableDirectories=PortableFolders.ToList();Settings.AudioPluginDirectories=AudioPluginFolders.ToList();Settings.ExcludedDirectories=ExcludedFolders.ToList();
            Preferences.ApplyUserSettings(Settings);store.SavePreferences(Preferences);ThemeManager.Apply(Preferences.Theme);SettingsSaved?.Invoke();Changed(nameof(HeroDescription));Changed(nameof(EligibleCount));Changed(nameof(ScheduleStatus));
            Banner="Settings saved. Scheduling runs while UpdateDeck is open, including in the tray; scheduled checks never install updates. Scan again to apply discovery-folder changes.";return Task.CompletedTask;
        },()=>CanStartMaintenance&&ValidQuietHours);
        IgnoreCommand=Command(_=>{if(SelectedApp!=null){if(!Preferences.Ignored.Remove(SelectedApp.App.Id))Preferences.Ignored.Add(SelectedApp.App.Id);store.SavePreferences(Preferences);SetApps(currentApps);Changed(nameof(IgnoreLabel));}return Task.CompletedTask;},()=>CanStartMaintenance);
        CloseDetailsCommand=Command(_=>{SelectedApp=null;return Task.CompletedTask;});
        DismissCommand=Command(_=>{Banner="";return Task.CompletedTask;});
        ExportCommand=Command(_=>ExportInventory());ImportCommand=Command(_=>RunMaintenanceAsync(ImportPreview),()=>CanStartMaintenance);DiagnosticsCommand=Command(_=>ExportDiagnostics());
        AddFolderCommand=Command(_=>{if(PortableFolders.Count>=32)throw new InvalidOperationException("Up to 32 portable folders can be selected. Remove one before adding another.");var dialog=new OpenFolderDialog{Title="Choose a portable application folder (two levels maximum)"};if(dialog.ShowDialog()==true){if(Path.GetPathRoot(dialog.FolderName)!.TrimEnd('\\')==dialog.FolderName.TrimEnd('\\'))throw new InvalidOperationException("Select an application folder, not an entire drive.");if(!PortableFolders.Contains(dialog.FolderName,StringComparer.OrdinalIgnoreCase))PortableFolders.Add(dialog.FolderName);}return Task.CompletedTask;});
        RemoveFolderCommand=Command(x=>{if(x is string folder)PortableFolders.Remove(folder);return Task.CompletedTask;});
        ExcludeFolderCommand=Command(x=>{if(x is string folder){if(ExcludedFolders.Count>=128)throw new InvalidOperationException("Up to 128 folders can be excluded. Remove an exclusion before adding another.");if(!ExcludedFolders.Contains(folder,StringComparer.OrdinalIgnoreCase))ExcludedFolders.Add(folder);PortableFolders.Remove(folder);Banner="Folder excluded in your settings draft. Save preferences to apply this exclusion.";}return Task.CompletedTask;});
        AllowFolderCommand=Command(x=>{if(x is string folder)ExcludedFolders.Remove(folder);return Task.CompletedTask;});
        AddAudioPluginFolderCommand=Command(_=>{if(AudioPluginFolders.Count>=32)throw new InvalidOperationException("Up to 32 custom plugin folders can be selected. Remove one before adding another.");var dialog=new OpenFolderDialog{Title="Choose an audio-plugin folder (metadata only)"};if(dialog.ShowDialog()==true){if(!AudioPluginInventory.SafeRoot(dialog.FolderName))throw new InvalidOperationException("Choose a plugin folder, not an entire drive.");if(!AudioPluginFolders.Contains(dialog.FolderName,StringComparer.OrdinalIgnoreCase))AudioPluginFolders.Add(dialog.FolderName);}return Task.CompletedTask;},()=>CanStartMaintenance);
        RemoveAudioPluginFolderCommand=Command(x=>{if(x is string folder)AudioPluginFolders.Remove(folder);return Task.CompletedTask;});
        ViewAudioPluginsCommand=Command(_=>{Page="Installed Apps";Filter="Audio plugins";Search="";Banner=$"{PluginCount} plugin modules discovered. Enable optional audio-plugin discovery in Settings and scan to populate this category. DLLs are never loaded for discovery.";return Task.CompletedTask;});
        foreach(var dir in Preferences.PortableDirectories)PortableFolders.Add(dir);
        foreach(var dir in Preferences.AudioPluginDirectories)AudioPluginFolders.Add(dir);
        foreach(var dir in Preferences.ExcludedDirectories)ExcludedFolders.Add(dir);
        ApplySort();LoadHistory();
        scheduler=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
        scheduler.Tick+=async(_,_)=>
        {
            bool changed=false;foreach(var row in Rows)changed=row.RefreshAge()||changed;
            if(changed){AppsView.Refresh();Changed(nameof(CurrentCount));Changed(nameof(UnknownCount));Changed(nameof(EligibleCount));Changed(nameof(HeroDescription));Changed(nameof(ResultCount));Changed(nameof(IsEmpty));}
            if(CanStartMaintenance&&UpdatePolicy.ScheduleDue(Preferences,DateTimeOffset.Now)&&MachineServices.ScheduledCheckAllowed(Preferences))await RunMaintenanceAsync(RunScheduledCheckAsync);
        };
        scheduler.Start();
    }
    private IReadOnlyList<Software> currentApps=[];
    private bool FilterRow(object value)
    {
        if(value is not AppRow row)return false;var a=row.App;
        if(GamePlatforms.IsGaming(a))return false;
        if(Page=="Updates"&&a.State is not (UpdateState.Available or UpdateState.Attention))return false;
        if(Filter=="Audio plugins"&&a.Kind!=SoftwareKind.AudioPlugin)return false;
        if(Filter is not ("Audio plugins" or "All discovered software")&&a.Kind==SoftwareKind.AudioPlugin)return false;
        if(Search.Length>0&&!$"{a.Name} {a.Publisher} {a.PackageId} {a.Provider}".Contains(Search,StringComparison.CurrentCultureIgnoreCase))return false;
        return Filter switch{"Updates available"=>a.State==UpdateState.Available,"Verified current"=>a.State==UpdateState.Current&&a.HasFreshCheck,"Vendor managed"=>a.State==UpdateState.VendorManaged,"Needs attention"=>a.State==UpdateState.Attention||a.Pinned,"Ignored"=>Preferences.Ignored.Contains(a.Id),"Unknown status"=>a.State==UpdateState.Unknown||a.State==UpdateState.Current&&!a.HasFreshCheck,_=>true};
    }
    private void ApplySort()
    {
        if(AppsView is ListCollectionView list)list.CustomSort=new AppRowComparer(Sort,Preferences.SortDescending);
        else{AppsView.SortDescriptions.Clear();AppsView.SortDescriptions.Add(new("Name",ListSortDirection.Ascending));}
    }
    public bool SortByColumn(string path)
    {
        string? target=path switch{"Name"=>"Name","Publisher"=>"Publisher","Version"=>"Installed version","Available"=>"Available version","Provider"=>"Provider","Status"=>"Status",_=>null};
        if(target==null)return false;
        if(Sort==target){Preferences.SortDescending=!Preferences.SortDescending;ApplySort();Changed(nameof(Sort));}else Sort=target;
        return true;
    }
    private void SetApps(IReadOnlyList<Software> apps)
    {
        apps=apps.Select(GamePlatforms.Categorize).ToArray();currentApps=apps;var selectedIds=Rows.Where(x=>x.Selected).Select(x=>x.App.Id).ToHashSet();var detailId=SelectedApp?.App.Id;
        Rows.ReplaceAll(apps.Select(app=>{var visible=Preferences.Ignored.Contains(app.Id)?app with{State=UpdateState.Ignored,Explanation="Ignored by your saved policy. Include it again to permit updates."}:app;return new AppRow(visible){Selected=selectedIds.Contains(app.Id)};}));
        SelectedApp=Rows.FirstOrDefault(x=>x.App.Id==detailId);
        HasInventory=Rows.Count>0;Vendors.Clear();GameLaunchers.Clear();foreach(var target in vendors.Targets){if(GamePlatforms.Contains(target.Id))GameLaunchers.Add(target);else Vendors.Add(target);}
        GamesView.Refresh();Changed(nameof(GamesSummary));
        foreach(var property in new[]{nameof(InstalledCount),nameof(PluginCount),nameof(UpdateCount),nameof(CurrentCount),nameof(UnknownCount),nameof(EligibleCount),nameof(HeroTitle),nameof(HeroDescription),nameof(ResultCount),nameof(IsEmpty),nameof(HasVendors)})Changed(property);
    }
    public async Task InitializeAsync()
    {
        try
        {
            var cached=store.LoadInventory();SetApps(cached);foreach(var provider in store.LoadReports())Providers.Add(provider);
            LastScan=Preferences.LastCheck.HasValue?$"Last catalog check {Preferences.LastCheck.Value.ToLocalTime():g} · cached until rechecked":"No catalog check yet";
            if(store.StorageNotice.Length>0)Banner=store.StorageNotice;
            await ScanAsync(false);
            if(stopped)return;
            QueueEngine.Recover(store,currentApps,system?.PendingRestart??true);LoadHistory();
            if(Preferences.ScanOnStartup&&System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())await ScanAsync(true,false);
        }
        catch(Exception ex){ReportError(ex);}
    }
    public async Task<bool> ScanAsync(bool remote,bool refreshMachine=true)
    {
        if(stopped)return false;
        if(!await scanGate.WaitAsync(0))return false;
        Busy=true;var timer=System.Diagnostics.Stopwatch.StartNew();var scanStarted=DateTimeOffset.UtcNow;
        try
        {
            bool online=remote&&System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            var machineTask=refreshMachine?machine.InspectAsync(lifetime.Token):null;
            var result=await inventory.ScanAsync(online,new Progress<string>(s=>Activity=s),lifetime.Token);
            if(machineTask!=null)
            {
                var observed=await machineTask;var previous=system??store.LoadMachine();system=observed;
                if(previous?.Graphics is {Length:>0}&&observed.Graphics.Length>0&&!previous.Graphics.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(observed.Graphics.Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))
                {
                    DriverObservation=$"Windows reports changed graphics metadata at {DateTime.Now:g}. Compare the versions below; driver branch and a newer applicable vendor release are not independently verified.";
                    store.SaveOperation(new(){AppId="graphics-observation",Name="Graphics device versions changed",Provider="Windows device inventory",PreviousVersion=string.Join("; ",previous.Graphics),TargetVersion=string.Join("; ",observed.Graphics),State=OperationState.Observed,FinishedAt=DateTimeOffset.UtcNow,Message="A fresh read of Windows device metadata changed. This is an observation, not a claim that an UpdateDeck driver installation succeeded."});
                }
                else DriverObservation=$"Graphics metadata read from Windows at {DateTime.Now:g}. "+(previous?.Graphics is {Length:>0}?"No change observed since the previous successful read.":"No earlier graphics baseline is available.");
                if(observed.Graphics.Length>0)store.SaveMachine(observed);
                Changed(nameof(HardwareSections));Changed(nameof(SystemSummary));Changed(nameof(GraphicsSummary));Changed(nameof(DriverGuidance));Changed(nameof(HeroDescription));
            }
            QueueEngine.ReconcileMonitors(store,MachineServices.InstallerStillRunning);
            int reconciled=QueueEngine.ReconcileInstalledVersions(store,result.Apps,DateTimeOffset.UtcNow,system?.PendingRestart??true);
            LoadHistory();if(reconciled>0)Banner=$"{reconciled} earlier operation(s) now have an independently verified installed version. History has been updated; no installer was rerun.";
            SetApps(result.Apps);Providers.Clear();foreach(var provider in result.Providers)Providers.Add(provider);
            if(online&&ScanEvidence.CompletedCatalogCheck(result,scanStarted,DateTimeOffset.UtcNow)){Preferences.LastCheck=DateTimeOffset.UtcNow;Preferences.ScheduledFailureCount=0;store.SavePreferences(Preferences);LastScan=$"Catalog checked {Preferences.LastCheck.Value.ToLocalTime():g}";Changed(nameof(ScheduleStatus));}
            else LastScan=$"Inventory scanned {DateTime.Now:g} · fresh catalog verification unavailable";
            Activity=$"{Rows.Count} applications discovered in {timer.Elapsed.TotalSeconds:F1}s";
            if(remote&&!online)Banner="You're offline. Installed inventory and history are available. Cached update results keep their original check time.";
            if(remote&&Providers.Any(x=>x.Name=="WinGet"&&!x.Available))Banner=Providers.First(x=>x.Name=="WinGet").Message;
            Changed(nameof(SourceSummary));Changed(nameof(Storage));
            store.Log("performance",$"scan elapsed_ms={timer.ElapsedMilliseconds}; records={Rows.Count}; remote={online}");
            return true;
        }
        catch(OperationCanceledException)when(stopped){return false;}
        catch(Exception ex)when(ex is not OperationCanceledException){ReportError(ex);return false;}
        finally{Busy=false;scanGate.Release();}
    }
    private Task UpdateAsync(HashSet<string>? selected)=>RunMaintenanceAsync(()=>UpdateCoreAsync(selected));
    private async Task UpdateCoreAsync(HashSet<string>? selected)
    {
        var plan=await BuildPlanAsync(selected);if(plan==null)return;
        await ExecutePlanAsync(plan,Preferences.AutomaticVendorHandoffs);
    }
    private async Task<QueuePlan?> BuildPlanAsync(HashSet<string>? selected)
    {
        if(selected is {Count:0}){Banner="Select applications using the checkboxes before updating the selection.";return null;}
        if(!await ScanAsync(true)){Banner="The inventory refresh did not finish. No update queue was started. Resolve the scan or storage issue, then retry.";return null;}
        var candidates=currentApps.Where(x=>!GamePlatforms.IsGaming(x)&&(selected==null||selected.Contains(x.Id))).ToArray();
        return await Task.Run(()=>{var processes=RunningApplications.Capture();return UpdatePolicy.Plan(candidates,Preferences,DateTimeOffset.UtcNow,MachineServices.InstallationRestartRequired(),processes.UnsafeToInstall,MachineServices.FreeSpace());});
    }
    private Task PreviewPlanAsync()=>RunMaintenanceAsync(PreviewPlanCoreAsync);
    private async Task PreviewPlanCoreAsync()
    {
        var plan=await BuildPlanAsync(null);if(plan==null)return;
        Operations.Clear();Deferred.Clear();PreviewActions.Clear();SelectedApp=null;
        foreach(var item in plan.Automatic)PreviewActions.Add($"{item.App.Name} · {item.App.InstalledVersion} → {item.App.AvailableVersion} · {item.App.Provider} · {item.App.Scope}/{item.App.Architecture}");
        int handoffs=Preferences.AutomaticVendorHandoffs?Math.Min(2,plan.Handoffs.Count):0;
        foreach(var item in plan.Handoffs.Take(handoffs))PreviewActions.Add($"Publisher handoff · {vendors.Targets.FirstOrDefault(x=>x.Id==item.App.VendorId)?.Name??item.App.VendorId} · Opens once; completion needs rechecking.");
        foreach(var item in plan.Deferred)Deferred.Add(item);
        IsPlanPreview=true;QueueDetailsExpanded=true;Page="Updates";
        QueueText=$"{plan.Automatic.Count} eligible routine update(s) · {handoffs} manager handoff(s) · {plan.Deferred.Count} updates need review";
        Banner="Preview only. No installers or managers were started. Update All refreshes inventory and checks the plan again before executing.";
    }
    private Task ReviewSupervisedAsync(string id)=>RunMaintenanceAsync(()=>ReviewSupervisedCoreAsync(id));
    private async Task ReviewSupervisedCoreAsync(string id)
    {
        if(!await ScanAsync(true)){Banner="Refresh did not finish. No official installer was downloaded.";return;}
        var app=currentApps.SingleOrDefault(x=>x.Id==id);
        if(app==null||!app.CanSupervise){Banner="A reviewed interactive update is no longer available for this installation.";return;}
        string? blocked=await Task.Run(()=>UpdatePolicy.BlockReason(app,Preferences,DateTimeOffset.UtcNow,MachineServices.InstallationRestartRequired(),MachineServices.Running(app),MachineServices.FreeSpace(app),true));
        if(blocked!=null){Banner=blocked;return;}
        Window review;
        if(app.EffectiveDelivery==UpdateDelivery.OfficialInstaller)
        {
            if(official.ReleaseFor(app) is not OfficialRelease release){Banner="The official release descriptor is no longer available. Refresh and review again.";return;}
            review=new OfficialReviewWindow(app,release);
        }
        else review=new WinGetReviewWindow(app);
        review.Owner=Application.Current.MainWindow;
        if(review.ShowDialog()!=true){Banner="Installer review closed. No download or installation was started.";return;}
        await ExecutePlanAsync(new([],[],[]){Supervised=[new(app,"Supervised","Individually reviewed interactive update")]},false,true);
    }
    private async Task ExecutePlanAsync(QueuePlan plan,bool allowHandoffs,bool supervised=false)
    {
        if(stopped)return;
        IsPlanPreview=false;PreviewActions.Clear();
        Deferred.Clear();foreach(var item in plan.Deferred)Deferred.Add(item);
        Operations.Clear();foreach(var item in plan.Automatic.Concat(plan.Supervised))Operations.Add(new(){AppId=item.App.Id,Name=item.App.Name,Provider=item.App.Provider,PreviousVersion=item.App.InstalledVersion,TargetVersion=item.App.AvailableVersion});
        SelectedApp=null;QueueDetailsExpanded=true;
        Page="Updates";
        bool handoffs=allowHandoffs&&plan.Handoffs.Count>0;
        var executionPlan=plan with{Handoffs=plan.Handoffs.Take(2).ToArray()};
        if(plan.Automatic.Count+plan.Supervised.Count==0&&!handoffs){QueueText=$"0 routine updates ready · {plan.Deferred.Count} updates need review";Banner="No installers started. "+UpdatePolicy.DeferredSummary(plan);return;}
        QueueRunning=true;bool receivingProgress=true;
        try
        {
            var progress=new Progress<QueueProgress>(p=>
            {
                if(!receivingProgress||stopped)return;
                QueueText=$"{p.Completed} of {p.Total} operations finished · {p.Activity}";
                if(p.Operation!=null){activeId=p.Operation.AppId;var old=Operations.FirstOrDefault(x=>x.AppId==p.Operation.AppId);if(old!=null)Operations[Operations.IndexOf(old)]=p.Operation;else Operations.Add(p.Operation);}
            });
            var results=await queue.RunAsync(executionPlan,handoffs,progress,app=>UpdatePolicy.BlockReason(app,Preferences,DateTimeOffset.UtcNow,MachineServices.InstallationRestartRequired(),MachineServices.Running(app),MachineServices.FreeSpace(app),supervised));
            receivingProgress=false;Operations.Clear();foreach(var result in results)Operations.Add(result);
            var verified=results.Count(x=>x.State==OperationState.Verified);var failures=results.Count(x=>x.State==OperationState.Failed);var handed=results.Count(x=>x.State==OperationState.Handoff);
            QueueText=$"{verified} verified · {failures} failed · {results.Count(x=>x.State is OperationState.Cancelled or OperationState.Deferred)} skipped/deferred · {results.Count(x=>x.State==OperationState.InteractionRequired)} need interaction · {handed} handoffs · {results.Count(x=>x.State==OperationState.AwaitingVerification)} awaiting verification";
            Banner=QueueText+(results.Any(x=>x.RebootRequired)?" · Restart Windows manually, then recheck.":"")+(plan.Handoffs.Count>2?" · Additional vendor managers remain; open them individually.":"");
            Activity=QueueText;Notification?.Invoke(Banner);LoadHistory();
            if(verified>0&&!results.Any(x=>x.RequiresInspection)&&!stopped)
            {
                // Keep maintenance ownership while refreshing the visible inventory, but allow
                // normal window close now that this queue has no active installer.
                QueueRunning=false;string summary=QueueText;await ScanAsync(true);if(!stopped)Activity=summary;
            }
        }
        finally{receivingProgress=false;QueueRunning=false;activeId=null;}
    }
    private Task Handoff(string id)=>RunMaintenanceAsync(()=>HandoffCoreAsync(id));
    private async Task HandoffCoreAsync(string id)
    {
        if(stopped)return;
        var result=await vendors.LaunchAsync(id,lifetime.Token);Banner=result.Message;
        var app=currentApps.FirstOrDefault(x=>x.VendorId==id);
        store.SaveOperation(new(){AppId=app?.Id??id,Name=vendors.Targets.FirstOrDefault(x=>x.Id==id)?.Name??id,Provider=id,PreviousVersion=app?.InstalledVersion??"",State=result.State,Message=result.Message,FinishedAt=DateTimeOffset.UtcNow});LoadHistory();
    }
    private void LoadHistory(){var selectedId=SelectedOperation?.Id;History.Clear();foreach(var op in store.LoadOperations())History.Add(op);SelectedOperation=History.FirstOrDefault(x=>x.Id==selectedId);Changed(nameof(HasHistory));Changed(nameof(Details));}
    private async Task RunMaintenanceAsync(Func<Task> operation)
    {
        if(stopped)return;
        try{await maintenance.TryRunAsync(async()=>{CommandManager.InvalidateRequerySuggested();await operation();});}
        finally{CommandManager.InvalidateRequerySuggested();}
    }
    private async Task RunScheduledCheckAsync()
    {
        var previous=Preferences.LastCheck;Preferences.LastScheduledAttempt=DateTimeOffset.UtcNow;
        try
        {
            store.SavePreferences(Preferences);
            bool completed=await ScanAsync(true);if(stopped)return;
            bool checkedCatalog=completed&&Preferences.LastCheck!=previous;
            Preferences.ScheduledFailureCount=checkedCatalog?0:Math.Min(6,Preferences.ScheduledFailureCount+1);
            store.SavePreferences(Preferences);Changed(nameof(ScheduleStatus));
        }
        catch(OperationCanceledException)when(stopped){}
        catch(Exception ex){ReportError(ex);}
    }
    private Task ExportInventory()
    {
        var dialog=new SaveFileDialog{Filter="UpdateDeck manifest (*.json)|*.json",FileName="UpdateDeck-inventory.json"};
        if(dialog.ShowDialog()==true)File.WriteAllText(dialog.FileName,InventoryManifest.Capture(currentApps,Preferences,System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()).ToJson());
        return Task.CompletedTask;
    }
    private Task ImportPreview()
    {
        var dialog=new OpenFileDialog{Filter="UpdateDeck manifest (*.json)|*.json"};if(dialog.ShowDialog()!=true)return Task.CompletedTask;
        if(new FileInfo(dialog.FileName).Length>5*1024*1024)throw new InvalidDataException("Manifest exceeds the 5 MB limit.");
        var manifest=InventoryManifest.Parse(File.ReadAllText(dialog.FileName));
        new MigrationPreviewWindow(manifest,currentApps){Owner=Application.Current.MainWindow}.ShowDialog();
        return Task.CompletedTask;
    }
    private Task ExportDiagnostics()
    {
        var dialog=new SaveFileDialog{Filter="Diagnostic report (*.json)|*.json",FileName="UpdateDeck-diagnostics.json"};
        if(dialog.ShowDialog()==true)File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(new{version="2.0.1",at=DateTimeOffset.UtcNow,os=system?.OS,architecture=system?.Architecture,inventoryCount=Rows.Count,providers=Providers.Select(x=>new{x.Name,x.Available,x.Capabilities}),history=History.Select(x=>new{x.Name,x.Provider,x.PreviousVersion,x.TargetVersion,x.State,x.StartedAt,x.FinishedAt,x.ExitCode,x.RebootRequired,x.RequiresInspection,x.RecordedProcessEnded})},StateStore.Json));
        return Task.CompletedTask;
    }
    public void ReportError(Exception ex)
    {
        Banner=ex switch
        {
            InvalidOperationException=>ex.Message,
            JsonException or InvalidDataException=>"This file or provider response could not be validated. Check its source and choose a supported file, then retry.",
            Microsoft.Data.Sqlite.SqliteException=>"UpdateDeck could not read or save its local data. Check storage permissions and any removable drive, then recheck installed versions before retrying.",
            UnauthorizedAccessException=>"Windows denied access to a required location. Check its permissions or organizational policy, then retry.",
            IOException=>"A required file or storage location is unavailable. Check the drive and file permissions, then retry.",
            System.Net.Http.HttpRequestException=>"The official source could not be reached. Check your connection or proxy and scan again.",
            TimeoutException=>"A source did not respond within its time limit. Other inventory sources remain available; try again later.",
            _=>"An operation could not be completed. Details were saved locally. Recheck the installed state before retrying."
        };
        Activity="Needs attention — review the message above";store.Log("error",ex.ToString());
    }
    public void RefreshMotionPreferences(){Changed(nameof(ScanAnimation));Changed(nameof(QueueAnimation));}
    public void SaveWindow(double width,double height,StoredWindowBounds? bounds=null,bool maximized=false){Preferences.WindowWidth=width;Preferences.WindowHeight=height;Preferences.WindowBounds=bounds;Preferences.WindowMaximized=maximized;try{store.SavePreferences(Preferences);}catch(Exception ex){ReportError(ex);}}
    public void Stop(){if(stopped)return;stopped=true;scheduler.Stop();lifetime.Cancel();official.Dispose();}
}

