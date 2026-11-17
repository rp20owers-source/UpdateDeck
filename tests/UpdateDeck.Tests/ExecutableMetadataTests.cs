using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class ExecutableMetadataTests
{
    private static string Helper=>Path.Combine(AppContext.BaseDirectory,"helpers","UpdateDeck.TestInstaller.exe");
    [Theory][InlineData("3.7.1","3,7,1,0")][InlineData("3.7.1.0","3,7,1,0")]
    public void MatchesCommaSeparatedWindowsVersionResources(string installed,string resource)=>Assert.True(ExecutableMetadata.ResourceVersionMatches(installed,resource));
    [Theory][InlineData("3.7.1","3,7,2,0")][InlineData("3.7.1","3,7,1,0 product")][InlineData("3.7.1","3,7")]
    public void RejectsMismatchedOrMalformedWindowsVersionResources(string installed,string resource)=>Assert.False(ExecutableMetadata.ResourceVersionMatches(installed,resource));
    [Fact]public void ReadsNativeAppHostArchitectureWithoutExecutingIt()=>Assert.Equal("X64",ExecutableMetadata.Architecture(Helper));
    [Fact]public void DoesNotGuessArchitectureFromMalformedHeader(){var file=Path.Combine(Fixture.Temp(),"bad.exe");File.WriteAllBytes(file,[0x4d,0x5a,0,0]);Assert.Null(ExecutableMetadata.Architecture(file));}
    [Fact]public void DoesNotTreatAnyCpuManagedLibraryAsX86()=>Assert.Null(ExecutableMetadata.Architecture(typeof(UpdateDeck.Core.Software).Assembly.Location));
    [Fact]public void MatchingRegisteredExecutableCanRecoverAMissingInstallPath()
    {
        var app=Fixture.App() with{Name="UpdateDeck",Publisher="Ryan Powers",InstalledVersion="2.0.3",InstallLocation="",IconPath=Helper,Architecture="Unknown"};
        var enriched=ExecutableMetadata.EnrichRegistration(app);Assert.Equal(Path.GetDirectoryName(Helper),enriched.InstallLocation);Assert.Equal(Helper,enriched.PrimaryExecutable);Assert.Equal("X64",enriched.Architecture);
    }
    [Theory][InlineData("publisher")][InlineData("version")][InlineData("product")]
    public void MismatchingExecutableCannotInventAnInstallationPath(string mismatch)
    {
        var app=Fixture.App() with{Name="UpdateDeck",Publisher="Ryan Powers",InstalledVersion="2.0.3",InstallLocation="",IconPath=Helper};
        app=mismatch switch{"publisher"=>app with{Publisher="Someone Else"},"version"=>app with{InstalledVersion="99.0"},_=>app with{Name="Another Application"}};
        Assert.Empty(ExecutableMetadata.EnrichRegistration(app).InstallLocation);
    }
    [Theory][InlineData("vcredist_x64.exe")][InlineData("VC_redist.x64.exe")]
    public void RedistributableBootstrapperIconsCannotDefineInstalledArchitecture(string filename)
    {
        var executable=Path.Combine(Fixture.Temp(),filename);File.Copy(Helper,executable);
        var app=Fixture.App() with{Name="UpdateDeck",Publisher="Ryan Powers",InstalledVersion="2.0.3",InstallLocation="",IconPath=executable,Architecture="Unknown"};
        var enriched=ExecutableMetadata.EnrichRegistration(app);
        Assert.Equal("Unknown",enriched.Architecture);Assert.Empty(enriched.InstallLocation);Assert.Empty(enriched.PrimaryExecutable);
    }
    [Fact]public void VerifiedArchitectureTakesPrecedenceOverRegisteredIconArchitecture()
    {
        var app=Fixture.App() with{Name="UpdateDeck",Publisher="Ryan Powers",InstalledVersion="2.0.3",InstallLocation="",IconPath=Helper,Architecture="Arm64"};
        Assert.Equal("Arm64",ExecutableMetadata.EnrichRegistration(app).Architecture);
    }
    [Theory][InlineData("\\\\server\\share\\application.exe")][InlineData("\\\\?\\UNC\\server\\share\\application.exe")]
    public void RegistryIconsCannotTriggerImplicitNetworkExecutableInspection(string path)=>Assert.Null(ExecutableMetadata.LocalIconExecutable(path));
}
