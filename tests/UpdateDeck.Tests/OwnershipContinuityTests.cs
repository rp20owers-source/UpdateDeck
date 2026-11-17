using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class OwnershipContinuityTests
{
    [Theory][InlineData("WinGet","Registry")][InlineData("Scoop","WinGet")][InlineData("Chocolatey","Registry")]
    public void AProviderOutageCannotAuthorizeAnAutomaticOwnerSwitch(string previous,string current)
    {
        var old=Fixture.App() with{Provider=previous};var fresh=old with{Provider=current};var result=ManagerOwnership.PreserveWhenUnverified(fresh,old);
        Assert.Equal(previous,result.Provider);Assert.Equal(UpdateState.Attention,result.State);Assert.False(result.CanUpdate);Assert.False(result.CanSupervise);Assert.Contains("ownership",result.Explanation);
    }
    [Fact]public void APositiveFreshManagerManifestWinsOverACommunityCatalogAssociation()
    {var old=Fixture.App();var fresh=old with{Provider="Scoop",State=UpdateState.VendorManaged};Assert.Same(fresh,ManagerOwnership.PreserveWhenUnverified(fresh,old));}
    [Fact]public void UnrelatedInstallationIdsNeverInheritAnOwner()
    {var old=Fixture.App("old");var fresh=Fixture.App("new") with{Provider="Registry"};Assert.Same(fresh,ManagerOwnership.PreserveWhenUnverified(fresh,old));}
    [Fact]public void PreservedWinGetOwnershipCannotEnterTheOfficialInstallerFallback()
    {
        var old=Fixture.App() with{Name="Notepad++",Publisher="Notepad++ Team",Scope="Machine"};var fresh=old with{Provider="Registry",PackageId="",State=UpdateState.Unknown};Assert.True(OfficialInstallerProvider.IsCandidate(fresh));
        var held=ManagerOwnership.PreserveWhenUnverified(fresh,old);Assert.False(OfficialInstallerProvider.IsCandidate(held));
    }
}
