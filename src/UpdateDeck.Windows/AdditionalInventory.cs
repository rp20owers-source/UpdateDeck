using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

/// <summary>Read installed manager manifests, without changing repositories or running downloaded scripts.</summary>
public sealed class AdditionalInventory(Preferences preferences) : IInventorySource
{
    public string Name=>"Additional sources";
    public Task<DiscoveryResult> DiscoverAsync(bool checkUpdates,CancellationToken cancellation)=>Task.Run(()=>
    {
        var apps=new List<Software>();var reports=new List<ProviderReport>();
        var scoop=Environment.GetEnvironmentVariable("SCOOP")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"scoop");
        var global=Environment.GetEnvironmentVariable("SCOOP_GLOBAL")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"scoop");
        int scoopCount=0;
        foreach(var (root,scope) in new[]{(scoop,"User"),(global,"Machine")})
        {
            var path=Path.Combine(root,"apps");if(!Directory.Exists(path))continue;
            foreach(var dir in SafeDirectories(path))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var current=Path.Combine(dir,"current");var manifest=Path.Combine(current,"manifest.json");
                    if(!File.Exists(Path.Combine(current,"install.json"))||!File.Exists(manifest))continue;
                    using var json=JsonDocument.Parse(ReadManifest(manifest));
                    var id=Path.GetFileName(dir);var version=json.RootElement.TryGetProperty("version",out var v)?v.ToString():"Unknown";
                    apps.Add(new Software{Id=Software.Key("scoop|"+dir),Name=id,InstalledVersion=version,PackageId=id,InstallLocation=current,Provider="Scoop",Scope=scope,State=UpdateState.VendorManaged,VendorId="scoop",Evidence=["Scoop install.json + manifest.json"],Explanation="Owned by Scoop. Use the established manager; UpdateDeck does not migrate ownership."});scoopCount++;
                }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){}
            }
        }
        reports.Add(new("Scoop",scoopCount>0,ProviderCapability.Inventory|ProviderCapability.Handoff,$"{scoopCount} installed package manifests. Updates remain supervised in Scoop.",DateTimeOffset.UtcNow));
        var choco=Environment.GetEnvironmentVariable("ChocolateyInstall")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"chocolatey");int chocoCount=0;
        foreach(var dir in SafeDirectories(Path.Combine(choco,"lib")))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var file=Directory.EnumerateFiles(dir,"*.nuspec",SearchOption.TopDirectoryOnly).FirstOrDefault();if(file==null)continue;
                if(new FileInfo(file).Length>2*1024*1024)continue;
                using var reader=System.Xml.XmlReader.Create(file,new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024});
                var xml=XDocument.Load(reader);string Field(string n)=>xml.Descendants().FirstOrDefault(x=>x.Name.LocalName==n)?.Value??"";
                var id=Field("id");if(id.Length==0)continue;
                apps.Add(new Software{Id=Software.Key("choco|"+dir),Name=Field("title") is {Length:>0} title?title:id,PackageId=id,InstalledVersion=Field("version"),Publisher=Field("authors"),InstallLocation=dir,Scope="Machine",Provider="Chocolatey",State=UpdateState.VendorManaged,VendorId="chocolatey",Evidence=["Chocolatey installed nuspec"],Explanation="Owned by Chocolatey. Open official manager guidance to keep its package registration consistent."});chocoCount++;
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Xml.XmlException){}
        }
        reports.Add(new("Chocolatey",chocoCount>0,ProviderCapability.Inventory|ProviderCapability.Handoff,$"{chocoCount} installed package manifests. Automatic updates are not implemented for this provider.",DateTimeOffset.UtcNow));
        int portableCount=0;
        foreach(var root in preferences.PortableDirectories.Where(AudioPluginInventory.SafeRoot))
        {
            try{if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)continue;}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            if(preferences.ExcludedDirectories.Any(x=>IsUnder(root,x)))continue;
            // Deliberately limited to the selected directory and one child level, never a whole-drive recursive search.
            foreach(var dir in new[]{root}.Concat(SafeDirectories(root,199)))
            {
                if(preferences.ExcludedDirectories.Any(x=>IsUnder(dir,x)))continue;
                try
                {
                    foreach(var file in Directory.EnumerateFiles(dir,"*.exe",SearchOption.TopDirectoryOnly).Take(100))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
                        var f=FileVersionInfo.GetVersionInfo(file);
                        if(string.IsNullOrWhiteSpace(f.ProductName)||string.IsNullOrWhiteSpace(f.CompanyName)||string.IsNullOrWhiteSpace(f.ProductVersion)||Path.GetFileName(file).Contains("unins",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(file).Contains("setup",StringComparison.OrdinalIgnoreCase))continue;
                        apps.Add(new Software{Id=Software.Key("portable|"+file),Name=f.ProductName,Publisher=f.CompanyName,InstalledVersion=f.ProductVersion,InstallLocation=dir,IconPath=file,PrimaryExecutable=file,Architecture=ExecutableMetadata.Architecture(file)??"Unknown",Provider="Portable",Scope="Portable",InstallerType="Executable metadata",Explanation="Opt-in executable metadata. Portable installation and update availability are not independently verified.",Evidence=["File version resource; opt-in directory"]});portableCount++;
                    }
                }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){}
            }
        }
        reports.Add(new("Portable folders",true,ProviderCapability.Inventory,$"{preferences.PortableDirectories.Count} selected folders; {portableCount} metadata candidates. No arbitrary binaries are executed.",DateTimeOffset.UtcNow));
        return new DiscoveryResult(apps,reports);
    },cancellation);
    private static string ReadManifest(string file)
    {
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        if(stream.Length>2*1024*1024)throw new InvalidDataException("Manifest exceeds the inventory limit.");
        using var reader=new StreamReader(stream);var buffer=new char[4096];var text=new System.Text.StringBuilder();int count;
        while((count=reader.Read(buffer,0,buffer.Length))>0){if(text.Length+count>2*1024*1024)throw new InvalidDataException("Manifest exceeds the inventory limit.");text.Append(buffer,0,count);}return text.ToString();
    }
    private static string[] SafeDirectories(string root,int maximum=10000){try{return Directory.EnumerateDirectories(root).Take(maximum).Where(x=>(File.GetAttributes(x)&FileAttributes.ReparsePoint)==0).ToArray();}catch(IOException){return [];}catch(UnauthorizedAccessException){return [];}}
    private static bool IsUnder(string path,string root)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
