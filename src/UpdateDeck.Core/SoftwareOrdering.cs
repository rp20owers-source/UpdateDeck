using System.Globalization;
namespace UpdateDeck.Core;

public static class SoftwareOrdering
{
    public static int Compare(Software a,Software b,string sort,bool descending)
    {
        int Text(string x,string y)=>CultureInfo.CurrentCulture.CompareInfo.Compare(x,y,CompareOptions.IgnoreCase);
        int comparison;
        if(sort is "Installed version" or "Available version")
        {
            string x=sort=="Installed version"?a.InstalledVersion:a.AvailableVersion,y=sort=="Installed version"?b.InstalledVersion:b.AvailableVersion;
            bool knownX=Versioning.Known(x),knownY=Versioning.Known(y);
            if(knownX!=knownY)return knownX?-1:1; // Unsupported versions stay last in either direction.
            comparison=Versioning.Compare(x,y)??Text(x,y);
        }
        else comparison=sort switch{"Publisher"=>Text(a.Publisher,b.Publisher),"Status"=>Status(a).CompareTo(Status(b)),"Provider"=>Text(a.Provider,b.Provider),"Last checked"=>Nullable.Compare(a.CheckedAt,b.CheckedAt),_=>Text(a.Name,b.Name)};
        if(descending)comparison=-Math.Sign(comparison);
        if(comparison!=0)return comparison;
        int name=Text(a.Name,b.Name);return name!=0?name:StringComparer.OrdinalIgnoreCase.Compare(a.Id,b.Id);
    }
    private static int Status(Software app)=>app.Pinned?1:app.State switch{UpdateState.Available=>0,UpdateState.Attention=>1,UpdateState.VendorManaged=>2,UpdateState.Unknown=>3,UpdateState.Ignored=>4,_=>5};
}
