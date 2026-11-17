using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class CatalogFreshnessTests
{
    [Theory][InlineData("old")][InlineData("future")][InlineData("missing")]
    public void ARecentQueryCannotMakeAnOldOrUnverifiedCatalogCurrent(string reason)
    {
        var app=Fixture.App() with{CatalogUpdatedAt=reason switch{"old"=>DateTimeOffset.UtcNow.AddHours(-1),"future"=>DateTimeOffset.UtcNow.AddHours(1),_=>null}};
        Assert.False(app.HasFreshCheck);Assert.Empty(UpdatePolicy.Plan([app],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue).Automatic);
        Assert.Contains("catalog cache",UpdatePolicy.BlockReason(app,new(),DateTimeOffset.UtcNow,false,false,long.MaxValue)!);
    }
    [Fact]public void ARecentSourceRefreshAndRecentQueryAreBothRequired()
    {var app=Fixture.App();Assert.True(app.HasFreshCheck);Assert.False((app with{CheckedAt=DateTimeOffset.UtcNow.AddHours(-1)}).HasFreshCheck);}
    [Fact]public void ADirectPublisherCheckDoesNotPretendToUseTheWinGetIndexClock()
    {var app=Fixture.App() with{Provider="Official installer",Delivery=UpdateDelivery.OfficialInstaller,CatalogUpdatedAt=null};Assert.True(app.HasFreshCheck);}
}
