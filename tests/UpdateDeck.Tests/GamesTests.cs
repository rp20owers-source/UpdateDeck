using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;

namespace UpdateDeck.Tests;

public class GamesTests
{
    [Fact]
    public void UnmappedCatalogSteamRegistrationsStayInGamesAndReconcileWithRegistry()
    {
        var registry=new Software{Id="registry",Name="Game",Provider="Steam",Kind=SoftwareKind.Game,ProductCodes=["Steam App 123"],VendorId="steam",Scope="Machine"};
        var catalog=registry with{Id="catalog",Provider="Registry",Kind=SoftwareKind.Application,VendorId="",Scope="Unknown"};
        Assert.True(GamePlatforms.IsGaming(catalog));
        Assert.Empty(UpdatePolicy.Plan([catalog with{State=UpdateState.Attention}],new Preferences(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue).Deferred);
        var resolved=IdentityResolver.Resolve(new[]{registry,catalog}.Select(GamePlatforms.Categorize));
        Assert.Equal(SoftwareKind.Game,Assert.Single(resolved).Kind);
    }
    [Theory]
    [InlineData(SoftwareKind.Game)]
    [InlineData(SoftwareKind.GameLauncher)]
    public void GamesNeverEnterDesktopInstallerQueue(SoftwareKind kind)
    {
        var app=new Software{Id="game",Name="Game",Kind=kind,VendorId="steam",Provider="WinGet",State=UpdateState.Available,PinVerified=true,Risk=Risk.Routine};
        Assert.False(app.CanUpdate);Assert.False(app.CanSupervise);
        var plan=UpdatePolicy.Plan([app],new Preferences(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue);
        Assert.Empty(plan.Automatic);Assert.Empty(plan.Handoffs);
        Assert.Empty(plan.Deferred);
    }
    [Theory]
    [InlineData("Steam","",UpdateState.Available)]
    [InlineData("Epic","",UpdateState.Attention)]
    [InlineData("GOG","",UpdateState.Unknown)]
    [InlineData("WinGet","steam",UpdateState.Available)]
    [InlineData("WinGet","epic",UpdateState.Attention)]
    [InlineData("WinGet","xbox",UpdateState.Available)]
    public void GamingOwnershipAlsoExcludesLegacyAndConflictedRecords(string provider,string vendor,UpdateState state)
    {
        var app=new Software{Id="legacy-game",Name="Game",Provider=provider,VendorId=vendor,State=state,PinVerified=true,Risk=Risk.Routine};
        Assert.True(GamePlatforms.IsGaming(app));
        Assert.NotEqual(SoftwareKind.Application,GamePlatforms.Categorize(app).Kind);
        Assert.False(app.CanUpdate);Assert.False(app.CanSupervise);
        var plan=UpdatePolicy.Plan([app],new Preferences(),DateTimeOffset.UtcNow,false,_=>throw new InvalidOperationException("Games must not be inspected for desktop updates"),long.MaxValue);
        Assert.Empty(plan.Automatic);Assert.Empty(plan.Handoffs);Assert.Empty(plan.Deferred);
    }
    [Fact]
    public void EveryGamePlatformHasReviewedDestination()
    {
        var targets=new VendorLaunchers();targets.Attach([]);
        foreach(var id in GamePlatforms.Ids)
        {
            var target=Assert.Single(targets.Targets,x=>x.Id==id);
            Assert.True(VendorLaunchers.IsReviewedUrl(target.OfficialUrl));
            Assert.StartsWith("https://",target.OfficialUrl);
        }
    }
    [Fact]
    public void EpicManifestDoesNotRunEmbeddedCommands()
    {
        var folder=Path.Combine(Path.GetTempPath(),"UpdateDeck-game-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string Json(bool incomplete)=>System.Text.Json.JsonSerializer.Serialize(new{AppName="test-id",DisplayName="Test game",InstallLocation=folder,AppVersionString="build-123",bIsIncompleteInstall=incomplete,LaunchExecutable="arbitrary-command.exe"});
            var app=Assert.IsType<Software>(GameLibraryInventory.ParseEpic(Json(false)));
            Assert.Equal(SoftwareKind.Game,app.Kind);Assert.Equal("Epic",app.Provider);Assert.Equal("",app.PrimaryExecutable);Assert.False(app.CanUpdate);
            Assert.Null(GameLibraryInventory.ParseEpic(Json(true)));
            Assert.Null(GameLibraryInventory.ParseEpic("{\"AppName\":\"id\",\"DisplayName\":\"Game\",\"InstallLocation\":\"relative/path\"}"));
        }
        finally{Directory.Delete(folder);}
    }
}
