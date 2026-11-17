using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using System.Windows.Data;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
namespace UpdateDeck.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName]string? property=null)=>PropertyChanged?.Invoke(this,new(property));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? property=null){if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Changed(property);return true;}
}
public sealed class BatchCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> values)
    {
        Items.Clear();foreach(var value in values)Items.Add(value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
public sealed class ActionCommand(Func<object?,Task> action,Action<Exception> error,Func<bool>? allowed=null) : ICommand
{
    private bool active;
    public bool CanExecute(object? parameter)=>!active&&(allowed?.Invoke()??true);
    public event EventHandler? CanExecuteChanged {add{CommandManager.RequerySuggested+=value;}remove{CommandManager.RequerySuggested-=value;}}
    public async void Execute(object? parameter){if(!CanExecute(parameter))return;active=true;CommandManager.InvalidateRequerySuggested();try{await action(parameter);}catch(Exception ex){error(ex);}finally{active=false;CommandManager.InvalidateRequerySuggested();}}
}
public sealed class PageVisibility : IValueConverter
{
    public object Convert(object value,Type target,object parameter,CultureInfo culture)=>parameter.ToString()!.Split('|').Contains(value?.ToString()??"")?Visibility.Visible:Visibility.Collapsed;
    public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class BoolVisibility : IValueConverter
{
    public object Convert(object value,Type target,object parameter,CultureInfo culture)=>((value is bool b&&b)^(parameter?.ToString()=="Invert"))?Visibility.Visible:Visibility.Collapsed;
    public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class DashboardMetricsVisibility : IMultiValueConverter
{
    public object Convert(object[] values,Type type,object parameter,CultureInfo culture)=>values.Length==2&&values[0]?.ToString()=="Dashboard"&&values[1] is double height&&height>=780?Visibility.Visible:Visibility.Collapsed;
    public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class InventoryVisibility : IMultiValueConverter
{
    public object Convert(object[] values,Type type,object parameter,CultureInfo culture)=>values.Length==3&&values[0]?.ToString() is "Dashboard" or "Installed Apps" or "Updates"&&!(values[0]?.ToString()=="Updates"&&(values[1] is true||values[2] is true))?Visibility.Visible:Visibility.Collapsed;
    public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class QueueDetailsVisibility : IMultiValueConverter
{
    public object Convert(object[] values,Type type,object parameter,CultureInfo culture)=>values.Length==3&&values[0]?.ToString()=="Updates"&&values[1] is true&&values[2] is not true?Visibility.Visible:Visibility.Collapsed;
    public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class HeightVisibility : IValueConverter
{
    public object Convert(object value,Type type,object parameter,CultureInfo culture)=>value is double height&&double.TryParse(parameter?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double minimum)&&height<minimum?Visibility.Collapsed:Visibility.Visible;
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class AppRow(Software app) : Observable
{
    public Software App {get;}=app;
    private bool lastFreshness=app.HasFreshCheck;
    public bool RefreshAge(){bool fresh=App.HasFreshCheck;if(fresh==lastFreshness)return false;lastFreshness=fresh;Changed(nameof(Status));Changed(nameof(Action));Changed(nameof(CanChooseInteractive));return true;}
    private bool selected;
    public bool Selected{get=>selected;set=>Set(ref selected,value);}
    public string Name=>App.Name;
    public string Family=>SoftwareGrouping.Family(App);
    public string Publisher=>App.Publisher;
    public string Version=>App.InstalledVersion;
    public string Available=>App.AvailableVersion.Length>0?App.AvailableVersion:"—";
    public string Provider=>App.Kind==SoftwareKind.AudioPlugin?App.InstallerType+" plugin":string.IsNullOrWhiteSpace(App.Provider)||App.Provider.Equals("Unknown",StringComparison.OrdinalIgnoreCase)?"Publisher / manual":App.Provider;
    public string Monogram=>string.IsNullOrEmpty(Name)?"?":Name[..1].ToUpperInvariant();
    public string Status=>App.Pinned?"Pinned":App.State switch{UpdateState.Available when !App.HasFreshCheck=>"Recheck needed",UpdateState.Available=>App.Risk==Risk.Routine?"Update available":"Review update",UpdateState.Current=>App.HasFreshCheck?"Verified current":"Cached result",UpdateState.VendorManaged=>"Vendor managed",UpdateState.Attention=>"Needs attention",UpdateState.Ignored=>"Ignored",_=>"Cannot verify"};
    public string Action=>App.VendorId.Length>0?App.VendorActionLabel.Length>0?App.VendorActionLabel:"Open manager":App.CanUpdate?"Update":App.CanSupervise?App.EffectiveDelivery==UpdateDelivery.WinGet?"Review update":"Review installer":"Inspect";
    public bool CanChooseInteractive=>App.CanUpdate&&App.CanSupervise;
    public override string ToString()=>Name;
}
public sealed class AppRowComparer(string sort,bool descending) : System.Collections.IComparer
{
    public int Compare(object? left,object? right)=>left is AppRow a&&right is AppRow b?SoftwareOrdering.Compare(a.App,b.App,sort,descending):0;
}

public sealed class SoftwareFamilyConverter : IValueConverter
{
    public object Convert(object value,Type target,object parameter,CultureInfo culture)=>value is Software app?SoftwareGrouping.Family(app):"Other";
    public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
