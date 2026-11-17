namespace UpdateDeck.Core;

/// <summary>Original installation selectors retained with an operation, independent of mutable inventory IDs.</summary>
public sealed record InstallationSnapshot
{
    public string Provider {get;init;}="";
    public string PackageId {get;init;}="";
    public string FamilyName {get;init;}="";
    public string Publisher {get;init;}="";
    public string Scope {get;init;}="";
    public string Architecture {get;init;}="";
    public string InstallLocation {get;init;}="";
    public string PrimaryExecutable {get;init;}="";
    public string Channel {get;init;}="";
    public SoftwareKind Kind {get;init;}
    public static InstallationSnapshot Capture(Software app)=>new()
    {
        Provider=app.Provider,PackageId=app.PackageId,FamilyName=app.FamilyName,Publisher=app.Publisher,
        Scope=app.Scope,Architecture=app.Architecture,InstallLocation=app.InstallLocation,
        PrimaryExecutable=app.PrimaryExecutable,Channel=app.InstalledChannel,Kind=app.Kind
    };
    private static bool Same(string? a,string? b)=>!string.IsNullOrWhiteSpace(a)&&!string.IsNullOrWhiteSpace(b)&&a.Equals(b,StringComparison.OrdinalIgnoreCase);
    public bool Matches(Software current)
    {
        if(current.Ambiguous||current.VersionEvidenceConflict||current.State==UpdateState.Attention||Kind!=SoftwareKind.Application||current.Kind!=Kind)return false;
        if(!Same(Provider,current.Provider)||!Same(Publisher,current.Publisher)||Scope is not ("User" or "Machine")||!Same(Scope,current.Scope))return false;
        if(Architecture is not ("X64" or "X86" or "Arm64")||!Same(Architecture,current.Architecture))return false;
        if(string.IsNullOrWhiteSpace(InstallLocation)||string.IsNullOrWhiteSpace(current.InstallLocation)||!Same(IdentityResolver.Normalize(InstallLocation),IdentityResolver.Normalize(current.InstallLocation)))return false;
        if(!string.Equals(Channel,current.InstalledChannel,StringComparison.OrdinalIgnoreCase))return false;
        if(!string.IsNullOrWhiteSpace(PackageId))return Same(PackageId,current.PackageId);
        if(!string.IsNullOrWhiteSpace(FamilyName))return Same(FamilyName,current.FamilyName);
        return Same(PrimaryExecutable,current.PrimaryExecutable);
    }
}
