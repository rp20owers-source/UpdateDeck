using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;

namespace UpdateDeck.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private NotifyIcon? tray;
    private bool exiting;
    private bool themeRefreshPending;
    private StoredWindowBounds? normalBounds;
    private bool restoreMaximized;
    public MainWindow(MainViewModel viewModel,bool compactTest=false)
    {
        InitializeComponent();model=viewModel;DataContext=model;
        CommandBindings.Add(new(System.Windows.Input.ApplicationCommands.Find,(_,e)=>
        {
            if(model.Page=="History"){HistorySearchBox.Focus();HistorySearchBox.SelectAll();}
            else if(model.Page=="Games"){GamesSearchBox.Focus();GamesSearchBox.SelectAll();}
            else{model.Page="Installed Apps";model.QueueDetailsExpanded=false;SearchBox.Focus();SearchBox.SelectAll();}
            e.Handled=true;
        }));
        WindowPlacement.ConstrainMinimums(this);
        Width=Math.Min(model.Preferences.WindowWidth,SystemParameters.WorkArea.Width);Height=Math.Min(model.Preferences.WindowHeight,SystemParameters.WorkArea.Height);
        normalBounds=model.Preferences.WindowBounds;restoreMaximized=!compactTest&&model.Preferences.WindowMaximized;
        if(!compactTest&&normalBounds!=null)WindowStartupLocation=WindowStartupLocation.Manual;
        if(compactTest){Width=1000;Height=700;}
        SourceInitialized+=(_,_)=>{WindowPlacement.ConstrainMinimums(this);if(!compactTest)WindowPlacement.Restore(this,normalBounds);};
        DpiChanged+=(_,_)=>WindowPlacement.ConstrainMinimums(this);
        ApplicationsGrid.AddHandler(System.Windows.Controls.GridViewColumnHeader.ClickEvent,new RoutedEventHandler((_,e)=>{if(e.Source is System.Windows.Controls.GridViewColumnHeader header&&header.Tag is string path&&path.Length>0){model.SortByColumn(path);e.Handled=true;}}));
        Loaded+=async(_,_)=>{normalBounds=WindowPlacement.Capture(this);if(restoreMaximized)WindowState=WindowState.Maximized;ThemeManager.Apply(model.Preferences.Theme);ConfigureTray();await model.InitializeAsync();};
        LocationChanged+=(_,_)=>{if(IsLoaded){WindowPlacement.ConstrainMinimums(this);if(WindowState==WindowState.Normal)normalBounds=WindowPlacement.Capture(this);}};
        StateChanged+=(_,_)=>{if(WindowState!=WindowState.Minimized)restoreMaximized=WindowState==WindowState.Maximized;};
        SizeChanged+=(_,_)=>{bool compact=ActualWidth<1150;SideColumn.Width=new GridLength(compact?72:212);Tag=compact?"Compact":"Full";BrandText.Visibility=BrandSub.Visibility=SideFooter.Visibility=compact?Visibility.Collapsed:Visibility.Visible;if(IsLoaded&&WindowState==WindowState.Normal)normalBounds=WindowPlacement.Capture(this);};
        model.SettingsSaved+=ConfigureTray;
        model.Notification+=message=>{if(model.Preferences.Notifications&&tray!=null)tray.ShowBalloonTip(7000,"UpdateDeck",message,ToolTipIcon.Info);};
        Closing+=OnClosing;
        Closed+=(_,_)=>{tray?.Dispose();model.Stop();};
        SystemParameters.StaticPropertyChanged+=OnSystemPreference;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnUserPreference;
        Closed+=(_,_)=>{SystemParameters.StaticPropertyChanged-=OnSystemPreference;Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnUserPreference;};
    }
    private void OnSystemPreference(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(SystemParameters.HighContrast))QueueThemeRefresh();
        if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation))Dispatcher.BeginInvoke(model.RefreshMotionPreferences);
    }
    private void OnUserPreference(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if(e.Category is Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle||e.Category==Microsoft.Win32.UserPreferenceCategory.General&&model.Preferences.Theme=="System")QueueThemeRefresh();
    }
    private void QueueThemeRefresh()=>Dispatcher.BeginInvoke(()=>
    {
        if(themeRefreshPending||Dispatcher.HasShutdownStarted)return;themeRefreshPending=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,()=>{themeRefreshPending=false;if(!Dispatcher.HasShutdownStarted)ThemeManager.Apply(model.Preferences.Theme);});
    });
    private void ConfigureTray()
    {
        if(!model.Preferences.Tray){tray?.Dispose();tray=null;return;}
        if(tray!=null)return;
        using var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/UpdateDeck.ico"))!.Stream;
        tray=new NotifyIcon{Text="UpdateDeck",Icon=new System.Drawing.Icon(stream),Visible=true};
        var menu=new ContextMenuStrip();
        void Add(string title,Action action)=>menu.Items.Add(title,null,(_,_)=>Dispatcher.Invoke(action));
        Add("Open UpdateDeck",ShowWindow);Add("Scan now",()=>{ShowWindow();model.ScanCommand.Execute(null);});
        Add("Available updates",()=>{ShowWindow();model.Page="Updates";});Add("Smart Update All",()=>{ShowWindow();model.UpdateAllCommand.Execute(null);});
        Add("Current operation",()=>{ShowWindow();model.Page="Updates";});Add("Settings",()=>{ShowWindow();model.Page="Settings";});
        menu.Items.Add(new ToolStripSeparator());Add("Exit",()=>{if(model.QueueRunning){ShowWindow();model.Banner="An installer is active. Cancel pending work and let the active operation finish before exiting.";return;}exiting=true;Close();});
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowWindow);
    }
    public void ShowWindow(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(model.QueueRunning){e.Cancel=true;if(model.Preferences.Tray&&!exiting)Hide();else model.Banner="The active installer will finish safely. Cancel pending work to stop the rest of the queue.";return;}
        var restore=RestoreBounds;model.SaveWindow(WindowState==WindowState.Normal?ActualWidth:restore.Width,WindowState==WindowState.Normal?ActualHeight:restore.Height,normalBounds,restoreMaximized);
        if(model.Preferences.Tray&&!exiting){e.Cancel=true;Hide();}
    }
}
