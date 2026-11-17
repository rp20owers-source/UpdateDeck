using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class ExecutableMetadataTests
{
    private static string Helper=>Path.Combine(AppContext.BaseDirectory,"helpers","UpdateDeck.TestInstaller.exe");
    [Fact]public void ReadsNativeAppHostArchitectureWithoutExecutingIt()=>Assert.Equal("X64",ExecutableMetadata.Architecture(Helper));
    [Fact]public void DoesNotGuessArchitectureFromMalformedHeader(){var file=Path.Combine(Fixture.Temp(),"bad.exe");File.WriteAllBytes(file,[0x4d,0x5a,0,0]);Assert.Null(ExecutableMetadata.Architecture(file));}
    [Fact]public void DoesNotTreatAnyCpuManagedLibraryAsX86()=>Assert.Null(ExecutableMetadata.Architecture(typeof(UpdateDeck.Core.Software).Assembly.Location));
    [Fact]public void MatchingRegisteredExecutableCanRecoverAMissingInstallPath()
    {
        var app=Fixture.App() with{Name="UpdateDeck",Publisher="Ryan Powers",InstalledVersion="2.0.1",InstallLocation="",IconPath=Helper,Architecture="Unknown"};
        var enriched=ExecutableMetadata.EnrichRegistration(app);Assert.Equal(Path.GetDirectoryName(Helper),enriched.InstallLocation);Assert.Equal(Helper,enriched.PrimaryExecutable);Assert.Equal("X64",enriched.Architecture);
    }
    [Theory][InlineData("publisher")][InlineData("version")][InlineData("product")]
    public void MismatchingExecutableCannotInventAnInstallationPath(string mismatch)
    {
        var app=Fixture.App() with{Name="UpdateDeck",Publisher="Ryan Powers",InstalledVersion="2.0.1",InstallLocation="",IconPath=Helper};
        app=mismatch switch{"publisher"=>app with{Publisher="Someone Else"},"version"=>app with{InstalledVersion="99.0"},_=>app with{Name="Another Application"}};
        Assert.Empty(ExecutableMetadata.EnrichRegistration(app).InstallLocation);
    }
    [Theory][InlineData("\\\\server\\share\\application.exe")][InlineData("\\\\?\\UNC\\server\\share\\application.exe")]
    public void RegistryIconsCannotTriggerImplicitNetworkExecutableInspection(string path)=>Assert.Null(ExecutableMetadata.LocalIconExecutable(path));
}
