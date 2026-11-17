using System.Text.RegularExpressions;

namespace UpdateDeck.Core;

/// <summary>Display groups only; never merges installation identities or update operations.</summary>
public static class SoftwareGrouping
{
    public static string Family(Software app)
    {
        var name=app.Name.Trim();var n=name.ToLowerInvariant();var package=app.PackageId.ToLowerInvariant();
        if(n.StartsWith("microsoft .net",StringComparison.Ordinal)||n.StartsWith(".net ",StringComparison.Ordinal)||n.Contains("asp.net")||n.Contains("windows desktop runtime")||package.StartsWith("microsoft.dotnet",StringComparison.Ordinal))return ".NET";
        if(n.Contains("visual c++")||package.StartsWith("microsoft.vcredist",StringComparison.Ordinal))return "Microsoft Visual C++";
        if(n.Contains("visual studio code")||package.Contains("visualstudiocode"))return "Visual Studio Code";
        if(n.Contains("visual studio")||package.StartsWith("microsoft.visualstudio",StringComparison.Ordinal))return "Visual Studio";
        var publisher=Regex.Replace(app.Publisher.Trim(),@"\s+"," ",RegexOptions.None,TimeSpan.FromMilliseconds(50));
        foreach(var brand in new[]{"Microsoft","Adobe","NVIDIA","Intel","AMD","IK Multimedia","Native Instruments","Google","Apple","Logitech","Corsair","Razer","Realtek"})
            if(publisher.Equals(brand,StringComparison.OrdinalIgnoreCase)||publisher.StartsWith(brand+" ",StringComparison.OrdinalIgnoreCase)||publisher.StartsWith(brand+",",StringComparison.OrdinalIgnoreCase))return brand;
        if(publisher.Length>0&&publisher is not ("Unknown" or "Unknown publisher" or "—"))
            return Regex.Replace(publisher,@"[,.]?\s+(?:Inc\.?|Incorporated|Corporation|Corp\.?|LLC|Ltd\.?)$","",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(50)).TrimEnd('.',',',' ');
        var family=Regex.Replace(name,@"\s+(?:v(?:ersion)?\s*)?\d.*$","",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(50)).Trim();
        return family.Length>0?family:name;
    }
}
