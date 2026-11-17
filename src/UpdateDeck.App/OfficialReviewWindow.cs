using System.Windows;
using System.Windows.Controls;

namespace UpdateDeck.App;

public sealed class OfficialReviewWindow : Window
{
    public OfficialReviewWindow(Software app,OfficialRelease release)
    {
        Title="Review official installer";Width=720;Height=640;MinWidth=520;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new System.Windows.Media.FontFamily("Segoe UI");FontSize=13;
        SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new Grid{Margin=new Thickness(24)};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var text=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Padding=new Thickness(8),Text=$"{app.Name}\n{app.InstalledVersion} → {release.Version}\n\nThe publisher's installer will open interactively. Review its license, installation options and any Windows elevation request. Keep the existing installation folder and architecture.\n\nSource\n{release.ReleasePage}\n\nDownload\n{release.Download}\n\nExpected publisher\nNOTEPAD++ · Windows Authenticode\n\nArchitecture · scope\n{release.Architecture} · {app.Scope}\n\nExisting installation\n{app.InstallLocation}\n\nDownload size\n{release.Size/1048576.0:F1} MB\n\nExpected SHA-256\n{release.Sha256}\n\nUpdateDeck checks the download length, SHA-256, signature, publisher and installer version before opening it. After it ends, a fresh read of the signed executable at the original path must verify the target version. Cancelling an installer will not be reported as an update."};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        var cancel=new Button{Content="Cancel",IsCancel=true,MinWidth=90,Margin=new Thickness(0,0,12,0)};
        var launch=new Button{Content="Download and open installer",MinWidth=210};launch.SetResourceReference(StyleProperty,"PrimaryButton");launch.Click+=(_,_)=>DialogResult=true;
        actions.Children.Add(cancel);actions.Children.Add(launch);Grid.SetRow(actions,1);root.Children.Add(text);root.Children.Add(actions);Content=root;
    }
}
