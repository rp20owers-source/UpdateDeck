using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;

namespace UpdateDeck.App;

public partial class App : Application
{
    private Mutex? instance;
    private Mutex? installerGuard;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationWait;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)){MessageBox.Show("UpdateDeck requires Windows 10 version 2004 (build 19041) or newer. Windows 10 security support depends on your edition and servicing program.","UpdateDeck");Shutdown(2);return;}
            installerGuard=new Mutex(false,"UpdateDeck.Running");
            if(e.Args.Length==2&&e.Args[0]=="--diagnose")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                string output=Path.GetFullPath(e.Args[1]);
                // Start after the dispatcher pump is established, including on a fresh installation.
                _=Dispatcher.InvokeAsync(async()=>
                {
                    try{await Diagnose(output);Shutdown(0);}
                    catch(Exception ex){try{File.WriteAllText(output,JsonSerializer.Serialize(new{error=ex.Message},StateStore.Json));}finally{Shutdown(1);}}
                });
                return;
            }
            var key=Software.Key(WindowsIdentity.GetCurrent().User?.Value??Environment.UserName);
            instance=new Mutex(true,"Local\\UpdateDeck-"+key,out bool first);
            activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\UpdateDeck-activate-"+key);
            if(!first){activate.Set();Shutdown();return;}
            var store=StateStore.OpenDefault(AppContext.BaseDirectory);var model=new MainViewModel(store);var window=new MainWindow(model,e.Args.Contains("--compact",StringComparer.OrdinalIgnoreCase));MainWindow=window;window.Show();
            DispatcherUnhandledException+=(_,args)=>{model.ReportError(args.Exception);args.Handled=true;};
            activationWait=ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(window.ShowWindow);},null,Timeout.Infinite,false);
        }
        catch(Exception ex){try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"UpdateDeck-startup-error.txt"),ex.ToString());}catch{}MessageBox.Show($"UpdateDeck could not start: {ex.Message}\nCheck that its data directory is writable and Windows policy permits this application.","UpdateDeck",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
    }
    private static async Task Diagnose(string output)
    {
        var timer=Stopwatch.StartNew();var runner=new ProcessRunner();var provider=new WinGetProvider(runner);
        var prefs=new Preferences();var vendors=new VendorLaunchers();var store=new StateStore(Path.Combine(Path.GetDirectoryName(output)!,"diagnostic-data"));
        store.Log("diagnostic.start","Read-only diagnostic started");
        using var official=new OfficialInstallerProvider(store.DirectoryPath);
        var coordinator=new InventoryCoordinator([new RegistryInventory(),new GameLibraryInventory(),new StoreInventory(),new AdditionalInventory(prefs),new AudioPluginInventory(prefs),provider],vendors,store,official:official);
        var result=await coordinator.ScanAsync(true,new Progress<string>(),CancellationToken.None);
        var machine=await new MachineServices(runner).InspectAsync(CancellationToken.None);
        File.WriteAllText(output,JsonSerializer.Serialize(new{version="2.0.1",elapsedMilliseconds=timer.ElapsedMilliseconds,machine,providers=result.Providers,applications=result.Apps,vendors=vendors.Targets},StateStore.Json));
    }
    protected override void OnExit(ExitEventArgs e){activationWait?.Unregister(null);activate?.Dispose();instance?.Dispose();installerGuard?.Dispose();base.OnExit(e);}
}
