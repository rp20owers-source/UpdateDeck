using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class VersionRobustnessTests
{
    [Theory][InlineData("１.２")][InlineData("١.٢")][InlineData("१२.३")][InlineData("1.0-alpha..2")][InlineData("1.0+metadata..x")][InlineData("1..2")][InlineData("1.0-")][InlineData("1.0+")]
    public void UnsupportedNumericScriptsOrEmptyIdentifiersRemainUnknown(string value)
    {Assert.False(Versioning.Known(value));Assert.Null(Versioning.Compare(value,"2.0"));Assert.Null(Versioning.Compare("2.0",value));Assert.False(Versioning.MajorChange(value,"2.0"));}
    [Fact]public void MixedUnicodeAndMalformedVersionEvidenceCannotCrashInventoryOrdering()
    {
        var random=new Random(8472);const string alphabet="0123456789.vV-+abc١٢३４５\0\n\t\ud800\udfff";
        for(int i=0;i<4000;i++)
        {
            string text=new(Enumerable.Range(0,random.Next(0,80)).Select(_=>alphabet[random.Next(alphabet.Length)]).ToArray());
            _=Versioning.Known(text);int? forward=Versioning.Compare(text,"2.0"),backward=Versioning.Compare("2.0",text);Assert.Equal(forward.HasValue,backward.HasValue);if(forward.HasValue)Assert.Equal(-Math.Sign(forward.Value),Math.Sign(backward!.Value));
        }
    }
    [Fact]public void OneUnknownRegistrationDoesNotBreakIndependentIdentityReconciliation()
    {
        var a=Fixture.App("unicode") with{InstalledVersion="١.٢",ProductCodes=[]};var b=Fixture.App("other") with{InstalledVersion="1.2",ProductCodes=[]};
        var records=IdentityResolver.Resolve([a,b]);Assert.Equal(2,records.Count);Assert.Null(Versioning.Compare(records.Single(x=>x.Id==a.Id).InstalledVersion,"2.0"));
    }
}
