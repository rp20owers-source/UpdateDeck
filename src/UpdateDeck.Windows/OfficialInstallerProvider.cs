using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public sealed record OfficialRelease(string Product,string Version,string Architecture,Uri Download,string Sha256,long Size,Uri ReleasePage);
public sealed record OfficialCheck(IReadOnlyList<Software> Apps,ProviderReport Report);

/// <summary>A deliberately small reviewed catalog. Unknown publishers, scopes and channels never authorize a download.</summary>
public sealed partial class OfficialInstallerProvider : IUpdateExecutor,IDisposable
{
    private const ProviderCapability Capabilities=ProviderCapability.Check|ProviderCapability.Install|ProviderCapability.Verify|ProviderCapability.Versions|ProviderCapability.ReleaseNotes|ProviderCapability.Download|ProviderCapability.Progress|ProviderCapability.RestartDetection|ProviderCapability.InteractiveInstall;
    public const string ProductId="notepad-plus-plus";
    public static readonly Uri MetadataUri=new("https://api.github.com/repos/notepad-plus-plus/notepad-plus-plus/releases/latest");
    private readonly OfficialDownloads downloads;
    private readonly string cacheRoot;
    private readonly Func<string,bool> trust;
    private readonly Func<Software,Software?>? installedValidator;
    private readonly Func<string,OfficialRelease,bool>? payloadValidator;
    private readonly Func<Software,string?> launchBoundary;
    private readonly Func<Software,CancellationToken,Task<bool?>> pinStatus;
    private readonly Func<string,string,CancellationToken,Task<ProcessResult>> launch;
    private readonly Dictionary<string,OfficialRelease> releases=new(StringComparer.Ordinal);
    public OfficialInstallerProvider(string dataDirectory,OfficialDownloads? downloads=null,Func<string,bool>? trust=null,Func<string,string,CancellationToken,Task<ProcessResult>>? launch=null,Func<Software,Software?>? installedValidator=null,Func<string,OfficialRelease,bool>? payloadValidator=null,Func<Software,string?>? launchBoundary=null,Func<Software,CancellationToken,Task<bool?>>? pinStatus=null)
    {
        cacheRoot=Path.Combine(Path.GetFullPath(dataDirectory),"Downloads");this.downloads=downloads??new();this.trust=trust??IsNotepadSigned;this.launch=launch??LaunchInteractiveAsync;
        this.installedValidator=installedValidator;this.payloadValidator=payloadValidator;
        this.pinStatus=pinStatus??ReadPinStatusAsync;
        this.launchBoundary=launchBoundary??(app=>MachineServices.InstallationRestartRequired()?"Restart Windows before another installation.":MachineServices.Running(app)?"The application is running. Close it normally and retry.":null);
    }
    [GeneratedRegex(@"^Notepad\+\+(?: \((?:64-bit x64|32-bit x86|ARM64|x64|x86)\))?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex ProductName();
    [GeneratedRegex(@"^[0-9]+(?:\.[0-9]+){1,3}$")]private static partial Regex ReleaseVersion();
    [GeneratedRegex(@"^sha256:([a-fA-F0-9]{64})$")]private static partial Regex Digest();
    public static bool IsCandidate(Software app)=>app.Provider=="Registry"&&app.FamilyName.Length==0&&app.VendorId.Length==0&&!app.Ambiguous&&!app.Pinned&&
        app.Scope=="Machine"&&ProductName().IsMatch(app.Name)&&app.Publisher is "Notepad++ Team" or "Notepad++"&&Versioning.Known(app.InstalledVersion)&&!Versioning.IsPrerelease(app.InstalledVersion);
    private Software? ValidateInstalled(Software app)
    {
        if(!IsCandidate(app))return null;
        if(installedValidator!=null)return installedValidator(app);
        var path=ExecutableMetadata.LocalIconExecutable(app.PrimaryExecutable.Length>0?app.PrimaryExecutable:app.IconPath);
        if(path==null||!Path.GetFileName(path).Equals("notepad++.exe",StringComparison.OrdinalIgnoreCase)||!trust(path))return null;
        var file=FileVersionInfo.GetVersionInfo(path);
        if(file.ProductName!="Notepad++"||Versioning.Compare(file.FileVersion,app.InstalledVersion)!=0)return null;
        var architecture=ExecutableMetadata.Architecture(path);if(architecture is not ("X64" or "X86" or "Arm64"))return null;
        var directory=Path.GetDirectoryName(path)!;
        if(!SafeInstallDirectory(directory)||app.InstallLocation.Length>0&&!IdentityResolver.Normalize(app.InstallLocation).Equals(directory,StringComparison.OrdinalIgnoreCase))return null;
        return app with{InstallLocation=directory,PrimaryExecutable=path,Architecture=architecture};
    }
    public static bool SafeInstallDirectory(string directory)=>Path.IsPathFullyQualified(directory)&&!ExecutableMetadata.NetworkPath(directory)&&directory.IndexOfAny(['"','\r','\n','\0'])<0&&Path.GetPathRoot(directory)?.TrimEnd('\\')!=directory.TrimEnd('\\');
    public async Task<OfficialCheck> AttachAsync(IReadOnlyList<Software> apps,bool checkUpdates,CancellationToken cancellation)
    {
        releases.Clear();var now=DateTimeOffset.UtcNow;
        var eligible=new List<(Software Original,Software Validated)>();
        foreach(var app in apps.Where(IsCandidate))
        {
            cancellation.ThrowIfCancellationRequested();
            try{var validated=ValidateInstalled(app);if(validated!=null)eligible.Add((app,validated));}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException){}
        }
        if(!checkUpdates||eligible.Count==0)return new(apps,new("Official installers",true,Capabilities,eligible.Count==0?"No uncovered, signed installation matches the reviewed fallback catalog (Notepad++). Existing managers retain ownership.":"Local signed product recognized. Check updates for the publisher's release metadata.",now));
        try
        {
            var blocked=new Dictionary<string,Software>();
            foreach(var group in eligible.GroupBy(x=>x.Validated.Scope+"|"+x.Validated.Architecture).Where(x=>x.Count()>1).ToArray())
                foreach(var pair in group){eligible.Remove(pair);blocked[pair.Original.Id]=pair.Original with{Ambiguous=true,State=UpdateState.Attention,Explanation="Multiple Notepad++ registrations share this scope and architecture. The reviewed installer cannot safely select between them. Inspect the registrations before updating."};}
            foreach(var pair in eligible.ToArray())
            {
                bool? pin=await pinStatus(pair.Validated,cancellation);
                if(pin!=false){eligible.Remove(pair);blocked[pair.Original.Id]=pair.Original with{Pinned=pin==true,State=UpdateState.Attention,Explanation=pin==true?"Pinned in WinGet. The direct installer will not override this policy.":"Existing WinGet pins could not be ruled out. The direct installer is disabled; review pins or use the established updater."};}
            }
            if(eligible.Count==0)return new(apps.Select(x=>blocked.GetValueOrDefault(x.Id,x)).ToArray(),new("Official installers",false,ProviderCapability.None,"Pin policy or ambiguous registrations block the reviewed fallback. Inspect the application details. No installer was downloaded.",now));
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            string metadata=await downloads.MetadataAsync(MetadataUri,timeout.Token);
            var replacement=new Dictionary<string,Software>();
            foreach(var pair in eligible)
            {
                var app=pair.Validated;var release=ParseRelease(metadata,app.Architecture);
                if(release==null)continue;
                int? comparison=Versioning.Compare(release.Version,app.InstalledVersion);if(comparison==null)continue;
                releases[ReleaseKey(app)]=release;
                replacement[app.Id]=app with{Provider="Official installer",Delivery=UpdateDelivery.OfficialInstaller,OfficialInstallerId=ProductId,PackageId="Notepad++.Notepad++",PinVerified=true,AvailableVersion=release.Version,CheckedAt=now,State=comparison>0?UpdateState.Available:UpdateState.Current,
                    Evidence=app.Evidence.Append("Installed Notepad++ Authenticode signer, file version and PE architecture verified").Append("Official publisher GitHub stable release metadata with SHA-256 digest").ToArray(),
                    Explanation=comparison>0?"A newer stable release is available from the publisher's official GitHub repository. Review and launch the signed installer individually; finish its license and elevation prompts yourself.":"Installed version is at least the latest stable release from the publisher's official repository."};
            }
            return new(apps.Select(x=>replacement.GetValueOrDefault(x.Id,blocked.GetValueOrDefault(x.Id,x))).ToArray(),new("Official installers",true,Capabilities,$"{replacement.Count} signed installations checked against the reviewed publisher repository. Interactive installation only.",now){RemoteCheckCompleted=replacement.Count>0});
        }
        catch(Exception ex)when(ex is not OperationCanceledException||!cancellation.IsCancellationRequested)
        {return new(apps,new("Official installers",false,ProviderCapability.None,$"Official release verification unavailable: {ex.Message}. No download or installation was authorized.",now));}
    }
    public static OfficialRelease? ParseRelease(string metadata,string architecture)
    {
        try
        {
            if(metadata.Length>2*1024*1024)return null;
            using var document=JsonDocument.Parse(metadata);var root=document.RootElement;
            if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
            var tag=root.GetProperty("tag_name").GetString()??"";var version=tag.StartsWith('v')?tag[1..]:"";
            if(!ReleaseVersion().IsMatch(version))return null;
            string? suffix=architecture switch{"X64"=>".x64","X86"=>"","Arm64"=>".arm64",_=>null};if(suffix==null)return null;
            string filename=$"npp.{version}.Installer{suffix}.exe";
            string expected=$"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/{tag}/{filename}";
            string page=$"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/tag/{tag}";
            if(root.GetProperty("html_url").GetString()!=page)return null;
            var assets=root.GetProperty("assets").EnumerateArray().Where(x=>x.GetProperty("name").GetString()==filename).ToArray();if(assets.Length!=1)return null;
            var asset=assets[0];var digest=Digest().Match(asset.GetProperty("digest").GetString()??"");long size=asset.GetProperty("size").GetInt64();
            if(!digest.Success||size is <100000 or >50000000||asset.GetProperty("state").GetString()!="uploaded"||asset.GetProperty("browser_download_url").GetString()!=expected)return null;
            return new(ProductId,version,architecture,new Uri(expected),digest.Groups[1].Value,size,new Uri(page));
        }
        catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){return null;}
    }
    private static string ReleaseKey(Software app)=>Software.Key(app.PrimaryExecutable+"|"+app.Architecture);
    public OfficialRelease? ReleaseFor(Software app)=>releases.GetValueOrDefault(ReleaseKey(app));
    public async Task<ExecutionResult> ExecuteAsync(Software app,IProgress<string> progress,CancellationToken cancellation)
    {
        if(!app.CanSupervise||app.OfficialInstallerId!=ProductId||!releases.TryGetValue(ReleaseKey(app),out var release)||release.Version!=app.AvailableVersion||release.Architecture!=app.Architecture||!app.HasFreshCheck)return new(OperationState.Deferred,"Refresh and review the official release before launching its installer.");
        // Validate the existing registration again. Persisted metadata alone can never authorize execution.
        var installed=ValidateInstalled(app with{Provider="Registry"});
        if(installed==null||installed.PrimaryExecutable!=app.PrimaryExecutable||installed.InstallLocation!=app.InstallLocation)return new(OperationState.Deferred,"The installed file's identity changed. Rescan before downloading.");
        string? filePath=null;FileStream? payloadLease=null;bool retained=false,launchAttempted=false;
        try
        {
            progress.Report("Checking existing package pins before downloading");
            if(await pinStatus(app,cancellation)!=false)return new(OperationState.Deferred,"The package is pinned or its pins cannot be verified. No direct installer was started.");
            Directory.CreateDirectory(cacheRoot);
            if((File.GetAttributes(cacheRoot)&FileAttributes.ReparsePoint)!=0)throw new IOException("The installer download directory cannot be a filesystem link.");
            filePath=Path.Combine(cacheRoot,"notepad-"+Guid.NewGuid().ToString("N")+".exe");
            await using(var destination=new FileStream(filePath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,64*1024,FileOptions.Asynchronous))
            {
                progress.Report("Downloading the official installer over HTTPS");
                await downloads.DownloadAsync(release,destination,progress,cancellation);await destination.FlushAsync(cancellation);
                if(!await OfficialDownloads.HashMatchesAsync(destination,release.Sha256,cancellation))return new(OperationState.Failed,"The installer SHA-256 does not match official release metadata. It was not started.");
            }
            // Keep a deny-write/delete handle from validation through process completion to prevent payload replacement.
            var locked=payloadLease=new FileStream(filePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            bool validPayload=payloadValidator!=null?payloadValidator(filePath,release):trust(filePath)&&Versioning.Compare(FileVersionInfo.GetVersionInfo(filePath).FileVersion,release.Version)==0;
            if(!await OfficialDownloads.HashMatchesAsync(locked,release.Sha256,cancellation)||!validPayload)return new(OperationState.Failed,"The downloaded installer signature, publisher, version or integrity check failed. It was not started.");
            if(ValidateInstalled(app with{Provider="Registry"})==null)return new(OperationState.Deferred,"The installed application changed during the download. Rescan before retrying.");
            var boundary=launchBoundary(app);if(boundary!=null)return new(OperationState.Deferred,boundary);
            if(await pinStatus(app,cancellation)!=false)return new(OperationState.Deferred,"The package pin policy changed or could not be verified. The direct installer was not started.");
            cancellation.ThrowIfCancellationRequested();
            progress.Report("Official installer opened. Complete its license and Windows elevation prompts; UpdateDeck will verify the installed file afterward.");
            launchAttempted=true;var result=await launch(filePath,app.InstallLocation,cancellation);
            if(result.TimedOut||result.OutputIncomplete)
            {
                retained=true;InstallerPayloadRetention.Retain(filePath,locked,result.MonitoringCompletion);
                return new(OperationState.AwaitingVerification,"The installer outcome is still uncertain. Pending work stops. Inspect it and any child installers before retrying. Its downloaded payload remains protected while monitoring is unresolved.",StopQueue:true,ActiveProcessId:result.ProcessId,ActiveProcessStartedUtcTicks:result.StartedUtcTicks);
            }
            if(result.ExitCode is 0 or 3010)return new(OperationState.AwaitingVerification,"The interactive installer ended. Checking the exact installed executable independently.",result.ExitCode,result.ExitCode==3010);
            return new(OperationState.InteractionRequired,$"The official installer ended with code {result.ExitCode}. It may have been cancelled. No update success is claimed.",result.ExitCode);
        }
        catch(System.ComponentModel.Win32Exception ex)when(ex.NativeErrorCode==1223){return new(OperationState.InteractionRequired,"Windows elevation was cancelled. No update success is claimed.");}
        catch(Exception ex)when(launchAttempted||ex is not OperationCanceledException)
        {
            if(launchAttempted)
            {
                retained=true;InstallerPayloadRetention.Retain(filePath!,payloadLease!,null);
                return new(OperationState.AwaitingVerification,$"Installer launch or monitoring could not be confirmed ({ex.GetType().Name}). Inspect publisher dialogs and child installers before retrying. Pending work stopped and its payload remains protected until this app exits.",StopQueue:true);
            }
            return new(OperationState.Failed,$"The official installer could not be completed: {ex.Message}");
        }
        finally{if(!retained){payloadLease?.Dispose();if(filePath!=null)try{File.Delete(filePath);}catch(IOException){}catch(UnauthorizedAccessException){}}}
    }
    public async Task<ExecutionResult> VerifyAsync(Software app,CancellationToken cancellation)
    {
        for(int attempt=0;attempt<3;attempt++)
        {
            if(attempt>0)await Task.Delay(1500,cancellation);
            try
            {
                if(trust(app.PrimaryExecutable)&&ExecutableMetadata.Architecture(app.PrimaryExecutable)==app.Architecture)
                {
                    var info=FileVersionInfo.GetVersionInfo(app.PrimaryExecutable);
                    if(info.ProductName=="Notepad++"&&Versioning.Compare(info.FileVersion,app.AvailableVersion) is >=0)return new(OperationState.Verified,$"Signed executable at the original installation path now reports version {info.FileVersion}; target {app.AvailableVersion} verified.");
                }
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException){}
        }
        return new(OperationState.AwaitingVerification,"The target version at the original installation path could not be verified. Finish the publisher workflow and recheck; execution alone is not success.");
    }
    public static bool IsNotepadSigned(string path)
    {
        try
        {
            if(!Authenticode.Valid(path))return false;
#pragma warning disable SYSLIB0057
            using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.GetNameInfo(X509NameType.SimpleName,false).Equals("NOTEPAD++",StringComparison.OrdinalIgnoreCase);
        }catch{return false;}
    }
    private static async Task<bool?> ReadPinStatusAsync(Software app,CancellationToken cancellation)
    {
        using var guard=WinGetPinStorage.Acquire();if(!guard.Safe)return null;
        string? cli=WinGetCliLocator.Find();
        if(cli==null)return !guard.HasDatabases&&WinGetCliLocator.AppInstallerPresent()==false?false:null;
        var provider=new WinGetProvider(new ProcessRunner(),()=>cli);
        var pins=await provider.ReadPinsAsync(cancellation,app with{PackageId="Notepad++.Notepad++"});
        return pins.Verified?WinGetPins.IsPinned(app with{PackageId="Notepad++.Notepad++"},pins.Ids):null;
    }
    private static async Task<ProcessResult> LaunchInteractiveAsync(string executable,string installDirectory,CancellationToken cancellation)
    {
        if(!SafeInstallDirectory(installDirectory))throw new InvalidDataException("The installation directory is not safe for the reviewed NSIS argument.");
        cancellation.ThrowIfCancellationRequested();
        // NSIS explicitly requires an unquoted /D as the last argument, including paths with spaces.
        var process=Process.Start(new ProcessStartInfo(executable){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(executable)!,Arguments="/D="+installDirectory});
        if(process==null)return new(-1,"","Windows did not return an installer process.",true);
        int id=process.Id;long? started=null;try{started=process.StartTime.ToUniversalTime().Ticks;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        var completion=ObserveInteractiveCompletionAsync(process);
        try{return new(await completion.WaitAsync(TimeSpan.FromHours(6),cancellation),"","");}
        catch(Exception ex)when(ex is OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {return new(-1,"","Installer monitoring is unresolved; it was not terminated.",true,id,started,WasCancelled:cancellation.IsCancellationRequested){MonitoringCompletion=completion};}
    }
    private static async Task<int> ObserveInteractiveCompletionAsync(Process process)
    {try{await process.WaitForExitAsync();return process.ExitCode;}finally{process.Dispose();}}
    public void Dispose()=>downloads.Dispose();
}

public sealed class UpdateExecutor(IUpdateExecutor winget,IUpdateExecutor official) : IUpdateExecutor,ISupervisedUpdateExecutor
{
    private IUpdateExecutor? For(Software app)=>app.EffectiveDelivery switch{UpdateDelivery.WinGet=>winget,UpdateDelivery.OfficialInstaller=>official,_=>null};
    public Task<ExecutionResult> ExecuteAsync(Software app,IProgress<string> progress,CancellationToken cancellation)=>For(app)?.ExecuteAsync(app,progress,cancellation)??Task.FromResult(new ExecutionResult(OperationState.Deferred,"No reviewed execution provider is available."));
    public Task<ExecutionResult> VerifyAsync(Software app,CancellationToken cancellation)=>For(app)?.VerifyAsync(app,cancellation)??Task.FromResult(new ExecutionResult(OperationState.AwaitingVerification,"No independent verifier is available."));
    public Task<ExecutionResult> ExecuteSupervisedAsync(Software app,IProgress<string> progress,CancellationToken cancellation)=>For(app) is ISupervisedUpdateExecutor interactive?interactive.ExecuteSupervisedAsync(app,progress,cancellation):app.EffectiveDelivery==UpdateDelivery.OfficialInstaller?official.ExecuteAsync(app,progress,cancellation):Task.FromResult(new ExecutionResult(OperationState.Deferred,"No reviewed interactive execution method is available."));
}

