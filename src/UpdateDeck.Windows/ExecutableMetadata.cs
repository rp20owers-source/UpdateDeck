using System.Diagnostics;
using System.Text.RegularExpressions;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

/// <summary>Reads resources and PE headers only. It never loads an application or plugin as executable code.</summary>
public static partial class ExecutableMetadata
{
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]private static partial Regex NonIdentity();
    [GeneratedRegex(@"(?:CORPORATION|INCORPORATED|LIMITED|CORP|INC|LLC|LTD)$")]private static partial Regex LegalSuffix();
    public static bool PublisherTextMatches(string publisher,string company,string product)
    {
        string a=LegalSuffix().Replace(Normalize(publisher),""),b=LegalSuffix().Replace(Normalize(company),"");
        return a.Length>=3&&b.Length>=3&&(a==b||product.Equals("Notepad++",StringComparison.OrdinalIgnoreCase)&&a=="NOTEPADTEAM"&&b=="DONHODONHFREEFR");
    }
    public static string? LocalIconExecutable(string? icon)
    {
        if(string.IsNullOrWhiteSpace(icon)||icon.Length>32767)return null;
        var value=icon.Trim();int comma=value.LastIndexOf(',');
        if(comma>0&&int.TryParse(value[(comma+1)..],out _))value=value[..comma];
        value=Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if(!value.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!Path.IsPathFullyQualified(value)||NetworkPath(value))return null;
        try{return File.Exists(value)?Path.GetFullPath(value):null;}catch(ArgumentException){return null;}catch(NotSupportedException){return null;}
    }
    public static bool NetworkPath(string path)=>path.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)||(path.StartsWith(@"\\",StringComparison.Ordinal)&&!path.StartsWith(@"\\?\",StringComparison.Ordinal));
    public static Software EnrichRegistration(Software app)
    {
        try
        {
            var executable=LocalIconExecutable(app.IconPath);if(executable==null)return app;
            string leaf=Path.GetFileNameWithoutExtension(executable);
            if(leaf.Contains("unins",StringComparison.OrdinalIgnoreCase)||leaf.Contains("setup",StringComparison.OrdinalIgnoreCase)||leaf.Equals("cleanup",StringComparison.OrdinalIgnoreCase)||leaf.StartsWith("vcredist",StringComparison.OrdinalIgnoreCase)||leaf.StartsWith("vc_redist",StringComparison.OrdinalIgnoreCase))return app;
            var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\')+"\\";
            if(executable.StartsWith(windows,StringComparison.OrdinalIgnoreCase))return app; // MSI cache icons and system launchers are not app executables.
            var info=FileVersionInfo.GetVersionInfo(executable);
            var publisher=Normalize(app.Publisher);var company=Normalize(info.CompanyName??"");var name=Normalize(app.Name);var product=Normalize(info.ProductName??"");
            if(!PublisherTextMatches(app.Publisher,info.CompanyName??"",info.ProductName??"")||product.Length<3||!name.Contains(product,StringComparison.Ordinal))return app;
            bool versionMatches=Versioning.Compare(app.InstalledVersion,info.ProductVersion)==0||Versioning.Compare(app.InstalledVersion,info.FileVersion)==0;
            if(!versionMatches)return app;
            string directory=Path.GetDirectoryName(executable)!;
            if(app.InstallLocation.Length>0&&!IdentityResolver.Normalize(app.InstallLocation).Equals(IdentityResolver.Normalize(directory),StringComparison.OrdinalIgnoreCase)&&!executable.StartsWith(app.InstallLocation.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))return app;
            var architecture=Architecture(executable);
            return app with{InstallLocation=app.InstallLocation.Length>0?app.InstallLocation:directory,Architecture=app.Architecture=="Unknown"?architecture??app.Architecture:app.Architecture,PrimaryExecutable=executable,Evidence=app.Evidence.Append("Registered executable resources match product, publisher and installed version").ToArray()};
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception){return app;}
    }
    private static string Normalize(string text)=>NonIdentity().Replace(text,"").ToUpperInvariant();
    public static string? Architecture(string file)
    {
        try
        {
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader=new BinaryReader(stream);
            if(stream.Length<64||reader.ReadUInt16()!=0x5A4D)return null;
            stream.Position=60;int header=reader.ReadInt32();if(header<64||header>1024*1024||header>stream.Length-26)return null;
            stream.Position=header;if(reader.ReadUInt32()!=0x00004550)return null;
            ushort machine=reader.ReadUInt16();ushort sections=reader.ReadUInt16();
            if(sections is 0 or >96)return null;
            stream.Position=header+20;ushort optionalSize=reader.ReadUInt16();
            if(optionalSize<96||header+24L+optionalSize>stream.Length)return null;
            stream.Position=header+24;ushort magic=reader.ReadUInt16();
            if(machine==0x14c&&magic!=0x10b||machine is 0x8664 or 0xAA64&&magic!=0x20b)return null;
            if(machine==0x14c)
            {
                // Managed AnyCPU PE32 images must not be mistaken for an x86 installation.
                long clrDirectory=header+24+(magic==0x20b?112:96)+14*8;
                if(clrDirectory+8<=header+24L+optionalSize){stream.Position=clrDirectory;if(reader.ReadUInt32()!=0)return null;}
            }
            return machine switch{0x8664=>"X64",0xAA64=>"Arm64",0x14c=>"X86",_=>null};
        }
        catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(ArgumentException){return null;}
    }
}
