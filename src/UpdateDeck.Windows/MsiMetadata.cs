using System.Runtime.InteropServices;
using System.Text;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

/// <summary>Queries installed product metadata and cached MSI summary information with update count zero. Never configures or repairs a product.</summary>
public static class MsiMetadata
{
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern uint MsiGetProductInfoExW(string product,string? sid,uint context,string property,StringBuilder value,ref uint length);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern uint MsiGetSummaryInformationW(uint database,string path,uint updateCount,out uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern uint MsiSummaryInfoGetPropertyW(uint summary,uint property,out uint type,out int integer,out long time,StringBuilder value,ref uint length);
    [DllImport("msi.dll",ExactSpelling=true)]private static extern uint MsiCloseHandle(uint handle);
    public static string? ArchitectureFromTemplate(string template)
    {
        var components=template.Split(';');if(components.Length!=2)return null;
        return components[0].Trim().ToUpperInvariant() switch{"INTEL" or ""=>"X86","X64"=>"X64","ARM64"=>"Arm64",_=>null};
    }
    public static Software Enrich(Software app)
    {
        if(app.Architecture!="Unknown"||app.InstallerType!="MSI"||app.Scope is not ("Machine" or "User"))return app;
        foreach(var code in app.ProductCodes.Where(x=>Guid.TryParseExact(x,"B",out _)))
        foreach(uint context in app.Scope=="Machine"?new uint[]{4}:new uint[]{2,1})
        {
            uint summary=0;
            try
            {
                var value=new StringBuilder(32768);uint length=(uint)value.Capacity;
                if(MsiGetProductInfoExW(code,null,context,"LocalPackage",value,ref length)!=0)continue;
                string path=Path.GetFullPath(value.ToString());string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Installer")+Path.DirectorySeparatorChar;
                if(!path.StartsWith(cache,StringComparison.OrdinalIgnoreCase)||!path.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                if(MsiGetSummaryInformationW(0,path,0,out summary)!=0||summary==0)continue;
                value.Clear();length=(uint)value.Capacity;
                if(MsiSummaryInfoGetPropertyW(summary,7,out uint type,out _,out _,value,ref length)!=0||type!=30)continue;
                string? architecture=ArchitectureFromTemplate(value.ToString());if(architecture==null)continue;
                return app with{Architecture=architecture,Evidence=app.Evidence.Append("Installed Windows Installer product: cached package Summary Template read without modification").ToArray()};
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){}
            finally{if(summary!=0)MsiCloseHandle(summary);}
        }
        return app;
    }
}
