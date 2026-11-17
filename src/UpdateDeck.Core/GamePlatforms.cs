namespace UpdateDeck.Core;

public static class GamePlatforms
{
    public static readonly string[] Ids=["steam","epic","battlenet","ea","ubisoft","gog","riot","xbox","rockstar","amazon","itch"];
    public static bool Contains(string id)=>Ids.Contains(id,StringComparer.OrdinalIgnoreCase);
    public static string SteamRegistrationId(Software app)=>app.ProductCodes.Select(code=>code.StartsWith("Steam App ",StringComparison.OrdinalIgnoreCase)?code[10..]:"").FirstOrDefault(id=>id.Length is >0 and <=20&&id.All(char.IsAsciiDigit))??"";
    public static bool IsGaming(Software app)=>app.Kind is SoftwareKind.Game or SoftwareKind.GameLauncher||Contains(app.VendorId)||app.Provider is "Steam" or "Epic" or "GOG"||SteamRegistrationId(app).Length>0;
    public static Software Categorize(Software app)
    {
        var steamId=SteamRegistrationId(app);
        if(steamId.Length>0)return app with{Kind=SoftwareKind.Game,Provider="Steam",VendorId="steam",PackageId="steam:"+steamId,Delivery=UpdateDelivery.None};
        return app.Kind is SoftwareKind.Game or SoftwareKind.GameLauncher?app:
            app.Provider is "Steam" or "Epic" or "GOG"?app with{Kind=SoftwareKind.Game}:
            Contains(app.VendorId)?app with{Kind=SoftwareKind.GameLauncher}:app;
    }
}
