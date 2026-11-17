using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using Microsoft.Win32;

namespace UpdateDeck.App;

public sealed class MigrationPreviewWindow : Window
{
    private readonly IMigrationCatalog catalog;
    private readonly InventoryManifest manifest;
    private readonly ObservableCollection<MigrationRow> rows=[];
    private readonly DataGrid table;
    private readonly TextBox details;
    private readonly TextBlock status;
    private readonly Button check,cancel,save;
    private CancellationTokenSource? checking;
    private bool closed;

    public MigrationPreviewWindow(InventoryManifest manifest,IEnumerable<Software> installed,IMigrationCatalog? catalog=null)
    {
        this.manifest=manifest;this.catalog=catalog??new WinGetMigrationCatalog();
        Title="Migration preview · UpdateDeck";Width=1050;Height=730;MinWidth=700;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;MinWidth=Math.Min(MinWidth,MaxWidth);MinHeight=Math.Min(MinHeight,MaxHeight);
        SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");FontFamily=new System.Windows.Media.FontFamily("Segoe UI");FontSize=13;
        var ids=installed.Where(x=>x.PackageId.Length>0).Select(x=>x.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);int selected=0;
        foreach(var app in manifest.Applications)
        {
            bool exists=ids.Contains(app.PackageId);var row=new MigrationRow(app,exists);
            row.Selected=app.CanCheckCatalog&&!app.Ignored&&!exists&&selected<25;if(row.Selected)selected++;rows.Add(row);
        }
        var root=new Grid{Margin=new Thickness(22)};foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),new GridLength(140),GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});
        var intro=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14),Text=$"{rows.Count:N0} saved items · {manifest.SourceArchitecture} → this PC ({RuntimeInformation.OSArchitecture})\nSelect up to 25 public package IDs to check the Microsoft community catalog. This checks current-PC installer metadata; it does not transfer licenses, install software, import policy or prove driver, plug-in or project compatibility."};root.Children.Add(intro);
        var search=new TextBox{Tag="Search saved applications",Margin=new Thickness(0,0,0,12)};AutomationProperties.SetName(search,"Search saved applications");Grid.SetRow(search,1);root.Children.Add(search);
        var view=CollectionViewSource.GetDefaultView(rows);view.Filter=item=>item is MigrationRow row&&(search.Text.Length==0||$"{row.Name} {row.PackageId} {row.App.Publisher}".Contains(search.Text,StringComparison.OrdinalIgnoreCase));search.TextChanged+=(_,_)=>view.Refresh();
        table=new DataGrid{ItemsSource=view,IsReadOnly=true,RowHeight=48,SelectionMode=DataGridSelectionMode.Single};AutomationProperties.SetName(table,"Imported inventory and catalog results");
        var selector=new FrameworkElementFactory(typeof(CheckBox));selector.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(MigrationRow.Selected)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        selector.SetBinding(IsEnabledProperty,new Binding(nameof(MigrationRow.CanCheckCatalog)));selector.SetBinding(AutomationProperties.NameProperty,new Binding(nameof(MigrationRow.Name)){StringFormat="Check {0}"});selector.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);selector.SetValue(MarginProperty,new Thickness(0));
        table.Columns.Add(new DataGridTemplateColumn{Header="Check",CellTemplate=new DataTemplate{VisualTree=selector},Width=60});
        AddColumn("Application",nameof(MigrationRow.Name),220);AddColumn("Package ID",nameof(MigrationRow.PackageId),220);AddColumn("Result",nameof(MigrationRow.Label),220);AddColumn("Catalog version",nameof(MigrationRow.Version),130);
        table.SelectionChanged+=(_,_)=>ShowDetails();Grid.SetRow(table,2);root.Children.Add(table);
        details=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,12,0,0),Text="Select a saved application to inspect its scope, architecture, risk and catalog evidence."};AutomationProperties.SetName(details,"Migration item evidence");Grid.SetRow(details,3);root.Children.Add(details);
        var footer=new Grid{Margin=new Thickness(0,16,0,0)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        status=new LiveStatusTextBlock{Text="Ready · metadata preview only",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);footer.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);footer.Children.Add(actions);
        check=new Button{Content="Check selected IDs",Margin=new Thickness(0,0,8,0)};check.SetResourceReference(StyleProperty,"PrimaryButton");check.Click+=async(_,_)=>await CheckAsync();actions.Children.Add(check);
        cancel=new Button{Content="Cancel check",IsEnabled=false,Margin=new Thickness(0,0,8,0)};cancel.Click+=(_,_)=>checking?.Cancel();actions.Children.Add(cancel);
        save=new Button{Content="Save preview",Margin=new Thickness(0,0,8,0)};save.Click+=(_,_)=>Save();actions.Children.Add(save);
        var close=new Button{Content="Close",IsCancel=true};close.Click+=(_,_)=>Close();actions.Children.Add(close);
        Grid.SetRow(footer,4);root.Children.Add(footer);Content=root;
        Closed+=(_,_)=>{closed=true;checking?.Cancel();};
    }
    private void AddColumn(string header,string property,double width)=>table.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(property),IsReadOnly=true,Width=width});
    private void ShowDetails()
    {
        if(table.SelectedItem is not MigrationRow row)return;var app=row.App;var result=row.Result;
        details.Text=$"{app.Name} · {app.Publisher}\nSaved: {app.InstalledVersion} · {app.Scope} · {app.Architecture} · {app.Provider} · {app.Risk}{(app.Ignored?" · ignored in saved policy":"")}\n{(row.AlreadyInstalled?"This package ID is already recognized locally; this alone does not verify scope or version.\n":"")}{(result==null?row.Label:result.Message+"\nCatalog: "+result.Version+" · "+result.Architecture+" · "+result.Scope+" · "+result.InstallerType+"\nSource refresh: "+(result.CatalogUpdatedAt?.ToLocalTime().ToString("g")??"Unverified"))}";
    }
    private async Task CheckAsync()
    {
        if(checking!=null)return;table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);
        var selected=rows.Where(x=>x.Selected).ToArray();
        if(selected.Length is 0 or >25){status.Text="Select between 1 and 25 saved applications.";return;}
        if(selected.Any(x=>!x.App.CanCheckCatalog)){status.Text="Some selected items require their publisher or established manager. Select only valid WinGet package IDs.";return;}
        checking=new();var token=checking.Token;check.IsEnabled=save.IsEnabled=table.IsEnabled=false;cancel.IsEnabled=true;int completed=0;
        try
        {
            foreach(var row in selected)
            {
                token.ThrowIfCancellationRequested();status.Text=$"Checking {completed+1} of {selected.Length} · {row.Name}";
                var result=await catalog.ResolveAsync(row.PackageId,row.App.Scope,token);if(closed)return;
                row.Result=result;completed++;ShowDetails();if(result.StopBatch){status.Text=$"Stopped after {completed} checks. Inspect the selected result and try later.";return;}
            }
            status.Text=$"Checked {completed} package IDs · no installations performed";
        }
        catch(OperationCanceledException){if(!closed)status.Text=$"Cancelled after {completed} checks. Completed results remain visible.";}
        catch(Exception ex){if(!closed)status.Text=$"Catalog check stopped ({ex.GetType().Name}). No installer was started.";}
        finally{checking.Dispose();checking=null;if(!closed){check.IsEnabled=save.IsEnabled=table.IsEnabled=true;cancel.IsEnabled=false;}}
    }
    private void Save()
    {
        try
        {
            var dialog=new SaveFileDialog{Filter="Migration preview (*.json)|*.json",FileName="UpdateDeck-migration-preview.json"};if(dialog.ShowDialog(this)!=true)return;
            File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(new{schema=1,product="UpdateDeck migration preview",checkedAt=DateTimeOffset.UtcNow,sourceArchitecture=manifest.SourceArchitecture,targetArchitecture=RuntimeInformation.OSArchitecture.ToString(),metadataOnly=true,applications=rows.Select(row=>new{saved=row.App,recognizedLocally=row.AlreadyInstalled,result=row.Result})},StateStore.Json));status.Text="Preview saved · no policy or installation changes";
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text="The preview could not be saved. Choose a writable location and retry.";}
    }
}

public sealed class MigrationRow(ManifestApplication app,bool alreadyInstalled) : Observable
{
    public ManifestApplication App {get;}=app;
    public bool AlreadyInstalled {get;}=alreadyInstalled;
    public string Name=>App.Name;
    public string PackageId=>App.PackageId;
    public bool CanCheckCatalog=>App.CanCheckCatalog;
    public override string ToString()=>Name;
    private bool selected;
    public bool Selected{get=>selected;set=>Set(ref selected,value);}
    private CatalogPreview? result;
    public CatalogPreview? Result{get=>result;set{if(Set(ref result,value)){Changed(nameof(Label));Changed(nameof(Version));}}}
    public string Version=>Result?.Version??"—";
    public string Label=>Result?.State switch{CatalogPreviewState.Applicable=>App.Risk is Risk.High or Risk.SystemSensitive?"Metadata found · sensitive software":"Applicable metadata found",CatalogPreviewState.NotFound=>"Exact ID not found",CatalogPreviewState.NoApplicableInstaller=>"No applicable installer",CatalogPreviewState.NeedsReview=>"Needs review",CatalogPreviewState.Unavailable=>"Catalog unavailable",_=>!App.CanCheckCatalog?"Publisher / manager workflow":AlreadyInstalled?"Recognized locally · not checked":"Not checked"};
}
