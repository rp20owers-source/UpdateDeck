using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class ContinuityTests
{
    [Fact]public void ProductCodeAndVersionChangeKeepsInstallationHistoryIdentity()
    {
        var old=Fixture.App() with{ProductCodes=["old-msi"]};
        var fresh=old with{Id="new-registration",InstalledVersion="1.1.0",ProductCodes=["new-msi"]};
        Assert.Equal(old.Id,Assert.Single(InstallationIdentity.Continue([fresh],[old])).Id);
    }
    [Theory][InlineData("path")][InlineData("scope")][InlineData("architecture")][InlineData("provider")][InlineData("publisher")]
    public void GenuineInstallationChangesNeverInheritOldPolicy(string kind)
    {
        var old=Fixture.App();var fresh=old with{Id="fresh",InstalledVersion="2.0"};
        fresh=kind switch{"path"=>fresh with{InstallLocation=old.InstallLocation+"-new"},"scope"=>fresh with{Scope="Machine"},"architecture"=>fresh with{Architecture="Arm64"},"provider"=>fresh with{Provider="Scoop"},_=>fresh with{Publisher="Another publisher"}};
        Assert.Equal("fresh",Assert.Single(InstallationIdentity.Continue([fresh],[old])).Id);
    }
    [Fact]public void StoreFamilyIsStableAcrossVersionedInstallDirectories()
    {
        var old=Fixture.App() with{Provider="Store",FamilyName="Example_family"};
        var fresh=old with{Id="new",InstallLocation=old.InstallLocation+"_2.0",InstalledVersion="2.0"};
        Assert.Equal(old.Id,Assert.Single(InstallationIdentity.Continue([fresh],[old])).Id);
    }
    [Fact]public void MultipleCurrentMatchesAreBlockedInsteadOfStealingOnePriorId()
    {
        var old=Fixture.App();var a=old with{Id="new-a"};var b=old with{Id="new-b"};
        var rows=InstallationIdentity.Continue([a,b],[old]);Assert.Equal(2,rows.Select(x=>x.Id).Distinct().Count());Assert.All(rows,x=>Assert.True(x.Ambiguous));
    }
    [Fact]public void MissingPathAndPackageIdCannotCarryPolicyBasedOnName()
    {
        var old=Fixture.App() with{PackageId="",InstallLocation=""};var fresh=old with{Id="fresh"};
        Assert.False(InstallationIdentity.Continues(fresh,old));
    }
    [Fact]public void MalformedCachedMetadataCannotAuthorizeUpdates()
    {
        var app=Fixture.App() with{Name=null!,ProductCodes=null!,Evidence=null!,InstallLocation="bad\0path",State=(UpdateState)999};
        var safe=SoftwareValidation.Normalize(app);Assert.True(safe.Ambiguous);Assert.False(safe.CanUpdate);Assert.NotNull(safe.Name);Assert.Empty(safe.ProductCodes);Assert.Empty(safe.Evidence);Assert.Equal(UpdateState.Attention,safe.State);
    }
    [Fact]public void NullPreferenceEntriesDoNotCrashValidation()
    {
        var p=new Preferences{PortableDirectories=[null!,"relative"],ExcludedDirectories=[null!,"relative"],Ignored=new HashSet<string>{null!,"ABC","abc"}};
        p.Validate();Assert.Empty(p.PortableDirectories);Assert.Empty(p.ExcludedDirectories);Assert.Single(p.Ignored);Assert.Contains("aBc",p.Ignored);
    }
}
