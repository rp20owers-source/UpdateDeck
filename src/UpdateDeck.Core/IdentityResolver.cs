namespace UpdateDeck.Core;

public static class IdentityResolver
{
    private static bool Eq(string a,string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && a.Equals(b,StringComparison.OrdinalIgnoreCase);
    public static int Confidence(Software a,Software b)
    {
        if(a.Kind!=b.Kind)return 0;
        if (a.Id==b.Id) return 100;
        if(a.Kind==SoftwareKind.AudioPlugin&&!Eq(a.PrimaryExecutable,b.PrimaryExecutable))return 0;
        if(a.InstalledChannel.Length>0&&b.InstalledChannel.Length>0&&!Eq(a.InstalledChannel,b.InstalledChannel))return 0;
        if(a.Ambiguous||b.Ambiguous)return 0;
        if (!Eq(a.Scope,"Unknown") && !Eq(b.Scope,"Unknown") && !Eq(a.Scope,b.Scope)) return 0;
        if (!Eq(a.Architecture,"Unknown") && !Eq(b.Architecture,"Unknown") && !Eq(a.Architecture,b.Architecture)) return 0;
        // Preserve installation instances and channels, even when the product code was reused.
        bool crossRegistration=a.Provider=="WinGet"&&b.Provider=="Registry"||b.Provider=="WinGet"&&a.Provider=="Registry";
        bool sameRegistration=crossRegistration&&a.ProductCodes.Intersect(b.ProductCodes,StringComparer.OrdinalIgnoreCase).Any();
        if (!sameRegistration&&Versioning.Compare(a.InstalledVersion,b.InstalledVersion) is int c && c!=0) return 0;
        if(!sameRegistration&&Versioning.Compare(a.InstalledVersion,b.InstalledVersion)==null&&!Eq(a.InstalledVersion,b.InstalledVersion))return 0;
        if (a.InstallLocation.Length>0 && b.InstallLocation.Length>0 && !Eq(Normalize(a.InstallLocation),Normalize(b.InstallLocation))) return 0;
        if(a.ProductCodes.Intersect(b.ProductCodes,StringComparer.OrdinalIgnoreCase).Any()) return 98;
        if(Eq(a.FamilyName,b.FamilyName)) return 99;
        if(Eq(a.PackageId,b.PackageId) && Eq(a.Scope,b.Scope) && Eq(a.Architecture,b.Architecture)) return 95;
        if(Eq(Normalize(a.InstallLocation),Normalize(b.InstallLocation)) && Eq(a.Publisher,b.Publisher) && Eq(a.Name,b.Name)) return 94;
        return 0;
    }
    public static IReadOnlyList<Software> Resolve(IEnumerable<Software> records)
    {
        var input=records.Select(SoftwareValidation.Normalize).Select(x=>x.InstalledVersionEvidence.Length==0?x with{InstalledVersionEvidence=[$"{x.Provider}: {x.InstalledVersion}"]}:x).ToArray();
        var collisions=input.Where(x=>x.Provider=="WinGet").SelectMany(x=>x.ProductCodes.Select(code=>(code,app:x))).GroupBy(x=>x.code,StringComparer.OrdinalIgnoreCase).Where(g=>g.Select(x=>x.app.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1).SelectMany(g=>g.Select(x=>x.app.Id)).ToHashSet();
        input=input.Select(x=>collisions.Contains(x.Id)?x with{Ambiguous=true,State=UpdateState.Attention,Explanation="Several package identities reuse this product code. Inspect the installation before updating."}:x).ToArray();
        var result=new List<Software>();
        // Every confidence score >=94 requires one of these strong shared identifiers.
        // Indexing only selects candidates; the complete conservative comparison still decides.
        var index=new Dictionary<string,HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        void Register(int position,Software app)
        {
            foreach(string key in CandidateKeys(app))
            {if(!index.TryGetValue(key,out var positions))index[key]=positions=[];positions.Add(position);}
        }
        foreach(var record in input.OrderBy(x=>x.Provider=="Registry"?0:1).ThenBy(x=>x.Id,StringComparer.Ordinal))
        {
            var positions=new HashSet<int>();foreach(string key in CandidateKeys(record))if(index.TryGetValue(key,out var found))positions.UnionWith(found);
            var matches=positions.Order().Select(i=>(app:result[i],i,score:Confidence(result[i],record))).Where(x=>x.score>=94).ToList();
            if(matches.Count==0) { result.Add(record);Register(result.Count-1,record);continue; }
            if(matches.Count>1)
            {
                result.Add(record with { Ambiguous=true, State=UpdateState.Attention, Explanation="Several installation instances match this provider. Inspect them before updating." });
                Register(result.Count-1,result[^1]);
                foreach(var m in matches) result[m.i]=m.app with { Ambiguous=true, State=UpdateState.Attention, Explanation="Provider identity is ambiguous; automatic updates are disabled." };
                continue;
            }
            var existing=matches[0].app;
            var primary=Rank(record.Provider)>Rank(existing.Provider)?record:existing;
            var other=ReferenceEquals(primary,record)?existing:record;
            bool versionsDisagree=Versioning.Compare(primary.InstalledVersion,other.InstalledVersion) is int comparison?comparison!=0:!Eq(primary.InstalledVersion,other.InstalledVersion);
            versionsDisagree=versionsDisagree||primary.VersionEvidenceConflict||other.VersionEvidenceConflict;
            result[matches[0].i]=primary with
            {
                Id=existing.Id,
                InstallLocation=First(primary.InstallLocation,other.InstallLocation),
                IconPath=First(primary.IconPath,other.IconPath),
                PrimaryExecutable=First(primary.PrimaryExecutable,other.PrimaryExecutable),
                Architecture=primary.Architecture=="Unknown"?other.Architecture:primary.Architecture,
                Scope=primary.Scope=="Unknown"?other.Scope:primary.Scope,
                InstalledChannel=First(primary.InstalledChannel,other.InstalledChannel),
                ProductCodes=primary.ProductCodes.Union(other.ProductCodes,StringComparer.OrdinalIgnoreCase).ToArray(),
                Pinned=primary.Pinned||other.Pinned,
                PinVerified=primary.PinVerified||other.PinVerified,
                InstalledVersionEvidence=primary.InstalledVersionEvidence.Union(other.InstalledVersionEvidence).ToArray(),
                VersionEvidenceConflict=versionsDisagree,
                State=versionsDisagree?UpdateState.Attention:primary.State,
                Explanation=versionsDisagree?"The registry and package catalog report different versions for the same installation registration. This may be a version mapping or a concurrent change. Raw source values are retained; recheck or inspect before updating.":primary.Explanation,
                Evidence=primary.Evidence.Union(other.Evidence).ToArray()
            };
            Register(matches[0].i,result[matches[0].i]);
        }
        // The CLI cannot unambiguously target multiple instances of the same package in one scope.
        foreach(var group in result.Where(x=>x.Provider=="WinGet" && x.PackageId.Length>0).GroupBy(x=>$"{x.PackageId}|{x.Scope}",StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1))
            foreach(var app in group.ToArray()) { var i=result.IndexOf(app); result[i]=app with { Ambiguous=true,State=UpdateState.Attention,Explanation="Multiple installations share this package ID and scope. Use the publisher's updater." }; }
        return result.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private static int Rank(string provider) => provider switch { "Scoop" or "Chocolatey" or "Steam" or "Epic"=>5,"Store" or "MSIX"=>4,"WinGet"=>3,"Portable"=>2,_=>1 };
    private static IEnumerable<string> CandidateKeys(Software app)
    {
        string kind=((int)app.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture)+"|";
        yield return kind+"id|"+app.Id;
        foreach(string code in app.ProductCodes)if(!string.IsNullOrWhiteSpace(code))yield return kind+"code|"+code;
        if(!string.IsNullOrWhiteSpace(app.FamilyName))yield return kind+"family|"+app.FamilyName;
        if(!string.IsNullOrWhiteSpace(app.PackageId))yield return kind+"package|"+app.PackageId;
        string path=Normalize(app.InstallLocation);
        if(path.Length>0&&app.Publisher.Length>0&&app.Name.Length>0)yield return kind+"path|"+path+"|"+app.Publisher+"|"+app.Name;
    }
    private static string First(string a,string b)=>a.Length>0?a:b;
    public static string Normalize(string path)=>path.Trim().TrimEnd('\\','/');
}
