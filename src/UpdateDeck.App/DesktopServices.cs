using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace UpdateDeck.App;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        bool dark=theme!="Light";
        if(theme=="System") {try{using var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");dark=Convert.ToInt32(k?.GetValue("AppsUseLightTheme",0))==0;}catch{}}
        var colors=dark?new[]{"#11151C","#161B24","#1A202B","#252F40","#303A49","#F0F3F9","#ADB8C9","#7EAEFF","#2867CF","#80D9B0"}:new[]{"#F5F6F8","#EEF0F4","#FFFFFF","#E6EDFA","#D1D7E2","#18212F","#536176","#235FBF","#245FCA","#17633E"};
        var keys=new[]{"BackgroundBrush","SidebarBrush","SurfaceBrush","HoverBrush","BorderBrush","TextBrush","MutedBrush","AccentBrush","PrimaryBrush","SuccessBrush"};
        for(int i=0;i<keys.Length;i++)Application.Current.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Application.Current.Resources["PrimaryTextBrush"]=Brushes.White;
        if(SystemParameters.HighContrast)
        {
            foreach(var key in new[]{"BackgroundBrush","SidebarBrush","SurfaceBrush"})Application.Current.Resources[key]=SystemColors.WindowBrush;
            foreach(var key in new[]{"TextBrush","MutedBrush","SuccessBrush"})Application.Current.Resources[key]=SystemColors.WindowTextBrush;
            foreach(var key in new[]{"PrimaryBrush","AccentBrush"})Application.Current.Resources[key]=SystemColors.HighlightBrush;
            Application.Current.Resources["HoverBrush"]=SystemColors.ControlBrush;
            Application.Current.Resources["BorderBrush"]=SystemColors.WindowTextBrush;
            Application.Current.Resources["PrimaryTextBrush"]=SystemColors.HighlightTextBrush;
        }
        if(Application.Current.MainWindow is Window window)
        {
            try{int enabled=dark?1:0;DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,20,ref enabled,4);}catch(DllNotFoundException){}
        }
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
public sealed class NavGlyph : IValueConverter
{
    public object Convert(object value,Type type,object parameter,CultureInfo culture)=>value?.ToString() switch{"Dashboard"=>"\uE80F","Updates"=>"\uE895","Installed Apps"=>"\uE8B7","Games"=>"\uE7FC","Vendor Updaters"=>"\uE8A7","Drivers"=>"\uE7F4","History"=>"\uE81C","This PC"=>"\uE7F4",_=>"\uE713"};
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class IconConverter : IValueConverter
{
    private static readonly Dictionary<string,BitmapSource?> cache=new(StringComparer.OrdinalIgnoreCase);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern uint ExtractIconEx(string file,int index,IntPtr[]? large,IntPtr[] small,uint count);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr handle);
    public object? Convert(object value,Type type,object parameter,CultureInfo culture)
    {
        if(value is not string raw||raw.Length==0)return null;if(cache.TryGetValue(raw,out var cached))return cached;
        BitmapSource? bitmap=null;
        try
        {
            string path=raw.Trim();int index=0;var comma=path.LastIndexOf(',');if(comma>0&&int.TryParse(path[(comma+1)..],out index))path=path[..comma];
            path=Environment.ExpandEnvironmentVariables(path.Trim('"'));
            if(!path.StartsWith("\\\\",StringComparison.Ordinal)&&File.Exists(path)&&Path.IsPathFullyQualified(path))
            {
                var icons=new IntPtr[1];if(ExtractIconEx(path,index,null,icons,1)>0&&icons[0]!=IntPtr.Zero){try{bitmap=Imaging.CreateBitmapSourceFromHIcon(icons[0],Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(32,32));bitmap.Freeze();}finally{DestroyIcon(icons[0]);}}
            }
        }catch{}
        if(cache.Count>1000)cache.Clear();cache[raw]=bitmap;return bitmap;
    }
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class PreviewWindow : Window
{
    public PreviewWindow(string title,string text)
    {
        Title=title;Width=780;Height=620;MinWidth=500;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        Content=new System.Windows.Controls.ScrollViewer{Margin=new Thickness(24),VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,Content=new System.Windows.Controls.TextBlock{Text=text,TextWrapping=TextWrapping.Wrap}};
    }
}
