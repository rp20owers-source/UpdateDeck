using System.Windows;
using System.Windows.Controls;

namespace UpdateDeck.App;

public sealed class WinGetReviewWindow : Window
{
    public WinGetReviewWindow(Software app)
    {
        Title="Review interactive update";Width=700;Height=620;MinWidth=520;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new System.Windows.Media.FontFamily("Segoe UI");FontSize=13;
        SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new Grid{Margin=new Thickness(24)};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var text=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Text=$"{app.Name}\n{app.InstalledVersion} → {app.AvailableVersion}\n\n{(Versioning.MajorChange(app.InstalledVersion,app.AvailableVersion)?"This changes the major version. Check project, add-on and environment compatibility before proceeding.\n\n":"")}WinGet will request the exact catalog package and open its supported interactive installer. Review its license, destination and installation options. Keep the existing scope and architecture. Save your work and close the application first.\n\nSource\nMicrosoft community catalog (winget)\nhttps://github.com/microsoft/winget-pkgs\n\nExact package\n{app.PackageId}\n\nTarget version\n{app.AvailableVersion}\n\nScope · architecture\n{app.Scope} · {app.Architecture}\n\nExisting installation\n{app.InstallLocation}\n\nCompatibility policy\n{app.Risk} · This individual action does not change your Update All policy. Driver and firmware updates remain separate.\n\nWinGet performs its normal download, integrity, source and installer checks. Windows may request elevation. UpdateDeck does not accept required CLI agreements automatically; if WinGet requires one, review it in WinGet yourself and recheck.\n\nAfter execution, a new installed inventory must verify the target version. Cancelling the installer will not be reported as a successful update."};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        actions.Children.Add(new Button{Content="Cancel",IsCancel=true,MinWidth=90,Margin=new Thickness(0,0,12,0)});
        var start=new Button{Content="Run interactive update",MinWidth=190};start.SetResourceReference(StyleProperty,"PrimaryButton");start.Click+=(_,_)=>DialogResult=true;actions.Children.Add(start);
        Grid.SetRow(actions,1);root.Children.Add(text);root.Children.Add(actions);Content=root;
    }
}
