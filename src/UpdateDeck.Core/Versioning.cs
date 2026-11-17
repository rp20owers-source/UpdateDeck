using System.Numerics;
using System.Text.RegularExpressions;

namespace UpdateDeck.Core;

/// <summary>Conservative numeric / semantic ordering. Unknown syntax never implies an upgrade.</summary>
public static partial class Versioning
{
    [GeneratedRegex(@"\A[vV]?([0-9]+(?:\.[0-9]+)*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z")]
    private static partial Regex Syntax();
    public static int? Compare(string? left, string? right)
    {
        var a = Parse(left); var b = Parse(right);
        if (a == null || b == null) return null;
        for (int i=0; i<Math.Max(a.Value.Numbers.Length,b.Value.Numbers.Length); i++)
        {
            var x = i<a.Value.Numbers.Length ? a.Value.Numbers[i] : BigInteger.Zero;
            var y = i<b.Value.Numbers.Length ? b.Value.Numbers[i] : BigInteger.Zero;
            if (x != y) return x.CompareTo(y);
        }
        var ap = a.Value.Pre; var bp = b.Value.Pre;
        if (ap == bp) return 0;
        if (ap.Length == 0) return 1;
        if (bp.Length == 0) return -1;
        var aa = ap.Split('.'); var bb = bp.Split('.');
        for(int i=0;i<Math.Min(aa.Length,bb.Length);i++)
        {
            bool an = BigInteger.TryParse(aa[i], out var x), bn = BigInteger.TryParse(bb[i], out var y);
            int c = an && bn ? x.CompareTo(y) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(aa[i],bb[i]);
            if(c!=0) return Math.Sign(c);
        }
        return aa.Length.CompareTo(bb.Length);
    }
    public static bool Known(string value) => Parse(value) != null;
    public static bool IsPrerelease(string value) => Parse(value)?.Pre.Length > 0;
    public static bool SameReleaseTrack(string installed,string target)
    {
        var a=Parse(installed);var b=Parse(target);if(a==null||b==null)return false;
        if(a.Value.Pre.Length==0||b.Value.Pre.Length==0)return a.Value.Pre.Length==b.Value.Pre.Length;
        static string Track(string value)=>new string(value.TakeWhile(c=>c!='.'&&!char.IsAsciiDigit(c)).ToArray()).TrimEnd('-');
        string x=Track(a.Value.Pre),y=Track(b.Value.Pre);return x.Length>0&&y.Length>0&&x.Equals(y,StringComparison.OrdinalIgnoreCase);
    }
    public static bool MajorChange(string a,string b)
    {
        var x=Parse(a); var y=Parse(b);
        return x!=null && y!=null && x.Value.Numbers[0]!=y.Value.Numbers[0];
    }
    private static (BigInteger[] Numbers, string Pre)? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 256) return null;
        var m=Syntax().Match(input.Trim());
        if(!m.Success) return null;
        return (m.Groups[1].Value.Split('.').Select(x=>BigInteger.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray(),m.Groups[2].Value);
    }
}
