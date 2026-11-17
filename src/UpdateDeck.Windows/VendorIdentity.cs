using System.Text.RegularExpressions;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public static class VendorIdentity
{
    private static string PublisherKey(string value)
    {
        var key=new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        foreach(var suffix in new[]{"INCORPORATED","CORPORATION","LIMITED","GMBH","CORP","INC","LLC","LTD","SRL","NV","BV"})
            if(key.EndsWith(suffix,StringComparison.Ordinal)&&key.Length>suffix.Length){key=key[..^suffix.Length];break;}
        return key;
    }
    public static bool Matches(Software app,VendorDefinition vendor)
    {
        if(app.Name.Length is 0 or >4096||app.Publisher.Length is 0 or >4096)return false;
        var publisher=PublisherKey(app.Publisher);
        if(!vendor.Publishers.Any(expected=>publisher.Equals(PublisherKey(expected),StringComparison.Ordinal)))return false;
        return vendor.Products.Any(product=>Regex.IsMatch(app.Name,@"(?<![\p{L}\p{Nd}])"+Regex.Escape(product)+@"(?!\p{L})",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)));
    }
}
