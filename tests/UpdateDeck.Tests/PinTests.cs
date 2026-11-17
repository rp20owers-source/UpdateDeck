using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class PinTests
{
    [Fact]public void StandardErrorWarningsCannotAuthorizeAnApparentlyEmptyPinList()
    {Assert.False(WinGetPins.Parse(new(0,"There are no pins configured.","Warning: source or policy could not be read.")).Verified);}
    [Theory]
    [InlineData("There are no pins configured.")]
    [InlineData("Es sind keine Pins konfiguriert.")]
    [InlineData("No hay anclajes configurados.")]
    [InlineData("Aucune épingle n’est configurée.")]
    [InlineData("Nessun PIN configurato.")]
    [InlineData("ピンが構成されていません。")]
    [InlineData("구성된 핀이 없습니다.")]
    [InlineData("Não há martcadores configurados.")]
    [InlineData("Закрепления не настроены.")]
    [InlineData("没有已配置包钉。")]
    [InlineData("沒有經過設定的釘選。")]
    public void OfficialLocalizedEmptyPinMessagesAreRecognized(string message)=>Assert.True(WinGetPins.Parse(new(0,message," ")).Verified);

    [Fact]public void VersionColumnCannotMasqueradeAsPackageIdWhenNameUsesSinglePaddingSpace()
    {
        const string output="Name                     Id               Version Source Pin type Pinned version\n-------------------------------------------------------------------------------\nLongest application name Example.Package 1.2b winget Pinning\n";
        var parsed=WinGetPins.Parse(new(0,output,""),["Example.Package","Other.Application"]);
        Assert.True(parsed.Verified);Assert.Contains("Example.Package",parsed.Ids);Assert.DoesNotContain("1.2b",parsed.Ids);
    }
    [Fact]public void WideUnicodeNamesAndInternalSpacesDoNotChangePinIdentity()
    {
        const string output="名称    ID     版本    源    固定类型    固定版本\n------------------------------------------------------------\n文字 application  with  spaces Example.Package 1.0 winget Pinning\n";
        // Use the exact official Chinese header; no positional assumptions are made about display cells.
        var unknown=WinGetPins.Parse(new(0,output,""),["Example.Package"]);
        Assert.False(unknown.Verified); // A changed or guessed translation must never authorize updates.
        const string english="Name Id Version Source Pin type Pinned version\n------------------------------------------------------------\n文字 application  with  spaces Example.Package 1.0 winget Pinning\n";
        var parsed=WinGetPins.Parse(new(0,english,""),["Example.Package"]);Assert.True(parsed.Verified);Assert.Contains("Example.Package",parsed.Ids);
    }
    [Fact]public void ANoPinsPhraseInsideOtherOutputIsNotAnEmptyPinResult()
    {
        var parsed=WinGetPins.Parse(new(0,"Warning\nThere are no pins configured.\n",""));Assert.False(parsed.Verified);
    }
    [Fact]public void TruncatedOrUnknownPinRowsFailClosed()
    {
        const string header="Name Id Version Source Pin type Pinned version\n------------------------------------------------------------\n";
        Assert.False(WinGetPins.Parse(new(0,header+"App Example.Pack… 1.0 winget Pinning", ""),["Example.Package"]).Verified);
        Assert.False(WinGetPins.Parse(new(0,header+"App Unknown.Package 1.0 winget Pinning", ""),["Example.Package"]).Verified);
    }
    [Fact]public void InstalledProductCodePinsAreRecognized()
    {
        var app=Fixture.App() with{ProviderInstanceId="ARP\\Machine\\X64\\{AABBCCDD-0000-1111-2222-123456789012}",ProductCodes=["{AABBCCDD-0000-1111-2222-123456789012}"]};
        string text="Name Id Version Source Pin type Pinned version\n------------------------------------------------------------\nApp "+app.ProviderInstanceId+" 1.0 Installed Pinning";
        var parsed=WinGetPins.Parse(new(0,text,""),WinGetPins.Keys(app));Assert.True(parsed.Verified);Assert.True(WinGetPins.IsPinned(app,parsed.Ids));
    }
}
