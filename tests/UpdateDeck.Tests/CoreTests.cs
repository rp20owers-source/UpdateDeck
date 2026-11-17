using UpdateDeck.Core;
using Xunit;

namespace UpdateDeck.Tests;

public static class Fixture
{
    public static Software App(string id="one")=>new(){Id=id,Name=id,Publisher="Example Publisher",InstalledVersion="1.0.0",AvailableVersion="1.1.0",PackageId="Example."+id,Provider="WinGet",State=UpdateState.Available,PinVerified=true,Scope="User",Architecture="X64",InstallLocation=Path.Combine(Path.GetTempPath(),"UpdateDeckFixture",id),ProductCodes=[id],CheckedAt=DateTimeOffset.UtcNow,CatalogUpdatedAt=DateTimeOffset.UtcNow,InventoryObservedAt=DateTimeOffset.UtcNow};
    public static QueuePlan Plan(params Software[] apps)=>new(apps.Select(x=>new PlanItem(x,"Update","test")).ToArray(),[],[]);
    public static string Temp()=>Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"UpdateDeck-tests",Guid.NewGuid().ToString("N"))).FullName;
}
public class VersionTests
{
    [Theory][InlineData("1.0-beta.1","1.1-beta.2",true)][InlineData("1.0-beta1","1.1-beta2",true)][InlineData("1.0-beta","1.1-rc.1",false)][InlineData("1.0-canary","1.1-beta",false)][InlineData("1.0-20261114","1.1-20261115",false)][InlineData("1.0","2.0",true)]
    public void DifferentPrereleaseTracksCannotBeTreatedAsTheSameChannel(string a,string b,bool same)=>Assert.Equal(same,Versioning.SameReleaseTrack(a,b));
    [Theory]
    [InlineData("1.9","1.10",-1)][InlineData("1.0","1.0.0",0)][InlineData("1.2.3.4","1.2.3.3",1)]
    [InlineData("v2.0.0","1.99",1)][InlineData("1.0.0+build.1","1.0.0+build.2",0)]
    [InlineData("1.0.0-alpha.9","1.0.0-alpha.10",-1)][InlineData("1.0.0-beta","1.0.0",-1)]
    [InlineData("1.0.0-alpha.1","1.0.0-alpha.beta",-1)][InlineData("2026.11.14","2026.10.31",1)]
    [InlineData("1000000000000000000000000","999999999999999999999999",1)]
    public void OrdersSupportedVersions(string a,string b,int expected){Assert.Equal(expected,Math.Sign(Versioning.Compare(a,b)!.Value));Assert.Equal(-expected,Math.Sign(Versioning.Compare(b,a)!.Value));}
    [Theory][InlineData("Unknown")][InlineData("")][InlineData("latest")][InlineData("release 2026")][InlineData("1.2 (x64)")][InlineData("1..2")]
    public void RefusesUnreliableFormats(string value)=>Assert.Null(Versioning.Compare(value,"2.0"));
    [Fact]public void DetectsReleaseChannels(){Assert.True(Versioning.IsPrerelease("1.2-beta.1"));Assert.False(Versioning.IsPrerelease("1.2+build"));Assert.True(Versioning.MajorChange("1.9","2.0"));}
}
public class IdentityTests
{
    [Fact]public void CatalogVersionMappingDoesNotCreateADuplicateInstallationOrAuthorizeAnUpdate(){var registry=Fixture.App() with{Provider="Registry",InstalledVersion="3.13.6150"};var catalog=registry with{Id="catalog",Provider="WinGet",InstalledVersion="3.13.6"};var resolved=Assert.Single(IdentityResolver.Resolve([registry,catalog]));Assert.Equal(UpdateState.Attention,resolved.State);Assert.Equal(2,resolved.InstalledVersionEvidence.Length);Assert.False(resolved.CanUpdate);}
    [Fact]public void VersionDisagreementNeverMergesDifferentInstallationPaths(){var registry=Fixture.App() with{Provider="Registry",InstalledVersion="3.13.6150"};var catalog=registry with{Id="catalog",Provider="WinGet",InstalledVersion="3.13.6",InstallLocation=registry.InstallLocation+"-other"};Assert.Equal(2,IdentityResolver.Resolve([registry,catalog]).Count);}
    [Fact]public void ProductCodeMergesRegistryAndManager(){var a=Fixture.App() with{Provider="Registry"};var b=a with{Id="other",Provider="WinGet",PackageId="Example.One"};var result=IdentityResolver.Resolve([a,b]);Assert.Single(result);Assert.Equal("WinGet",result[0].Provider);Assert.Equal(a.Id,result[0].Id);}
    [Fact]public void SimilarNamesNeverMerge(){var a=Fixture.App();var b=Fixture.App("two") with{Name=a.Name};Assert.Equal(2,IdentityResolver.Resolve([a,b]).Count);}
    [Theory][InlineData("scope")][InlineData("architecture")][InlineData("location")][InlineData("version")]
    public void PreservesDistinctInstallations(string difference){var a=Fixture.App();var b=a with{Id="two"};b=difference switch{"scope"=>b with{Scope="Machine"},"architecture"=>b with{Architecture="Arm64"},"location"=>b with{InstallLocation=b.InstallLocation+"-beta"},_=>b with{InstalledVersion="2.0"}};Assert.Equal(0,IdentityResolver.Confidence(a,b));Assert.Equal(2,IdentityResolver.Resolve([a,b]).Count);}
    [Fact]public void AmbiguousProductCodeBlocksAllMatchedInstances(){var a=Fixture.App("a") with{ProductCodes=["shared"],InstallLocation="",InstalledVersion="Unknown"};var b=Fixture.App("b") with{ProductCodes=["shared"],InstallLocation="",InstalledVersion="2.0"};var c=Fixture.App("c") with{ProductCodes=["shared"],InstallLocation="",InstalledVersion="Unknown",Architecture="Unknown"};var rows=IdentityResolver.Resolve([a,b,c]);Assert.All(rows,x=>Assert.True(x.Ambiguous));}
    [Fact]public void StoreOwnershipOutranksWinGet(){var a=Fixture.App() with{FamilyName="Example_family"};var b=a with{Id="store",Provider="Store",State=UpdateState.VendorManaged};Assert.Equal("Store",Assert.Single(IdentityResolver.Resolve([a,b])).Provider);}
}
public class PolicyTests
{
    [Theory]
    [InlineData(false,false,false,false,false)]
    [InlineData(false,false,true,false,false)]
    [InlineData(true,false,false,false,true)]
    [InlineData(false,true,false,false,true)]
    [InlineData(false,false,false,true,true)]
    [InlineData(true,false,true,false,true)]
    public void RestartSignalsDistinguishServicingFromQueuedFileOperations(bool servicing,bool updates,bool files,bool failed,bool blocked)
    {
        var restart=new RestartAssessment(servicing,updates,files,failed);
        var plan=UpdatePolicy.Plan([Fixture.App()],new(),DateTimeOffset.UtcNow,restart.BlocksInstallation,_=>false,long.MaxValue);
        Assert.Equal(blocked,restart.BlocksInstallation);Assert.Equal(blocked?0:1,plan.Automatic.Count);
        Assert.Equal(blocked||files,restart.RestartRecommended);
    }
    [Fact]public void UnknownAndVendorInventoryDoNotBecomeFailedUpdateCandidates()
    {
        var unknown=Fixture.App("unknown") with{State=UpdateState.Unknown};var vendor=Fixture.App("manager") with{State=UpdateState.VendorManaged,VendorId="native"};
        var plan=UpdatePolicy.Plan([unknown,vendor,Fixture.App("available")],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue);
        Assert.Single(plan.Automatic);Assert.Single(plan.Handoffs);Assert.Empty(plan.Deferred);
    }
    [Fact]public void EmptyQueueExplainsTheActualBlocker()
    {
        var plan=UpdatePolicy.Plan([Fixture.App()],new(),DateTimeOffset.UtcNow,true,_=>false,long.MaxValue);
        Assert.Contains("Restart Windows",UpdatePolicy.DeferredSummary(plan));Assert.Contains("1 update",UpdatePolicy.DeferredSummary(plan));
    }
    [Fact]public void SavedInstallationPauseAlsoStopsAutomaticPublisherHandoffs()
    {
        var managed=Fixture.App() with{VendorId="native",State=UpdateState.VendorManaged};
        var plan=UpdatePolicy.Plan([managed,Fixture.App("routine")],new(){PauseInstallations=true,AutomaticVendorHandoffs=true},DateTimeOffset.UtcNow,false,_=>false,long.MaxValue);
        Assert.Empty(plan.Automatic);Assert.Empty(plan.Handoffs);Assert.NotEmpty(plan.Deferred);
    }
    [Fact]public void GameManagersNeverOpenAutomaticallyThroughUpdateAll(){foreach(string id in new[]{"steam","epic","battlenet"}){var app=Fixture.App() with{VendorId=id,State=UpdateState.VendorManaged};Assert.Empty(UpdatePolicy.Plan([app],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue).Handoffs);}}
    private static string? Reason(Software a,Preferences? p=null,bool restart=false,bool running=false,long disk=10L*1024*1024*1024)=>UpdatePolicy.BlockReason(a,p??new(),DateTimeOffset.UtcNow,restart,running,disk);
    [Fact]public void RoutineVerifiedUpdateIsEligible()=>Assert.Null(Reason(Fixture.App()));
    [Theory][InlineData("ignored")][InlineData("ambiguous")][InlineData("pinned")][InlineData("pinUnknown")][InlineData("versionUnknown")][InlineData("downgrade")][InlineData("stale")][InlineData("channel")][InlineData("sensitive")]
    public void BlocksUnsafePlans(string kind){var a=Fixture.App();var p=new Preferences();switch(kind){case"ignored":p.Ignored.Add(a.Id);break;case"ambiguous":a=a with{Ambiguous=true};break;case"pinned":a=a with{Pinned=true};break;case"pinUnknown":a=a with{PinVerified=false};break;case"versionUnknown":a=a with{InstalledVersion="Unknown"};break;case"downgrade":a=a with{AvailableVersion="0.9"};break;case"stale":a=a with{CheckedAt=DateTimeOffset.UtcNow.AddHours(-1)};break;case"channel":a=a with{InstalledVersion="1.0-beta",AvailableVersion="1.1"};break;case"sensitive":a=a with{Name="Graphics Driver"};break;}Assert.NotNull(Reason(a,p));}
    [Fact]public void BlocksRebootRunningAndLowDisk(){Assert.NotNull(Reason(Fixture.App(),restart:true));Assert.NotNull(Reason(Fixture.App(),running:true));Assert.NotNull(Reason(Fixture.App(),disk:100));}
    [Fact]public void SystemSensitiveUpdatesAreDirectedOnlyToPublisherWorkflow()
    {
        var reason=Reason(Fixture.App() with{Name="Tailscale",PackageId=""})!;
        Assert.Contains("publisher workflow",reason);Assert.DoesNotContain("reviewed action",reason);
    }
    [Fact]public void CautionUpdatesExplainTheirIndividualReviewRoute()
    {
        var reason=Reason(Fixture.App() with{Name="Blender",PackageId=""})!;
        Assert.Contains("requires individual review",reason);Assert.Contains("reviewed action when available",reason);
    }
    [Fact]public void DeterministicPlanDeduplicatesOperationsAndManagers(){var a=Fixture.App("b");var b=Fixture.App("a");var c=Fixture.App("c") with{VendorId="adobe",State=UpdateState.VendorManaged};var d=c with{Id="d"};var plan=UpdatePolicy.Plan([a,b,a,c,d],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue);Assert.Equal(new[]{b.Id,a.Id},plan.Automatic.Select(x=>x.App.Id));Assert.Single(plan.Handoffs);}
    [Fact]public void DriverManagersExcludedFromAutomaticHandoffs(){var a=Fixture.App() with{VendorId="nvidia",State=UpdateState.VendorManaged,IsDriver=true};Assert.Empty(UpdatePolicy.Plan([a],new(),DateTimeOffset.UtcNow,false,_=>false,long.MaxValue).Handoffs);}
    [Theory][InlineData("BIOS Update",Risk.High)][InlineData("NVIDIA Driver",Risk.SystemSensitive)][InlineData("Blender",Risk.Caution)][InlineData("Python",Risk.Caution)][InlineData("Simple Editor",Risk.Routine)]
    public void ClassifiesConservativeRisk(string name,Risk expected)=>Assert.Equal(expected,UpdatePolicy.Classify(Fixture.App() with{Name=name,PackageId=""}));
    [Fact]public void PreferencesValidateMalformedConfiguration(){var p=new Preferences{Theme="garbage",Schedule="hourly",QuietStart=-1,QuietEnd=27,WindowWidth=double.NaN,WindowHeight=10,PortableDirectories=["relative"]};p.Validate();Assert.Equal("Dark",p.Theme);Assert.Equal("Manual",p.Schedule);Assert.Equal(0,p.QuietStart);Assert.Equal(23,p.QuietEnd);Assert.Equal(1340,p.WindowWidth);Assert.Equal(620,p.WindowHeight);Assert.Empty(p.PortableDirectories);}
    [Fact]public void ScheduleHonorsQuietHoursAndLastCheck(){var p=new Preferences{Schedule="Daily",QuietStart=22,QuietEnd=7};var noon=new DateTimeOffset(DateTime.Today.AddHours(12));Assert.True(UpdatePolicy.ScheduleDue(p,noon));Assert.False(UpdatePolicy.ScheduleDue(p,new DateTimeOffset(DateTime.Today.AddHours(23))));p.LastCheck=noon.AddHours(-1);Assert.False(UpdatePolicy.ScheduleDue(p,noon));}
}
public class StorageTests
{
    [Fact]public void RoundTripsInventoryPreferencesAndOperations(){var store=new StateStore(Fixture.Temp(),true);var a=Fixture.App();store.SaveInventory([a]);var p=new Preferences{Theme="Light",Tray=true};p.Ignored.Add(a.Id);store.SavePreferences(p);Assert.Equal(a.Id,Assert.Single(store.LoadInventory()).Id);Assert.True(store.LoadPreferences().Tray);Assert.Contains(a.Id,store.LoadPreferences().Ignored);var op=new UpdateOperation{Id="op",Name="fixture"};store.SaveOperation(op);store.SaveOperation(op with{State=OperationState.Verified});Assert.Equal(OperationState.Verified,Assert.Single(store.LoadOperations()).State);}
    [Fact]public void CorruptDatabasePreservedBeforeRecovery(){var path=Fixture.Temp();File.WriteAllText(Path.Combine(path,"updatedeck.db"),"corrupt fixture");var store=new StateStore(path);Assert.Contains("corrupt",store.StorageNotice);Assert.Single(Directory.GetFiles(path,"*.corrupt-*"));Assert.Empty(store.LoadInventory());}
    [Fact]public void PendingWorkReconcilesFreshInstallationSelectorsWithoutRunningInstallers(){var store=new StateStore(Fixture.Temp());var a=Fixture.App();store.SaveOperation(new(){AppId=a.Id,PreviousVersion=a.InstalledVersion,TargetVersion=a.AvailableVersion,Installation=InstallationSnapshot.Capture(a),State=OperationState.Pending});var changed=QueueEngine.Recover(store,[a with{InstalledVersion="1.1.0"}]);Assert.Equal(OperationState.Verified,Assert.Single(changed).State);}
    [Fact]public void UnverifiedInterruptedWorkNeverClaimsFailureOrSuccess(){var store=new StateStore(Fixture.Temp());store.SaveOperation(new(){AppId="missing",TargetVersion="2.0",State=OperationState.Pending});Assert.Equal(OperationState.Interrupted,Assert.Single(QueueEngine.Recover(store,[])).State);}
}
