using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

/// <summary>
/// Conservative adapter for the one WinGet feature without a structured COM contract.
/// It recognizes the official headers and searches complete, known identity tokens;
/// it never infers an ID from whitespace column positions or version-looking text.
/// </summary>
public static partial class WinGetPins
{
    private sealed record Locale(string LocaleName,string NoPins,string[] Headers);
    private static readonly (HashSet<string> EmptyMessages,Regex[] Headers) Resources=LoadResources();
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]private static partial Regex Ansi();
    private static (HashSet<string>,Regex[]) LoadResources()
    {
        using var resource=typeof(WinGetPins).Assembly.GetManifestResourceStream("UpdateDeck.Windows.Resources.WinGetPinLocales.json")??throw new InvalidOperationException("Pin localization resources are missing.");
        using var json=JsonDocument.Parse(resource);
        var messages=new HashSet<string>(StringComparer.Ordinal);var patterns=new List<Regex>();
        foreach(var locale in json.RootElement.EnumerateArray())
        {
            var message=locale.GetProperty("noPins").GetString();if(!string.IsNullOrWhiteSpace(message))messages.Add(message.Trim());
            var headers=locale.GetProperty("headers").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
            if(headers.Length==6&&headers.All(x=>x.Length>0))patterns.Add(new Regex(@"^\s*"+string.Join(@"\s+",headers.Select(Regex.Escape))+@"\s*$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)));
        }
        return(messages,patterns.ToArray());
    }
    public static string[] Keys(Software app)=>new[]{app.PackageId,app.ProviderInstanceId}.Concat(app.ProductCodes).Where(x=>!string.IsNullOrWhiteSpace(x)&&x.Length<=1024).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool IsPinned(Software app,IReadOnlySet<string> keys)=>Keys(app).Any(keys.Contains);
    public static (bool Verified,HashSet<string> Ids) Parse(ProcessResult result,IEnumerable<string>? knownKeys=null)
    {
        var pinned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(result.ExitCode!=0||result.TimedOut||result.WasCancelled||result.OutputIncomplete||result.OutputTruncated||result.Output.Length>32768||!string.IsNullOrWhiteSpace(result.Error))return(false,pinned);
        var lines=Ansi().Replace(result.Output,"").Replace("\r","").Split('\n').Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.TrimEnd()).ToArray();
        // A package name cannot masquerade as the no-pin result inside a table or warning.
        if(lines.Length==1&&Resources.EmptyMessages.Contains(lines[0].Trim()))return(true,pinned);
        int separator=Array.FindIndex(lines,x=>x.Trim().Length>=8&&x.Trim().All(c=>c=='-'));
        if(separator<1||!Resources.Headers.Any(pattern=>pattern.IsMatch(lines[separator-1])))return(false,pinned);
        var keys=(knownKeys??[]).Where(x=>!string.IsNullOrWhiteSpace(x)&&x.Length<=1024).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(keys.Length==0)return(false,pinned);
        int rows=0;
        for(int i=separator+1;i<lines.Length;i++)
        {
            var line=lines[i];
            if(line.Contains('…')||line.Contains("...")||line.IndexOf('\0')>=0)return(false,new(StringComparer.OrdinalIgnoreCase));
            var matches=keys.Where(key=>ContainsToken(line,key)).ToArray();
            if(matches.Length==0)return(false,new(StringComparer.OrdinalIgnoreCase));
            foreach(var match in matches)pinned.Add(match);
            rows++;
        }
        return(rows>0,pinned);
    }
    private static bool ContainsToken(string line,string token)
    {
        int position=0;
        while(position<=line.Length-token.Length)
        {
            int found=line.IndexOf(token,position,StringComparison.OrdinalIgnoreCase);if(found<0)return false;
            int end=found+token.Length;
            if((found==0||!IdentityCharacter(line[found-1]))&&(end==line.Length||!IdentityCharacter(line[end])))return true;
            position=found+1;
        }
        return false;
    }
    private static bool IdentityCharacter(char c)=>char.IsLetterOrDigit(c)||c is '.' or '_' or '+' or '-';
}
