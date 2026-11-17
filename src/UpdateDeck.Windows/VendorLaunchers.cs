using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public sealed record VendorDefinition(string Id,string Name,string[] Products,string[] Publishers,string[] RelativePaths,string[] Signers,string OfficialUrl);
public sealed record VendorTarget(string Id,string Name,string? Executable,string OfficialUrl,string Explanation)
{
    public string Kind=>Executable!=null?"Installed application":OfficialUrl.StartsWith("https://",StringComparison.Ordinal)?"Official web guidance":"Windows handoff";
    public string ActionLabel=>Executable!=null?$"Open {Name}  ↗":OfficialUrl.StartsWith("https://",StringComparison.Ordinal)?"Open official guidance  ↗":$"Open {Name}  ↗";
    public override string ToString()=>Name;
}
public sealed class VendorLaunchers : IVendorLauncher
{
    // Built-in reviewed catalog, never downloaded executable command strings.
    public static readonly VendorDefinition[] Definitions=
    [
        new("nvidia","NVIDIA App",["NVIDIA App","NVIDIA Graphics Driver","NVIDIA HD Audio Driver"],["NVIDIA"],["NVIDIA Corporation/NVIDIA App/CEF/NVIDIA App.exe","NVIDIA Corporation/NVIDIA App/NVIDIA App.exe"],["NVIDIA Corporation"],"https://www.nvidia.com/en-us/software/nvidia-app/"),
        new("adobe","Adobe Creative Cloud",["Adobe Creative Cloud","Creative Cloud","Adobe Photoshop","Adobe Illustrator","Adobe InDesign","Adobe Premiere","Adobe After Effects","Adobe Audition","Adobe Lightroom"],["Adobe","Adobe Systems"],["Adobe/Adobe Creative Cloud/ACC/Creative Cloud.exe"],["Adobe Inc.","Adobe Systems Incorporated"],"https://helpx.adobe.com/creative-cloud/help/update-app.html"),
        new("acrobat","Adobe Acrobat update guidance",["Adobe Acrobat","Acrobat Reader"],["Adobe","Adobe Systems"],[],[],"https://helpx.adobe.com/acrobat/using/updating-acrobat-reader.html"),
        new("native","Native Access",["Native Access","Kontakt","Reaktor","Traktor","Massive","Absynth","Battery","Guitar Rig","Komplete Kontrol"],["Native Instruments"],["Native Instruments/Native Access/Native Access.exe"],["Native Instruments GmbH"],"https://www.native-instruments.com/en/specials/native-access/"),
        new("ik","IK Product Manager",["IK Product Manager","AmpliTube","SampleTank","TONEX","T-RackS","Hammond B-3X","MODO BASS","MODO DRUM","Syntronik","Miroslav Philharmonik","ARC System","Custom Shop","IK Multimedia Authorization Manager","Lurssen Mastering Console","MixBox","Pianoverse","SampleTron"],["IK Multimedia"],["IK Multimedia/IK Product Manager/IK Product Manager.exe"],["IK Multimedia Production srl","IK Multimedia Production SRL"],"https://www.ikmultimedia.com/products/productmanager/"),
        new("steam","Steam",["Steam"],["Valve"],["Steam/steam.exe"],["Valve Corp.","Valve Corporation"],"https://help.steampowered.com/en/"),
        new("epic","Epic Games Launcher",["Epic Games Launcher"],["Epic Games"],["Epic Games/Launcher/Portal/Binaries/Win64/EpicGamesLauncher.exe","Epic Games/Launcher/Portal/Binaries/Win32/EpicGamesLauncher.exe"],["Epic Games, Inc."],"https://www.epicgames.com/help/en-US/"),
        new("battlenet","Battle.net",["Battle.net"],["Blizzard"],["Battle.net/Battle.net Launcher.exe"],["Blizzard Entertainment, Inc."],"https://download.battle.net/en-us/desktop"),
        new("ea","EA app",["EA app","EA Desktop"],["Electronic Arts"],["Electronic Arts/EA Desktop/EA Desktop/EADesktop.exe"],["Electronic Arts, Inc.","Electronic Arts Inc."],"https://www.ea.com/ea-app"),
        new("ubisoft","Ubisoft Connect",["Ubisoft Connect","Uplay"],["Ubisoft"],["Ubisoft/Ubisoft Game Launcher/UbisoftConnect.exe"],["Ubisoft Entertainment"],"https://www.ubisoft.com/en-us/ubisoft-connect"),
        new("gog","GOG GALAXY",["GOG GALAXY"],["GOG"],["GOG Galaxy/GalaxyClient.exe"],["GOG sp. z o.o."],"https://www.gog.com/galaxy"),
        new("riot","Riot Client",["Riot Client"],["Riot Games"],["Riot Games/Riot Client/RiotClientServices.exe"],["Riot Games, Inc."],"https://www.riotgames.com/"),
        new("xbox","Xbox",["Xbox"],["Microsoft"],[],[],"https://www.xbox.com/en-US/apps/xbox-app-on-pc"),
        new("rockstar","Rockstar Games Launcher",["Rockstar Games Launcher"],["Rockstar"],["Rockstar Games/Launcher/Launcher.exe"],["Rockstar Games, Inc.","Rockstar Games, Inc"],"https://www.rockstargames.com/"),
        new("amazon","Amazon Games",["Amazon Games"],["Amazon"],["Amazon Games/App/Amazon Games.exe"],["Amazon.com Services LLC"],"https://gaming.amazon.com/amazon-games-app"),
        new("itch","itch.io",["itch"],["itch corp","itch.io"],["itch/itch.exe"],["itch corp."],"https://itch.io/app"),
        new("visualstudio","Visual Studio Installer",["Visual Studio Community","Visual Studio Professional","Visual Studio Enterprise","Visual Studio Build Tools","Visual Studio Installer","from Visual Studio"],["Microsoft"],["Microsoft Visual Studio/Installer/setup.exe"],["Microsoft Corporation"],"https://learn.microsoft.com/en-us/visualstudio/install/update-visual-studio"),
        new("imageline","Image-Line updates",["FL Studio"],["Image-Line"],[],[],"https://www.image-line.com/fl-studio-download/"),
        new("serato","Serato downloads",["Serato"],["Serato"],[],[],"https://serato.com/downloads"),
        new("rekordbox","rekordbox downloads",["rekordbox"],["AlphaTheta","Pioneer"],[],[],"https://rekordbox.com/en/download/"),
        new("motu","MOTU M-Series driver guidance",["MOTU M-Series","MOTU M Series","MOTU Audio"],["MOTU","Mark of the Unicorn"],[],[],"https://motu.com/m2-start"),
        new("boss","BOSS update guidance",["BOSS TONE STUDIO","BOSS TONE CENTRAL","ME-90 IR Loader"],["Roland","BOSS"],[],[],"https://www.boss.info/global/support/")
    ];
    private readonly Dictionary<string,VendorTarget> targets=new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<VendorTarget> Targets=>targets.Values.OrderBy(x=>x.Name).ToArray();
    public IReadOnlyList<Software> Attach(IReadOnlyList<Software> apps,Action<string>? diagnostic=null)
    {
        targets.Clear();var result=new List<Software>();
        foreach(var app in apps)
        {
            if(app.Kind==SoftwareKind.Game||app.Provider is "Store" or "MSIX" or "Scoop" or "Chocolatey" or "Steam" or "Epic" or "GOG"){result.Add(app);continue;}
            var vendor=Definitions.FirstOrDefault(v=>VendorIdentity.Matches(app,v));
            if(vendor==null){result.Add(app);continue;}
            if(!targets.ContainsKey(vendor.Id))
            {
                diagnostic?.Invoke("Checking "+vendor.Name);
                string? executable=null;
                var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}.Where(x=>x.Length>0).Distinct();
                foreach(var candidate in roots.SelectMany(root=>vendor.RelativePaths.Select(p=>Path.Combine(root,p.Replace('/',Path.DirectorySeparatorChar)))).Concat(vendor.RelativePaths.Where(_=>app.InstallLocation.Length>0).Select(p=>Path.Combine(app.InstallLocation,Path.GetFileName(p)))))
                    if(File.Exists(candidate)&&TrustedExecutable(candidate,vendor.Signers)){executable=candidate;break;}
                targets[vendor.Id]=new(vendor.Id,vendor.Name,executable,vendor.OfficialUrl,executable!=null?"Installed executable validated with Windows Authenticode and the expected publisher.":"A validated launcher was not found. Opens official publisher guidance in your browser.");
                diagnostic?.Invoke(vendor.Name+(executable!=null?"; validated executable":"; official guidance fallback"));
            }
            result.Add(app with {Kind=GamePlatforms.Contains(vendor.Id)?SoftwareKind.GameLauncher:app.Kind,VendorId=vendor.Id,State=UpdateState.VendorManaged,Risk=UpdatePolicy.Classify(app),Explanation=$"{vendor.Name} manages this software. Opening it is a handoff, not a verified update. Recheck the installed version afterward."});
        }
        if(apps.Any(x=>x.Provider=="Store"))targets["store"]=new("store","Microsoft Store",null,"ms-windows-store://downloadsandupdates","Opens Microsoft's registered Store update interface.");
        if(apps.Any(x=>x.Provider=="Scoop"))targets["scoop"]=new("scoop","Scoop",null,"https://github.com/ScoopInstaller/Scoop/wiki/Commands","Opens official update command guidance; no arbitrary shell is launched.");
        if(apps.Any(x=>x.Provider=="Chocolatey"))targets["chocolatey"]=new("chocolatey","Chocolatey",null,"https://docs.chocolatey.org/en-us/choco/commands/upgrade/","Opens official update command guidance; package ownership is preserved.");
        foreach(var vendor in Definitions.Where(v=>GamePlatforms.Contains(v.Id)))
        {
            if(targets.ContainsKey(vendor.Id))continue;
            string? executable=null;
            var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}.Where(x=>x.Length>0).Distinct();
            foreach(var candidate in roots.SelectMany(root=>vendor.RelativePaths.Select(p=>Path.Combine(root,p.Replace('/',Path.DirectorySeparatorChar)))))
                if(File.Exists(candidate)&&TrustedExecutable(candidate,vendor.Signers)){executable=candidate;break;}
            targets[vendor.Id]=new(vendor.Id,vendor.Name,executable,vendor.OfficialUrl,executable!=null?"Installed launcher validated. Open it to sign in and manage your library.":"No validated launcher found. Open the platform's official website for access and installation guidance.");
        }
        return result.Select(app=>app.VendorId.Length>0&&targets.TryGetValue(app.VendorId,out var target)?app with{VendorActionLabel=target.Executable!=null?"Open manager":target.OfficialUrl.StartsWith("https://",StringComparison.Ordinal)?"Open guidance":"Open Store"}:app).ToArray();
    }
    public async Task<ExecutionResult> LaunchAsync(string vendorId,CancellationToken cancellation)
    {
        if(!targets.TryGetValue(vendorId,out var target))return new(OperationState.Failed,"No reviewed handoff is available. Rescan installed applications.");
        return await Task.Run(()=>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(target.Executable!=null)
                {
                    var definition=Definitions.Single(x=>x.Id==vendorId);
                    if(!TrustedExecutable(target.Executable,definition.Signers))return new ExecutionResult(OperationState.Failed,"The vendor executable's publisher or signature could not be validated. No process was started.");
                    cancellation.ThrowIfCancellationRequested();
                    Process.Start(new ProcessStartInfo(target.Executable){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(target.Executable)!});
                }
                else
                {
                    if(!IsReviewedUrl(target.OfficialUrl))return new ExecutionResult(OperationState.Failed,"This handoff URL is not in the built-in official-source catalog.");
                    if(target.OfficialUrl.StartsWith("https://",StringComparison.Ordinal)&&!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())return new ExecutionResult(OperationState.InteractionRequired,"Official web guidance needs an internet connection. Connect and open it again when ready.");
                    cancellation.ThrowIfCancellationRequested();
                    Process.Start(new ProcessStartInfo(target.OfficialUrl){UseShellExecute=true});
                }
                string message=target.Executable==null&&target.OfficialUrl.StartsWith("https://",StringComparison.Ordinal)?$"Opened official guidance for {target.Name}. Follow the publisher's workflow, then recheck the installed version.":$"Opened {target.Name}. Finish in the official manager, then recheck. Update success has not been claimed.";
                return new ExecutionResult(OperationState.Handoff,message);
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){return new ExecutionResult(OperationState.Cancelled,"The pending publisher handoff was cancelled before launch.");}
            catch(Exception ex){return new ExecutionResult(OperationState.InteractionRequired,$"Windows could not open {target.Name}: {ex.Message}");}
        },cancellation);
    }
    public static bool IsReviewedUrl(string url)=>Definitions.Any(x=>x.OfficialUrl==url)||url is "ms-windows-store://downloadsandupdates" or "https://github.com/ScoopInstaller/Scoop/wiki/Commands" or "https://docs.chocolatey.org/en-us/choco/commands/upgrade/";
    private static bool TrustedExecutable(string path,string[] signers)
    {
        if(!Path.IsPathFullyQualified(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||signers.Length==0)return false;
        try
        {
            if(!Authenticode.Valid(path))return false;
#pragma warning disable SYSLIB0057
            using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return signers.Contains(certificate.GetNameInfo(X509NameType.SimpleName,false),StringComparer.OrdinalIgnoreCase);
        }catch{return false;}
    }
}
internal static class Authenticode
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct FileInfo {public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Path;public IntPtr Handle,KnownSubject;}
    [StructLayout(LayoutKind.Sequential)] private struct TrustData {public uint Size;public IntPtr Policy,Sip;public uint Ui,Revocation,Choice;public IntPtr File;public uint StateAction;public IntPtr State,Url;public uint Flags,Context;}
    [DllImport("wintrust.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]private static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
    public static bool Valid(string path)
    {
        var file=new FileInfo{Size=(uint)Marshal.SizeOf<FileInfo>(),Path=path};var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(file,pointer,false);var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),Ui=2,Choice=1,File=pointer,StateAction=1,Flags=0x00000080};
            int result=WinVerifyTrust(new IntPtr(-1),ref action,ref data);data.StateAction=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);return result==0;
        }
        finally{Marshal.DestroyStructure<FileInfo>(pointer);Marshal.FreeHGlobal(pointer);}
    }
}
