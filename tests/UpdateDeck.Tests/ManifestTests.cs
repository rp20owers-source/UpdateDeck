using System.Text.Json;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;

namespace UpdateDeck.Tests;

public class ManifestTests
{
    [Fact]public void VersionTwoRoundTripsSafeMetadataAndSelectedPreferences()
    {
        var app=Fixture.App() with{InstallLocation=@"C:\private\installation",PrimaryExecutable=@"C:\private\secret.exe",InstalledChannel="stable"};
        var preferences=new Preferences{Theme="Light",Schedule="Weekly",QuietStart=23,QuietEnd=6,AllowMeteredChecks=true,Ignored=[app.Id]};
        var manifest=InventoryManifest.Capture([app],preferences,"Arm64");string json=manifest.ToJson();var loaded=InventoryManifest.Parse(json);
        Assert.Equal(2,loaded.Schema);Assert.Equal("Arm64",loaded.SourceArchitecture);Assert.Equal("Weekly",loaded.Preferences!.Schedule);Assert.True(loaded.Preferences.AllowMeteredChecks);
        var saved=Assert.Single(loaded.Applications);Assert.True(saved.Ignored);Assert.Equal("User",saved.Scope);Assert.Equal("stable",saved.Channel);Assert.True(saved.CanCheckCatalog);
        Assert.DoesNotContain("private",json);Assert.DoesNotContain("primaryExecutable",json);Assert.DoesNotContain("canCheckCatalog",json);Assert.DoesNotContain("portableFolders",json);
    }
    [Fact]public void ExistingVersionOneManifestsRemainReadableWithoutInventingSelectors()
    {
        var manifest=InventoryManifest.Parse("""{"schema":1,"architecture":"X64","applications":[{"name":"Editor","packageId":"Vendor.Editor","provider":"WinGet","risk":"Routine","ignored":false}]}""");
        Assert.Equal("X64",manifest.SourceArchitecture);var app=Assert.Single(manifest.Applications);Assert.Equal("Unknown",app.Scope);Assert.Equal("Unknown",app.Architecture);Assert.Equal(SoftwareKind.Application,app.Kind);Assert.True(app.CanCheckCatalog);
    }
    [Theory]
    [InlineData("""{"schema":2,"product":"Other","applications":[]}""")]
    [InlineData("""{"schema":1,"schema":1,"applications":[]}""")]
    [InlineData("""{"schema":"1","applications":[]}""")]
    [InlineData("""{"schema":3,"applications":[]}""")]
    [InlineData("""{"schema":1,"applications":{}}""")]
    [InlineData("""{"schema":1,"applications":[{"name":"A","name":"B"}]}""")]
    [InlineData("""{"schema":1,"applications":[{"name":123}]}""")]
    [InlineData("""{"schema":1,"applications":[{"name":"A","ignored":"yes"}]}""")]
    [InlineData("""{"schema":1,"applications":[{"name":"A","risk":"Unreviewed"}]}""")]
    [InlineData("""{"schema":1,"applications":[{"name":"A","kind":"Unreviewed"}]}""")]
    [InlineData("""{"schema":1,"applications":[],"preferences":{"quietStart":"22"}}""")]
    [InlineData("""{"schema":1,"applications":[],"preferences":{"quietEnd":24}}""")]
    [InlineData("""{"schema":1,"applications":[],"preferences":{"schedule":"Every second"}}""")]
    [InlineData("""{"schema":1,"applications":[],"exportedAt":"tomorrow"}""")]
    public void AmbiguousOrUnsupportedDataIsRejected(string json)=>Assert.Throws<InvalidDataException>(()=>InventoryManifest.Parse(json));
    [Fact]public void ExcessiveCountsDepthAndSizeAreRejected()
    {
        string entries=string.Join(',',Enumerable.Repeat("{\"name\":\"A\"}",5001));Assert.Throws<InvalidDataException>(()=>InventoryManifest.Parse("{\"schema\":1,\"applications\":["+entries+"]}"));
        Assert.Throws<InvalidDataException>(()=>InventoryManifest.Parse(new string(' ',5*1024*1024+1)));
        Assert.ThrowsAny<JsonException>(()=>InventoryManifest.Parse("{\"schema\":1,\"applications\":[],\"unknown\":"+new string('[',20)+"0"+new string(']',20)+"}"));
    }
    [Theory][InlineData("Vendor.Editor",true)][InlineData("--id",false)][InlineData("Vendor.Editor --force",false)][InlineData("https://example.com/a.exe",false)][InlineData("Vendor.Editor\n",false)][InlineData("",false)]
    public void OnlyBoundedPackageIdentifiersCanBeQueried(string id,bool expected)=>Assert.Equal(expected,PackageIdentifiers.IsWinGet(id));
    [Fact]public void ImportedRiskIsReclassifiedAndNonApplicationKindsDoNotQueryCatalog()
    {
        var manifest=InventoryManifest.Parse("""{"schema":1,"applications":[{"name":"GPU Driver","provider":"WinGet","packageId":"Example.Driver","risk":"Routine"},{"name":"Plugin","provider":"WinGet","packageId":"Example.Plugin","kind":"AudioPlugin"}]}""");
        Assert.Equal(Risk.SystemSensitive,manifest.Applications[0].Risk);Assert.False(manifest.Applications[1].CanCheckCatalog);
    }
    [Fact]public async Task InvalidPackageIdsAreRejectedBeforeAnyNativeCatalogCall()
    {
        var result=await new WinGetMigrationCatalog().ResolveAsync("--force", "User",CancellationToken.None);Assert.Equal(CatalogPreviewState.NeedsReview,result.State);
    }
}
