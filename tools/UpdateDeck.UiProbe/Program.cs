using System.Windows;
using System.IO;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using UpdateDeck.App;

namespace UpdateDeck.UiProbe;

public static class Program
{
    // Developer UI previews only. This executable never calls an update executor or vendor launcher.
    [STAThread]public static int Main(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="seed-paused")
            {
                string folder=Path.GetFullPath(args[1]);
                if(!File.Exists(Path.Combine(folder,"portable.mode"))||!File.Exists(Path.Combine(folder,"UpdateDeck.exe")))throw new InvalidOperationException("Select an owned extracted QA portable folder.");
                new StateStore(Path.Combine(folder,"Data"),true).SavePreferences(new(){PauseInstallations=true,AutomaticVendorHandoffs=false});Console.WriteLine("QA portable preferences seeded with installations paused and automatic handoffs off.");return 0;
            }
            if(args.Length!=1||args[0] is not ("official-review" or "winget-review")){Console.Error.WriteLine("Usage: UpdateDeck.UiProbe official-review|winget-review OR seed-paused <QA portable folder>. Preview buttons close the dialog only; no installer is called.");return 2;}
            Software software;OfficialRelease? release=null;
            var registry=new RegistryInventory().DiscoverAsync(false,CancellationToken.None).GetAwaiter().GetResult();
            software=registry.Apps.FirstOrDefault(x=>x.Name.StartsWith("Notepad++",StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("This read-only preview needs an installed Notepad++ registration.");
            using(var downloads=new OfficialDownloads())
            {
                string metadata=downloads.MetadataAsync(OfficialInstallerProvider.MetadataUri,CancellationToken.None).GetAwaiter().GetResult();release=OfficialInstallerProvider.ParseRelease(metadata,software.Architecture=="Unknown"?"X64":software.Architecture)??throw new InvalidOperationException("No reviewed official release metadata.");
            }
            software=software with{AvailableVersion=release.Version,PackageId="Notepad++.Notepad++"};
            var application=new Application();application.Resources.MergedDictionaries.Add(new(){Source=new Uri("pack://application:,,,/UpdateDeck;component/Themes/Controls.xaml")});
            Window dialog=args[0]=="official-review"?new OfficialReviewWindow(software,release):new WinGetReviewWindow(software);
            dialog.Title+=" · UI preview only";application.MainWindow=dialog;dialog.Loaded+=(_,_)=>ThemeManager.Apply("Dark");
            bool? result=dialog.ShowDialog();Console.WriteLine($"Preview closed ({result}). No download, installer or updater was started.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
