using Microsoft.Win32;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public sealed class RegistryInventory : IInventorySource
{
    public string Name=>"Registry";
    public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>Task.Run(()=>
    {
        var apps=new List<Software>();int failures=0;
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
        foreach(var view in Environment.Is64BitOperatingSystem?new[]{RegistryView.Registry64,RegistryView.Registry32}:new[]{RegistryView.Registry32})
        {
            try
            {
                using var root=RegistryKey.OpenBaseKey(hive,view);using var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if(uninstall==null)continue;
                foreach(var id in uninstall.GetSubKeyNames())
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        using var key=uninstall.OpenSubKey(id);if(key==null)continue;
                        string Read(string name)=>Convert.ToString(key.GetValue(name))?.Trim()??"";
                        var name=Read("DisplayName");
                        if(name.Length==0||Read("SystemComponent")=="1"||Read("ParentKeyName").Length>0||Read("ReleaseType").Contains("Update",StringComparison.OrdinalIgnoreCase))continue;
                        var scope=hive==RegistryHive.LocalMachine?"Machine":"User";
                        var location=Read("InstallLocation").Trim('"');var version=Read("DisplayVersion");
                        var app=new Software {Id=Software.Key($"reg|{hive}|{view}|{id}"),Name=name,Publisher=Read("Publisher"),InstalledVersion=version.Length>0?version:"Unknown",InstallLocation=location,IconPath=Read("DisplayIcon"),ProductCodes=[id],Scope=scope,Architecture="Unknown",InstallerType=Read("WindowsInstaller")=="1"?"MSI":"Registered desktop",Evidence=[$"{hive}/{view}/Uninstall/{id}"]};
                        if(id.StartsWith("Steam App ",StringComparison.OrdinalIgnoreCase)&&id[10..].Length is >0 and <=20&&id[10..].All(char.IsAsciiDigit)&&Read("UninstallString").Contains("steam://uninstall/"+id[10..],StringComparison.OrdinalIgnoreCase))
                            app=app with{Kind=SoftwareKind.Game,PackageId="steam:"+id[10..],Provider="Steam",VendorId="steam",State=UpdateState.VendorManaged,InstallerType="Steam game",Risk=Risk.Caution,Explanation="Steam manages this game's application and content. Open Steam manually; Update All does not start game downloads.",Evidence=app.Evidence.Append("Steam app registration and uninstall protocol agree").ToArray()};
                        if(app.Kind!=SoftwareKind.Game&&id.StartsWith("GOG.com Games ",StringComparison.OrdinalIgnoreCase)&&Read("UninstallString").Contains("unins",StringComparison.OrdinalIgnoreCase))
                            app=app with{Kind=SoftwareKind.Game,Provider="GOG",VendorId="gog",State=UpdateState.VendorManaged,Risk=Risk.Caution,InstallerType="GOG game",Explanation="GOG game registration. Manage this title in GOG GALAXY or with its official offline installer."};
                        apps.Add(ExecutableMetadata.EnrichRegistration(MsiMetadata.Enrich(app)));
                    }
                    catch(Exception ex) when(ex is System.Security.SecurityException or UnauthorizedAccessException or IOException){failures++;}
                }
            }
            catch(Exception ex) when(ex is System.Security.SecurityException or UnauthorizedAccessException or IOException){failures++;}
        }
        return new DiscoveryResult(apps,[new(Name,true,ProviderCapability.Inventory,$"{apps.Count} registrations read across user and machine registry views. {failures} inaccessible entries.",DateTimeOffset.UtcNow)]);
    },cancellation);
}
