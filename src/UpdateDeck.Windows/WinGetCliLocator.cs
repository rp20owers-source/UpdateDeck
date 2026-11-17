using System.Security.Cryptography.X509Certificates;

namespace UpdateDeck.Windows;

/// <summary>Resolves Microsoft's installed App Installer, rather than trusting a same-named PATH executable.</summary>
public static class WinGetCliLocator
{
    public static string? Find()
    {
        try
        {
            var manager=new global::Windows.Management.Deployment.PackageManager();
            var packages=manager.FindPackagesForUser("").Where(p=>p.Id.Name=="Microsoft.DesktopAppInstaller"&&p.Id.PublisherId=="8wekyb3d8bbwe").ToArray();
            foreach(var package in packages.OrderByDescending(p=>p.Id.Version.Major).ThenByDescending(p=>p.Id.Version.Minor).ThenByDescending(p=>p.Id.Version.Build).ThenByDescending(p=>p.Id.Version.Revision))
            {
                var executable=Path.Combine(package.InstalledLocation.Path,"winget.exe");
                if(IsMicrosoftExecutable(executable))return executable;
            }
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){}
        var fallback=ProcessRunner.FindExecutable("winget.exe");
        return fallback!=null&&IsMicrosoftExecutable(fallback)?fallback:null;
    }
    public static bool IsMicrosoftExecutable(string path)
    {
        try
        {
            if(!Path.IsPathFullyQualified(path)||!File.Exists(path)||!Path.GetFileName(path).Equals("winget.exe",StringComparison.OrdinalIgnoreCase)||!Authenticode.Valid(path))return false;
            var resources=System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            if(!string.Equals(resources.OriginalFilename,"winget.exe",StringComparison.OrdinalIgnoreCase)||resources.ProductName!="Microsoft Desktop App Installer")return false;
#pragma warning disable SYSLIB0057
            using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.GetNameInfo(X509NameType.SimpleName,false).Equals("Microsoft Corporation",StringComparison.OrdinalIgnoreCase);
        }
        catch{return false;}
    }
    public static bool? AppInstallerPresent()
    {
        try{return new global::Windows.Management.Deployment.PackageManager().FindPackagesForUser("").Any(p=>p.Id.Name.StartsWith("Microsoft.DesktopAppInstaller",StringComparison.Ordinal)&&p.Id.PublisherId=="8wekyb3d8bbwe");}catch{return null;}
    }
}
