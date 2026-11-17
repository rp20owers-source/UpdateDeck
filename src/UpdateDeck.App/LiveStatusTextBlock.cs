using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UpdateDeck.App;

/// <summary>Polite, coalesced status announcements, with no idle timer when nobody is listening.</summary>
public sealed class LiveStatusTextBlock : TextBlock
{
    private DispatcherTimer? announcement;
    private string lastAnnounced="";
    static LiveStatusTextBlock()=>TextProperty.OverrideMetadata(typeof(LiveStatusTextBlock),new FrameworkPropertyMetadata(string.Empty,(sender,_)=>((LiveStatusTextBlock)sender).TextChanged()));
    public LiveStatusTextBlock()
    {
        AutomationProperties.SetLiveSetting(this,AutomationLiveSetting.Polite);
        Unloaded+=(_,_)=>announcement?.Stop();
    }
    private void TextChanged()
    {
        if(!IsLoaded||string.IsNullOrWhiteSpace(Text)||!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))return;
        if(announcement==null)
        {
            announcement=new DispatcherTimer(DispatcherPriority.Background,Dispatcher){Interval=TimeSpan.FromMilliseconds(450)};
            announcement.Tick+=(_,_)=>
            {
                announcement.Stop();if(!IsLoaded||!IsVisible||Text==lastAnnounced||string.IsNullOrWhiteSpace(Text))return;
                lastAnnounced=Text;
                var peer=FrameworkElementAutomationPeer.FromElement(this)??FrameworkElementAutomationPeer.CreatePeerForElement(this);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            };
        }
        if(!announcement.IsEnabled)announcement.Start();
    }
}
