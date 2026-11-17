using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UpdateDeck.Core;

public sealed record ManifestPreferences(string Theme,string Schedule,int QuietStart,int QuietEnd,bool AllowMeteredChecks);
public sealed record ManifestApplication
{
    public string Name {get;init;}="";
    public string Publisher {get;init;}="";
    public string InstalledVersion {get;init;}="Unknown";
    public string PackageId {get;init;}="";
    public string Provider {get;init;}="";
    public string Scope {get;init;}="Unknown";
    public string Architecture {get;init;}="Unknown";
    public string Channel {get;init;}="";
    public SoftwareKind Kind {get;init;}
    public Risk Risk {get;init;}
    public bool Ignored {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanCheckCatalog=>Kind==SoftwareKind.Application&&(Provider is "WinGet" or "Official installer")&&PackageIdentifiers.IsWinGet(PackageId);
}
public static partial class PackageIdentifiers
{
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+\-]{0,255}\z",RegexOptions.CultureInvariant)]private static partial Regex WinGet();
    public static bool IsWinGet(string? value)=>value!=null&&WinGet().IsMatch(value);
}
public sealed record InventoryManifest
{
    public int Schema {get;init;}=2;
    public string Product {get;init;}="UpdateDeck";
    public DateTimeOffset ExportedAt {get;init;}=DateTimeOffset.UtcNow;
    public string SourceArchitecture {get;init;}="Unknown";
    public ManifestPreferences? Preferences {get;init;}
    public IReadOnlyList<ManifestApplication> Applications {get;init;}=[];
    private static readonly JsonSerializerOptions Format=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    public static InventoryManifest Capture(IEnumerable<Software> applications,Preferences preferences,string architecture)=>new()
    {
        SourceArchitecture=architecture,
        Preferences=new(preferences.Theme,preferences.Schedule,preferences.QuietStart,preferences.QuietEnd,preferences.AllowMeteredChecks),
        Applications=applications.Select(app=>new ManifestApplication{Name=app.Name,Publisher=app.Publisher,InstalledVersion=app.InstalledVersion,PackageId=app.PackageId,Provider=app.Provider,Scope=app.Scope,Architecture=app.Architecture,Channel=app.InstalledChannel,Kind=app.Kind,Risk=UpdatePolicy.Classify(app),Ignored=preferences.Ignored.Contains(app.Id)}).ToArray()
    };
    public string ToJson()=>JsonSerializer.Serialize(this,Format);
    public static InventoryManifest Parse(string json)
    {
        if(Encoding.UTF8.GetByteCount(json)>5*1024*1024)throw new InvalidDataException("Manifest exceeds the 5 MB limit.");
        using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=16});var root=document.RootElement;UniqueObject(root);
        int schema=Number(root,"schema",0);if(schema is not (1 or 2))throw new InvalidDataException("Unsupported manifest schema.");
        if(schema==2&&Text(root,"product",64)!="UpdateDeck")throw new InvalidDataException("This is not an UpdateDeck inventory manifest.");
        if(!root.TryGetProperty("applications",out var array)||array.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Manifest applications must be an array.");
        if(array.GetArrayLength()>5000)throw new InvalidDataException("A manifest can contain up to 5,000 items. Export a smaller selection.");
        var applications=new List<ManifestApplication>();
        foreach(var item in array.EnumerateArray())
        {
            UniqueObject(item);string name=Text(item,"name",4096);if(string.IsNullOrWhiteSpace(name))throw new InvalidDataException("Every manifest item needs an application name.");
            var kind=EnumValue(item,"kind",SoftwareKind.Application);var risk=EnumValue(item,"risk",Risk.Routine);
            var app=new ManifestApplication{Name=name,Publisher=Text(item,"publisher",4096),InstalledVersion=Text(item,"installedVersion",4096,"Unknown"),PackageId=Text(item,"packageId",512),Provider=Text(item,"provider",256),Scope=Text(item,"scope",64,"Unknown"),Architecture=Text(item,"architecture",64,"Unknown"),Channel=Text(item,"channel",256),Kind=kind,Risk=risk,Ignored=Boolean(item,"ignored",false)};
            risk=UpdatePolicy.Classify(new Software{Name=app.Name,Publisher=app.Publisher,PackageId=app.PackageId,Kind=app.Kind,Risk=app.Risk});applications.Add(app with{Risk=risk});
        }
        ManifestPreferences? preferences=null;
        if(root.TryGetProperty("preferences",out var saved)&&saved.ValueKind!=JsonValueKind.Null)
        {
            UniqueObject(saved);string theme=Text(saved,"theme",32,"Dark"),schedule=Text(saved,"schedule",32,"Manual");int start=Number(saved,"quietStart",22),end=Number(saved,"quietEnd",7);
            if(theme is not ("Dark" or "Light" or "System")||schedule is not ("Manual" or "Daily" or "Weekly")||start is <0 or >23||end is <0 or >23)throw new InvalidDataException("Saved preferences contain unsupported values.");
            preferences=new(theme,schedule,start,end,Boolean(saved,"allowMeteredChecks",false));
        }
        DateTimeOffset exported=DateTimeOffset.MinValue;
        if(root.TryGetProperty("exportedAt",out var timestamp)&&(timestamp.ValueKind!=JsonValueKind.String||!timestamp.TryGetDateTimeOffset(out exported)))throw new InvalidDataException("Invalid export timestamp.");
        return new(){Schema=schema,ExportedAt=exported,SourceArchitecture=Text(root,"sourceArchitecture",64,Text(root,"architecture",64,"Unknown")),Preferences=preferences,Applications=applications};
    }
    private static void UniqueObject(JsonElement element)
    {
        if(element.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Manifest entries must be objects.");
        var names=new HashSet<string>(StringComparer.Ordinal);foreach(var property in element.EnumerateObject())if(!names.Add(property.Name))throw new InvalidDataException("Duplicate manifest properties are ambiguous and are not accepted.");
    }
    private static string Text(JsonElement item,string name,int maximum,string fallback="")
    {
        if(!item.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return fallback;
        if(value.ValueKind!=JsonValueKind.String)throw new InvalidDataException($"Manifest {name} must be text.");string text=value.GetString()!;
        if(text.Length>maximum||text.IndexOf('\0')>=0)throw new InvalidDataException($"Manifest {name} exceeds its supported text limit.");return text;
    }
    private static int Number(JsonElement item,string name,int fallback)
    {if(!item.TryGetProperty(name,out var value))return fallback;if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int number))throw new InvalidDataException($"Manifest {name} must be a whole number.");return number;}
    private static bool Boolean(JsonElement item,string name,bool fallback)
    {if(!item.TryGetProperty(name,out var value))return fallback;return value.ValueKind switch{JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new InvalidDataException($"Manifest {name} must be true or false.")};}
    private static T EnumValue<T>(JsonElement item,string name,T fallback)where T:struct,Enum
    {if(!item.TryGetProperty(name,out var value))return fallback;if(value.ValueKind==JsonValueKind.String&&Enum.TryParse<T>(value.GetString(),true,out var parsed)&&Enum.IsDefined(parsed))return parsed;throw new InvalidDataException($"Manifest {name} is unsupported.");}
}

public enum CatalogPreviewState { NotChecked,Applicable,Unavailable,NotFound,NoApplicableInstaller,NeedsReview }
public sealed record CatalogPreview(string PackageId,CatalogPreviewState State,string Version="",string Architecture="",string Scope="",string Publisher="",string InstallerType="",string Message="",DateTimeOffset? CatalogUpdatedAt=null,bool StopBatch=false);
public interface IMigrationCatalog
{
    Task<CatalogPreview> ResolveAsync(string packageId,string preferredScope,CancellationToken cancellation);
}
