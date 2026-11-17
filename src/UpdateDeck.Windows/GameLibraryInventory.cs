using System.Text.Json;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

/// <summary>Reads installed Epic metadata only. Never runs manifest-provided commands.</summary>
public sealed class GameLibraryInventory : IInventorySource
{
    public string Name=>"Game libraries";
    public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>Task.Run(()=>
    {
        var apps=new List<Software>();int unreadable=0;
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Epic","EpicGamesLauncher","Data","Manifests");
        try
        {
            if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0)
                foreach(var path in Directory.EnumerateFiles(folder,"*.item",SearchOption.TopDirectoryOnly).Take(5000))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var file=new FileInfo(path);if(file.Length>256*1024||(file.Attributes&FileAttributes.ReparsePoint)!=0){unreadable++;continue;}
                        var app=ParseEpic(File.ReadAllText(path));if(app!=null)apps.Add(app);
                    }
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){unreadable++;}
                }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){unreadable++;}
        return new DiscoveryResult(apps,[new(Name,true,ProviderCapability.Inventory,$"{apps.Count} installed Epic entries; Steam and GOG registrations are read with desktop inventory. Other platforms use launcher handoffs. {unreadable} inaccessible metadata files.",DateTimeOffset.UtcNow)]);
    },cancellation);
    public static Software? ParseEpic(string json)
    {
        if(json.Length>256*1024)return null;
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});var r=doc.RootElement;if(r.ValueKind!=JsonValueKind.Object)return null;
        string Read(string key)=>r.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        var id=Read("AppName");var name=Read("DisplayName");var path=Read("InstallLocation");
        if(id.Length is 0 or >256||name.Length is 0 or >512||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||!Directory.Exists(path))return null;
        if(r.TryGetProperty("bIsIncompleteInstall",out var incomplete)&&incomplete.ValueKind==JsonValueKind.True)return null;
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return null;
        var version=Read("AppVersionString");
        return new Software{Id=Software.Key("epic|"+id+"|"+Path.GetFullPath(path)),Name=name,PackageId="epic:"+id,Provider="Epic",VendorId="epic",Kind=SoftwareKind.Game,InstalledVersion=version.Length>0?version:"Unknown",InstallLocation=path,Scope="Library",InstallerType="Epic game",State=UpdateState.VendorManaged,Risk=Risk.Caution,Explanation="Installed Epic manifest. Manage updates and game content in Epic Games Launcher. Availability is not independently verified.",Evidence=["Local Epic installed-game manifest; installation folder exists"]};
    }
}
