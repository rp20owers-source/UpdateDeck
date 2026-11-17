using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace UpdateDeck.Core;

public enum UpdateState { Unknown, Current, Available, VendorManaged, Attention, Ignored }
public enum Risk { Routine, Caution, SystemSensitive, High }
public enum UpdateDelivery { None, WinGet, OfficialInstaller }
public enum SoftwareKind { Application, AudioPlugin, Game, GameLauncher }
public enum OperationState { Pending, Running, Verifying, Verified, AwaitingVerification, Failed, Deferred, InteractionRequired, Handoff, Cancelled, Interrupted, Observed }
[Flags] public enum ProviderCapability { None=0, Inventory=1, Check=2, Install=4, Handoff=8, Verify=16, Versions=32, ReleaseNotes=64, Download=128, AutomaticInstall=256, SilentInstall=512, Progress=1024, RestartDetection=2048, InteractiveInstall=4096, Rollback=8192 }

public sealed record Software
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstalledVersion { get; init; } = "Unknown";
    public string[] InstalledVersionEvidence { get; init; } = [];
    public bool VersionEvidenceConflict {get;init;}
    public string AvailableVersion { get; init; } = "";
    public string InstalledChannel { get; init; } = "";
    public string AvailableChannel { get; init; } = "";
    public string PackageId { get; init; } = "";
    public string ProviderInstanceId { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string[] ProductCodes { get; init; } = [];
    public string InstallLocation { get; init; } = "";
    public string IconPath { get; init; } = "";
    public string PrimaryExecutable { get; init; } = "";
    public string Architecture { get; init; } = "Unknown";
    public string Scope { get; init; } = "Unknown";
    public string InstallerType { get; init; } = "Unknown";
    public string Provider { get; init; } = "Registry";
    public SoftwareKind Kind { get; init; }
    public UpdateDelivery Delivery { get; init; }
    public string OfficialInstallerId { get; init; } = "";
    public string[] Evidence { get; init; } = [];
    public string VendorId { get; init; } = "";
    public string VendorActionLabel {get;init;}="";
    public UpdateState State { get; init; }
    public Risk Risk { get; init; }
    public string Explanation { get; init; } = "Installed software found. A trusted update source has not verified its status.";
    public DateTimeOffset? CheckedAt { get; init; }
    public DateTimeOffset? CatalogUpdatedAt {get;init;}
    public DateTimeOffset? InventoryObservedAt {get;init;}
    public bool Pinned { get; init; }
    public bool PinVerified { get; init; }
    public bool Ambiguous { get; init; }
    public bool IsDriver { get; init; }
    [JsonIgnore] public bool HasFreshCheck => CheckedAt.HasValue && DateTimeOffset.UtcNow-CheckedAt.Value is var age && age>=TimeSpan.FromMinutes(-2) && age<=TimeSpan.FromMinutes(30)&&
        (EffectiveDelivery!=UpdateDelivery.WinGet||CatalogFreshness.IsFresh(CatalogUpdatedAt,DateTimeOffset.UtcNow));
    [JsonIgnore] public UpdateDelivery EffectiveDelivery => Delivery != UpdateDelivery.None ? Delivery : Provider == "WinGet" ? UpdateDelivery.WinGet : UpdateDelivery.None;
    [JsonIgnore] public bool HasVerifiedUpdateMetadata => HasFreshCheck && Scope is "User" or "Machine" && Architecture is "X64" or "X86" or "Arm64" && Path.IsPathFullyQualified(InstallLocation) && !InstallLocation.StartsWith(@"\\",StringComparison.Ordinal) &&
        Versioning.Known(InstalledVersion)&&Versioning.Compare(AvailableVersion,InstalledVersion) is >0&&Versioning.SameReleaseTrack(InstalledVersion,AvailableVersion)&&InstalledChannel.Equals(AvailableChannel,StringComparison.OrdinalIgnoreCase)&&(EffectiveDelivery!=UpdateDelivery.WinGet||InstalledChannel.Length==0);
    [JsonIgnore] public bool CanUpdate => HasVerifiedUpdateMetadata && !GamePlatforms.IsGaming(this) && VendorId.Length==0 && EffectiveDelivery == UpdateDelivery.WinGet && State == UpdateState.Available && !Ambiguous && !VersionEvidenceConflict && !Pinned && PinVerified && Risk == Risk.Routine;
    [JsonIgnore] public bool CanSupervise => HasVerifiedUpdateMetadata && !GamePlatforms.IsGaming(this) && State == UpdateState.Available && !Ambiguous && !VersionEvidenceConflict && !Pinned && VendorId.Length==0 &&
        (EffectiveDelivery == UpdateDelivery.OfficialInstaller && OfficialInstallerId.Length > 0 && Risk == Risk.Routine || EffectiveDelivery==UpdateDelivery.WinGet&&PinVerified&&Risk is Risk.Routine or Risk.Caution);
    public static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..24];
    public override string ToString()=>Name;
}

public sealed record ProviderReport(string Name, bool Available, ProviderCapability Capabilities, string Message, DateTimeOffset CheckedAt)
{
    public bool RemoteCheckCompleted {get;init;}
    public DateTimeOffset? DataUpdatedAt {get;init;}
    public override string ToString()=>Name;
}
public sealed record DiscoveryResult(IReadOnlyList<Software> Apps, IReadOnlyList<ProviderReport> Providers);
public sealed record RestartAssessment(bool ServicingRequired,bool WindowsUpdateRequired,bool PendingFileOperations,bool InspectionFailed=false)
{
    public bool BlocksInstallation=>ServicingRequired||WindowsUpdateRequired||InspectionFailed;
    public bool RestartRecommended=>BlocksInstallation||PendingFileOperations;
    public string Message=>InspectionFailed?"Windows restart requirements could not be inspected. Installation is deferred.":ServicingRequired||WindowsUpdateRequired?"Windows Update or servicing requires a restart before installation.":PendingFileOperations?"Windows has queued file operations. Restart is recommended; this alone does not block unrelated application updates. Installers still perform their own checks.":"No restart requirement detected.";
}
public sealed record MachineInfo(string OS, string Architecture, string Processor, string[] Graphics, bool PendingRestart)
{
    public bool InstallationRestartRequired {get;init;}=true;
    public string RestartNotice {get;init;}="";
    public IReadOnlyList<HardwareSection> Hardware {get;init;}=[];
}
public sealed record StoredWindowBounds(int Left,int Top,int Width,int Height);
public sealed class Preferences
{
    public static readonly string[] SupportedFilters=["All applications","Updates available","Verified current","Vendor managed","Needs attention","Ignored","Unknown status","Audio plugins","All discovered software"];
    public static readonly string[] SupportedSorts=["Name","Publisher","Installed version","Available version","Status","Provider","Last checked"];
    public string Theme { get; set; } = "Dark";
    public bool ScanOnStartup { get; set; } = true;
    public string Schedule { get; set; } = "Manual";
    public bool Tray { get; set; }
    public bool Notifications { get; set; } = true;
    public bool AutomaticVendorHandoffs { get; set; } = true;
    public bool PauseInstallations { get; set; }
    public bool AllowMeteredChecks { get; set; }
    public int QuietStart { get; set; } = 22;
    public int QuietEnd { get; set; } = 7;
    public List<string> PortableDirectories { get; set; } = [];
    public List<string> ExcludedDirectories { get; set; } = [];
    public bool AudioPluginInventory { get; set; }
    public List<string> AudioPluginDirectories { get; set; } = [];
    public HashSet<string> Ignored { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Filter { get; set; } = "All applications";
    public string Sort { get; set; } = "Name";
    public bool SortDescending {get;set;}
    public double WindowWidth { get; set; } = 1340;
    public double WindowHeight { get; set; } = 870;
    public StoredWindowBounds? WindowBounds {get;set;}
    public bool WindowMaximized {get;set;}
    public DateTimeOffset? LastCheck { get; set; }
    public DateTimeOffset? LastScheduledAttempt {get;set;}
    public int ScheduledFailureCount {get;set;}
    public void ApplyUserSettings(Preferences draft)
    {
        draft.Validate();Theme=draft.Theme;ScanOnStartup=draft.ScanOnStartup;Schedule=draft.Schedule;Tray=draft.Tray;Notifications=draft.Notifications;
        AutomaticVendorHandoffs=draft.AutomaticVendorHandoffs;PauseInstallations=draft.PauseInstallations;AllowMeteredChecks=draft.AllowMeteredChecks;QuietStart=draft.QuietStart;QuietEnd=draft.QuietEnd;
        PortableDirectories=draft.PortableDirectories.ToList();ExcludedDirectories=draft.ExcludedDirectories.ToList();AudioPluginDirectories=draft.AudioPluginDirectories.ToList();AudioPluginInventory=draft.AudioPluginInventory;
        Validate(); // Preserve live ignore policy, last-check time, current view and window placement.
    }
    public void Validate()
    {
        if (Theme is not ("Dark" or "Light" or "System")) Theme = "Dark";
        if (Schedule is not ("Manual" or "Daily" or "Weekly")) Schedule = "Manual";
        if(!SupportedFilters.Contains(Filter,StringComparer.Ordinal))Filter="All applications";
        if(!SupportedSorts.Contains(Sort,StringComparer.Ordinal))Sort="Name";
        QuietStart = Math.Clamp(QuietStart, 0, 23); QuietEnd = Math.Clamp(QuietEnd, 0, 23);
        ScheduledFailureCount=Math.Clamp(ScheduledFailureCount,0,6);
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 900, 3840) : 1340;
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 620, 2160) : 870;
        if(WindowBounds is { } bounds&&(bounds.Left is <-131072 or >131072||bounds.Top is <-131072 or >131072||bounds.Width is <300 or >16384||bounds.Height is <200 or >16384))WindowBounds=null;
        if(Ignored==null||Ignored.Count>10000)PauseInstallations=true;
        PortableDirectories ??= []; ExcludedDirectories ??= []; AudioPluginDirectories ??= []; Ignored ??= [];
        PortableDirectories = PortableDirectories.Where(x=>!string.IsNullOrWhiteSpace(x)&&Path.IsPathFullyQualified(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        ExcludedDirectories = ExcludedDirectories.Where(x=>!string.IsNullOrWhiteSpace(x)&&Path.IsPathFullyQualified(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToList();
        AudioPluginDirectories = AudioPluginDirectories.Where(x=>!string.IsNullOrWhiteSpace(x)&&Path.IsPathFullyQualified(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        Ignored=new HashSet<string>(Ignored.Where(x=>!string.IsNullOrWhiteSpace(x)).Take(10000),StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record PlanItem(Software App, string Action, string Reason){public override string ToString()=>$"{App.Name} · {Action}: {Reason}";}
public sealed record QueuePlan(IReadOnlyList<PlanItem> Automatic, IReadOnlyList<PlanItem> Handoffs, IReadOnlyList<PlanItem> Deferred)
{
    public IReadOnlyList<PlanItem> Supervised {get;init;}=[];
}
public sealed record UpdateOperation
{
    [JsonRequired]public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string AppId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Provider { get; init; } = "";
    public string PreviousVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    [JsonRequired]public OperationState State { get; init; } = OperationState.Pending;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; init; }
    public string Message { get; init; } = "";
    public int? ExitCode { get; init; }
    public bool RebootRequired { get; init; }
    public bool RequiresInspection { get; init; }
    public int? ActiveProcessId { get; init; }
    public long? ActiveProcessStartedUtcTicks { get; init; }
    public bool RecordedProcessEnded {get;init;}
    public InstallationSnapshot? Installation {get;init;}
    [JsonIgnore]public string ResultLabel=>State is OperationState.Running or OperationState.Verifying?State.ToString():RequiresInspection?"Needs inspection":State switch
    {
        OperationState.AwaitingVerification=>"Awaiting verification",OperationState.InteractionRequired=>"Interaction required",
        OperationState.Handoff=>"Publisher handoff",OperationState.Observed=>"Observed change",
        _=>Enum.IsDefined(State)?State.ToString():"Unknown result"
    };
    public override string ToString()=>$"{Name} · {ResultLabel}";
}
public sealed record ExecutionResult(OperationState State, string Message, int? ExitCode=null, bool RebootRequired=false, bool Transient=false, bool StopQueue=false,int? ActiveProcessId=null,long? ActiveProcessStartedUtcTicks=null);
public sealed record QueueProgress(int Completed, int Total, string Activity, UpdateOperation? Operation);

public interface IInventorySource { string Name { get; } Task<DiscoveryResult> DiscoverAsync(bool checkUpdates, CancellationToken cancellation); }
public interface IUpdateExecutor
{
    Task<ExecutionResult> ExecuteAsync(Software app, IProgress<string> progress, CancellationToken cancellation);
    Task<ExecutionResult> VerifyAsync(Software app, CancellationToken cancellation);
}
public interface ISupervisedUpdateExecutor
{
    Task<ExecutionResult> ExecuteSupervisedAsync(Software app,IProgress<string> progress,CancellationToken cancellation);
}
public interface IOperationStore
{
    void SaveOperation(UpdateOperation operation);
    IReadOnlyList<UpdateOperation> LoadOperations();
}
public interface IVendorLauncher { Task<ExecutionResult> LaunchAsync(string vendorId, CancellationToken cancellation); }
