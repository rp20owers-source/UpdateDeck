using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;

namespace UpdateDeck.Tests;

public class SharedReleaseChecksTests
{
    [Theory]
    [InlineData("Steam","Valve counterfeit","steam")]
    [InlineData("Steamworks helper","Valve Corporation","steam")]
    [InlineData("SuperSteam","Valve Corporation","steam")]
    [InlineData("NVIDIA App","NVIDIA Tools Company","nvidia")]
    [InlineData("Pianoverse","IK Multimedia Helper","ik")]
    [InlineData("Pianoversetrojan","IK Multimedia","ik")]
    [InlineData("Visual Studio Installer","Microsoft Other Company","visualstudio")]
    public void LookalikeNamesAndPublishersCannotSelectAVendor(string name,string publisher,string vendor)
        =>Assert.False(VendorIdentity.Matches(new(){Name=name,Publisher=publisher},VendorLaunchers.Definitions.Single(x=>x.Id==vendor)));
    [Theory]
    [InlineData("Steam","Valve Corporation","steam")]
    [InlineData("Pianoverse version 1.0.11","IK Multimedia","ik")]
    [InlineData("AmpliTube5","IK Multimedia","ik")]
    [InlineData("Visual Studio Installer","Microsoft Corporation","visualstudio")]
    public void ReviewedProductAndPublisherPairsStillMatch(string name,string publisher,string vendor)
        =>Assert.True(VendorIdentity.Matches(new(){Name=name,Publisher=publisher},VendorLaunchers.Definitions.Single(x=>x.Id==vendor)));
    [Theory]
    [InlineData("architecture")]
    [InlineData("scope")]
    [InlineData("location")]
    [InlineData("stale")]
    [InlineData("catalog")]
    [InlineData("version")]
    [InlineData("channel")]
    [InlineData("vendor")]
    public void IncompleteOrConflictingEvidenceCannotOfferAnUpdateAction(string field)
    {
        var original=Fixture.App();Assert.True(original.CanUpdate);
        var app=field switch{"architecture"=>original with{Architecture="Unknown"},"scope"=>original with{Scope="Unknown"},"location"=>original with{InstallLocation=""},"stale"=>original with{CheckedAt=DateTimeOffset.UtcNow.AddHours(-1)},"catalog"=>original with{CatalogUpdatedAt=null},"version"=>original with{AvailableVersion=original.InstalledVersion},"channel"=>original with{AvailableChannel="beta"},_=>original with{VendorId="adobe"}};
        Assert.False(app.CanUpdate);Assert.False(app.CanSupervise);
    }
}
