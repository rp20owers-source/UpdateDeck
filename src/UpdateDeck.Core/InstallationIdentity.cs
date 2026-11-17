namespace UpdateDeck.Core;

/// <summary>
/// Carries an installation's durable ID across scans. Unlike within-scan deduplication,
/// this permits a changed version/product code only with an unchanged strong installation identity.
/// </summary>
public static class InstallationIdentity
{
    private static bool Equal(string a,string b)=>a.Length>0&&b.Length>0&&a.Equals(b,StringComparison.OrdinalIgnoreCase);
    private static bool Known(string a)=>a.Length>0&&!a.Equals("Unknown",StringComparison.OrdinalIgnoreCase);

    public static bool Continues(Software current,Software previous)
    {
        if(current.Id==previous.Id)return true;
        if(current.Ambiguous||previous.Ambiguous)return false;
        if(current.Kind!=previous.Kind||!current.InstalledChannel.Equals(previous.InstalledChannel,StringComparison.OrdinalIgnoreCase))return false;
        if(!Known(current.Scope)||!Known(previous.Scope)||!Equal(current.Scope,previous.Scope))return false;
        if(Known(current.Architecture)&&Known(previous.Architecture)&&!Equal(current.Architecture,previous.Architecture))return false;
        if(Equal(current.FamilyName,previous.FamilyName))return true;
        if(!Equal(current.PackageId,previous.PackageId))return false;
        if(current.Provider!=previous.Provider)return false;
        if(!Equal(IdentityResolver.Normalize(current.InstallLocation),IdentityResolver.Normalize(previous.InstallLocation)))return false;
        // A publisher disagreement can indicate a different edition or hijacked registration.
        if(current.Publisher.Length>0&&previous.Publisher.Length>0&&!Equal(current.Publisher,previous.Publisher))return false;
        return true;
    }

    public static IReadOnlyList<Software> Continue(IReadOnlyList<Software> current,IReadOnlyList<Software> previous)
    {
        if(previous.Count==0)return current;
        // Continuity requires the same record ID, family name or package ID. Use that
        // necessary condition to avoid comparing every current item with every old item.
        var index=new Dictionary<string,List<int>>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<previous.Count;i++)foreach(string key in Keys(previous[i]))
        {if(!index.TryGetValue(key,out var positions))index[key]=positions=[];positions.Add(i);}
        var candidates=current.Select(app=>
        {
            var positions=new HashSet<int>();foreach(string key in Keys(app))if(index.TryGetValue(key,out var found))positions.UnionWith(found);
            return positions.Order().Select(position=>(old:previous[position],index:position)).Where(x=>Continues(app,x.old)).ToArray();
        }).ToArray();
        var references=new int[previous.Count];foreach(var matches in candidates)foreach(var match in matches)references[match.index]++;
        var currentIds=current.Select(x=>x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result=new Software[current.Count];
        for(int i=0;i<current.Count;i++)
        {
            var app=current[i];var matches=candidates[i];
            if(matches.Length==1)
            {
                var prior=matches[0];
                bool unique=references[prior.index]==1;
                bool collision=currentIds.Contains(prior.old.Id)&&!app.Id.Equals(prior.old.Id,StringComparison.OrdinalIgnoreCase);
                result[i]=unique&&!collision?app with{Id=prior.old.Id}:app with{Ambiguous=true,State=UpdateState.Attention,Explanation="Installation continuity is ambiguous. Inspect the matching registrations before updating."};
            }
            else if(matches.Length>1)
            {
                // An exact prior ID is authoritative even when a previously ambiguous neighbor shares a path.
                var exact=matches.Where(x=>x.old.Id==app.Id).ToArray();
                result[i]=exact.Length==1?app:app with{Ambiguous=true,State=UpdateState.Attention,Explanation="Several previous installations match this provider record. Automatic updates are disabled."};
            }
            else result[i]=app;
        }
        return result;
    }
    private static IEnumerable<string> Keys(Software app)
    {
        yield return "id|"+app.Id;
        if(app.FamilyName.Length>0)yield return "family|"+app.FamilyName;
        if(app.PackageId.Length>0)yield return "package|"+app.PackageId;
    }
}
