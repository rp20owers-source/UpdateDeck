using System.Diagnostics;
using Microsoft.Win32;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public sealed class AudioPluginInventory(Preferences preferences,Func<IEnumerable<string>>? knownFolders=null) : IInventorySource
{
    public string Name=>"Audio plugins";
    public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>Task.Run(()=>
    {
        if(!preferences.AudioPluginInventory)return new DiscoveryResult([],[new(Name,true,ProviderCapability.Inventory,"Optional audio-plugin discovery is off. Enable it in Settings to read plugin metadata without loading any DLL.",DateTimeOffset.UtcNow)]);
        var apps=new List<Software>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int visited=0,folders=0,inaccessible=0;bool limited=false;
        var exclusions=preferences.ExcludedDirectories.ToArray();
        var roots=(knownFolders?.Invoke()??KnownFolders()).Concat(preferences.AudioPluginDirectories.ToArray()).Where(SafeRoot).Distinct(StringComparer.OrdinalIgnoreCase).Take(48).ToArray();
        foreach(var root in roots)
        {
            var pending=new Stack<(string Path,int Depth)>();pending.Push((root,0));
            while(pending.TryPop(out var current))
            {
                cancellation.ThrowIfCancellationRequested();if(folders>=1200||visited>=12000||apps.Count>=3000){limited=true;break;}
                if(exclusions.Any(x=>Under(current.Path,x)))continue;
                try
                {
                    if(!Directory.Exists(current.Path)||(File.GetAttributes(current.Path)&FileAttributes.ReparsePoint)!=0)continue;folders++;
                    foreach(var file in Directory.EnumerateFiles(current.Path).Take(2000))
                    {
                        cancellation.ThrowIfCancellationRequested();if(++visited>12000||apps.Count>=3000){limited=true;break;}
                        string extension=Path.GetExtension(file).ToLowerInvariant();if(extension is not (".dll" or ".vst3" or ".clap")||!seen.Add(Path.GetFullPath(file))||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
                        string? format=extension switch{".vst3" when PeExports.Contains(file,"GetPluginFactory","_GetPluginFactory@0")=>"VST3",".clap" when PeExports.Contains(file,"clap_entry")=>"CLAP",".dll" when PeExports.Contains(file,"VSTPluginMain","_VSTPluginMain@4")=>"VST2",_=>null};
                        if(format==null)continue;
                        var info=FileVersionInfo.GetVersionInfo(file);var version=info.ProductVersion??info.FileVersion??"Unknown";
                        var name=string.IsNullOrWhiteSpace(info.ProductName)?Path.GetFileNameWithoutExtension(file):info.ProductName;
                        apps.Add(new(){Id=Software.Key("audio-plugin|"+Path.GetFullPath(file)),Name=name,Publisher=info.CompanyName??"Unknown publisher",InstalledVersion=version,Kind=SoftwareKind.AudioPlugin,
                            InstallLocation=current.Path,PrimaryExecutable=file,Architecture=ExecutableMetadata.Architecture(file)??"Unknown",Scope=Under(file,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))?"User":"Plugin",Provider="Audio plugins",InstallerType=format,Risk=Risk.Caution,State=UpdateState.Unknown,
                            Evidence=[$"{format} entry symbol found in the PE export table; no code was loaded","File version resource; optional plugin directory"],Explanation=$"{format} plugin binary discovered from file exports and metadata. A module can contain several instruments/effects; individual classes are not loaded. Update status is unknown. Preserve presets and projects; use the publisher's manager."});
                    }
                    if(current.Depth<5)foreach(var child in Directory.EnumerateDirectories(current.Path).Take(400))pending.Push((child,current.Depth+1));
                }
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception){inaccessible++;}
            }
            if(limited)break;
        }
        return new DiscoveryResult(apps,[new(Name,true,ProviderCapability.Inventory|ProviderCapability.Versions,$"{apps.Count} plugin modules in {folders} folders; {inaccessible} inaccessible folders. "+(limited?"The bounded scan limit was reached. Choose narrower plugin folders.":"VST2, VST3 and CLAP exports are read without loading DLLs. Update versions remain unknown."),DateTimeOffset.UtcNow)]);
    },cancellation);
    public static bool SafeRoot(string? path)=>!string.IsNullOrWhiteSpace(path)&&Path.IsPathFullyQualified(path)&&Path.GetPathRoot(path)?.TrimEnd('\\','/')!=path.TrimEnd('\\','/');
    private static bool Under(string path,string parent)=>parent.Length>0&&(IdentityResolver.Normalize(path).Equals(IdentityResolver.Normalize(parent),StringComparison.OrdinalIgnoreCase)||path.StartsWith(parent.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
    public static IEnumerable<string> KnownFolders()
    {
        var common=new[]{Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Common")}.Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var folder in common){yield return Path.Combine(folder,"VST3");yield return Path.Combine(folder,"CLAP");}
        foreach(var path in (Environment.GetEnvironmentVariable("CLAP_PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries).Take(16))if(SafeRoot(path))yield return path;
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            string? path=null;try{using var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var vst=root.OpenSubKey(@"SOFTWARE\VST");path=vst?.GetValue("VSTPluginsPath") as string;}catch(Exception ex)when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException){}
            if(SafeRoot(path))yield return path!;
        }
    }
}
