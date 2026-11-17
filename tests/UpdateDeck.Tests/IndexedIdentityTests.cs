using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class IndexedIdentityTests
{
    private static Software Bare(string id)=>Fixture.App(id) with{PackageId="",ProductCodes=[],InstallLocation="",Publisher="Fixture publisher",InstalledVersion="1.0"};
    [Theory][InlineData("id")][InlineData("code")][InlineData("family")][InlineData("package")][InlineData("path")]
    public void EveryStrongIdentityRouteRemainsDiscoverable(string identity)
    {
        var a=Bare("a");var b=Bare("b");
        (a,b)=identity switch
        {
            "id"=>(a,b with{Id=a.Id}),
            "code"=>(a with{ProductCodes=["registration"]},b with{ProductCodes=["REGISTRATION"]}),
            "family"=>(a with{FamilyName="Fixture_family"},b with{FamilyName="fixture_FAMILY"}),
            "package"=>(a with{PackageId="Fixture.App"},b with{PackageId="fixture.APP"}),
            _=>(a with{InstallLocation=@"C:\Fixture\App\"},b with{Name=a.Name,InstallLocation=@"c:\fixture\app"})
        };
        Assert.True(IdentityResolver.Confidence(a,b)>=94);Assert.Single(IdentityResolver.Resolve([a,b]));
    }
    [Fact]public void NewlyMergedIdentifiersAreIndexedForLaterProviderRecords()
    {
        var a=Bare("a") with{Provider="Registry",ProductCodes=["registration"]};
        var b=Bare("b") with{Provider="WinGet",ProductCodes=["registration"],FamilyName="Fixture_family"};
        var c=Bare("c") with{Provider="Store",FamilyName="Fixture_family"};
        var result=Assert.Single(IdentityResolver.Resolve([c,b,a]));Assert.Equal("Store",result.Provider);Assert.Equal(a.Id,result.Id);
    }
    [Fact]public void ABridgeBetweenTwoInstallationsKeepsAllAmbiguousInstances()
    {
        var a=Bare("a") with{Provider="Registry",Scope="User",ProductCodes=["registration-a"]};
        var b=Bare("b") with{Provider="Registry",Scope="Machine",ProductCodes=["registration-b"]};
        var bridge=Bare("bridge") with{Provider="WinGet",Scope="Unknown",Architecture="Unknown",ProductCodes=["registration-a","registration-b"]};
        var result=IdentityResolver.Resolve([bridge,b,a]);Assert.Equal(3,result.Count);Assert.All(result,x=>{Assert.True(x.Ambiguous);Assert.False(x.CanUpdate);});
    }
    [Fact]public void BlankProductCodesCannotCombineUnrelatedRegistrations()
    {
        var result=IdentityResolver.Resolve([Bare("a") with{ProductCodes=[""]},Bare("b") with{ProductCodes=[""]}]);Assert.Equal(2,result.Count);Assert.All(result,x=>Assert.True(x.Ambiguous));
    }
    [Fact]public void LargeMixedProviderInventoryPreservesIndependentInstallationPolicy()
    {
        var registry=Enumerable.Range(0,3000).Select(i=>Fixture.App("record-"+i) with{Provider="Registry"}).ToArray();
        var packages=registry.Select((x,i)=>x with{Id="catalog-"+i,Provider="WinGet",Pinned=i%17==0}).ToArray();var resolved=IdentityResolver.Resolve(packages.Reverse().Concat(registry));
        Assert.Equal(registry.Length,resolved.Count);Assert.Equal(registry.Length,resolved.Select(x=>x.Id).Distinct().Count());Assert.Equal(packages.Count(x=>x.Pinned),resolved.Count(x=>x.Pinned));
        var fresh=resolved.Select((x,i)=>x with{Id="new-"+i,InstalledVersion="2.0",ProductCodes=["new-code-"+i]}).ToArray();var continued=InstallationIdentity.Continue(fresh,resolved);
        Assert.Equal(resolved.Select(x=>x.Id).Order(),continued.Select(x=>x.Id).Order());Assert.All(continued,x=>Assert.False(x.Ambiguous));
    }
}
