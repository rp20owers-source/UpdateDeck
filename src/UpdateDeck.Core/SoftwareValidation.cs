namespace UpdateDeck.Core;

/// <summary>Handles malformed cached/provider metadata without turning it into executable policy.</summary>
public static class SoftwareValidation
{
    public static Software Normalize(Software app)
    {
        bool malformed=false;
        string Text(string? text,int maximum,string fallback="")
        {
            if(text==null)return fallback;
            if(text.Length>maximum){malformed=true;return text[..maximum];}
            if(text.IndexOf('\0')>=0){malformed=true;return text.Replace('\0','\u2400');}
            return text;
        }
        string[] Array(string[]? values,int maximum)
        {
            if(values==null)return [];
            if(values.Length>maximum||values.Any(x=>x==null))malformed=true;
            return values.Where(x=>x!=null).Take(maximum).Select(x=>Text(x,4096)).ToArray();
        }
        var normalized=app with
        {
            Id=Text(app.Id,256),Name=Text(app.Name,4096,"Unnamed registration"),Publisher=Text(app.Publisher,4096),
            InstalledVersion=Text(app.InstalledVersion,4096,"Unknown"),AvailableVersion=Text(app.AvailableVersion,4096),
            InstalledVersionEvidence=Array(app.InstalledVersionEvidence,128),
            InstalledChannel=Text(app.InstalledChannel,256),AvailableChannel=Text(app.AvailableChannel,256),
            PackageId=Text(app.PackageId,512),ProviderInstanceId=Text(app.ProviderInstanceId,1024),FamilyName=Text(app.FamilyName,1024),ProductCodes=Array(app.ProductCodes,128),
            InstallLocation=Text(app.InstallLocation,32767),IconPath=Text(app.IconPath,32767),PrimaryExecutable=Text(app.PrimaryExecutable,32767),Architecture=Text(app.Architecture,64,"Unknown"),
            Scope=Text(app.Scope,64,"Unknown"),InstallerType=Text(app.InstallerType,256,"Unknown"),Provider=Text(app.Provider,256,"Unknown"),
            VendorId=Text(app.VendorId,128),VendorActionLabel=Text(app.VendorActionLabel,128),OfficialInstallerId=Text(app.OfficialInstallerId,128),Explanation=Text(app.Explanation,32768),Evidence=Array(app.Evidence,128)
        };
        if(normalized.ProductCodes.Any(string.IsNullOrWhiteSpace)){malformed=true;normalized=normalized with{ProductCodes=normalized.ProductCodes.Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray()};}
        if(!Enum.IsDefined(normalized.State)||!Enum.IsDefined(normalized.Risk)||!Enum.IsDefined(normalized.Delivery)||!Enum.IsDefined(normalized.Kind))malformed=true;
        if(normalized.Id.Length==0)
        {
            malformed=true;
            normalized=normalized with{Id=Software.Key($"invalid|{normalized.Provider}|{normalized.Name}|{normalized.InstalledVersion}|{normalized.InstallLocation}")};
        }
        if(malformed)return normalized with{Ambiguous=true,State=UpdateState.Attention,Risk=Risk.Caution,Explanation="This record contains malformed metadata. It is shown for inspection and cannot authorize an automatic update."};
        return normalized;
    }
}
