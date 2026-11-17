using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class HistoryReconciliationTests
{
    private static UpdateOperation Operation(Software app)=>new(){AppId=app.Id,Name=app.Name,Provider=app.Provider,PreviousVersion=app.InstalledVersion,TargetVersion=app.AvailableVersion,State=OperationState.AwaitingVerification,Installation=InstallationSnapshot.Capture(app)};
    [Fact]public void LaterFreshInventoryResolvesEndedWorkWithoutExecutingAnything()
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());store.SaveOperation(Operation(app));
        Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[app],DateTimeOffset.UtcNow,false));
        Assert.Equal(1,QueueEngine.ReconcileInstalledVersions(store,[app with{InstalledVersion=app.AvailableVersion}],DateTimeOffset.UtcNow,false));
        var result=Assert.Single(store.LoadOperations());Assert.Equal(OperationState.Verified,result.State);Assert.Contains("No installer was rerun",result.Message);
        Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[app with{InstalledVersion=app.AvailableVersion}],DateTimeOffset.UtcNow,false));
    }
    [Theory][InlineData("provider")][InlineData("publisher")][InlineData("scope")][InlineData("architecture")][InlineData("path")][InlineData("package")][InlineData("channel")][InlineData("ambiguous")][InlineData("version-conflict")][InlineData("version-conflict-flag")][InlineData("stale")][InlineData("future")][InlineData("unobserved")]
    public void AReusedInventoryIdCannotVerifyAnotherOrUncertainInstallation(string field)
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());store.SaveOperation(Operation(app));var updated=app with{InstalledVersion=app.AvailableVersion};
        updated=field switch
        {
            "provider"=>updated with{Provider="Scoop"},"publisher"=>updated with{Publisher="Other publisher"},"scope"=>updated with{Scope="Machine"},
            "architecture"=>updated with{Architecture="Unknown"},"path"=>updated with{InstallLocation=app.InstallLocation+"-other"},"package"=>updated with{PackageId="Other.Package"},
            "channel"=>updated with{InstalledChannel="beta"},"ambiguous"=>updated with{Ambiguous=true},"version-conflict"=>updated with{State=UpdateState.Attention},
            "version-conflict-flag"=>updated with{VersionEvidenceConflict=true},
            "stale"=>updated with{InventoryObservedAt=DateTimeOffset.UtcNow.AddHours(-1)},"future"=>updated with{InventoryObservedAt=DateTimeOffset.UtcNow.AddHours(1)},_=>updated with{InventoryObservedAt=null}
        };
        Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[updated],DateTimeOffset.UtcNow,false));Assert.Equal(OperationState.AwaitingVerification,Assert.Single(store.LoadOperations()).State);
    }
    [Fact]public void DuplicateFreshMatchesCannotVerifyHistory(){var app=Fixture.App();var store=new StateStore(Fixture.Temp());store.SaveOperation(Operation(app));var updated=app with{InstalledVersion=app.AvailableVersion};Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[updated,updated],DateTimeOffset.UtcNow,false));}
    [Fact]public void InspectionHoldWinsOverAnObservedTargetVersion()
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());var operation=Operation(app) with{RequiresInspection=true};store.SaveOperation(operation);
        Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[app with{InstalledVersion=app.AvailableVersion}],DateTimeOffset.UtcNow,false));Assert.True(Assert.Single(store.LoadOperations()).RequiresInspection);
        store.SaveOperation(operation with{RequiresInspection=false});Assert.Equal(1,QueueEngine.ReconcileInstalledVersions(store,[app with{InstalledVersion=app.AvailableVersion}],DateTimeOffset.UtcNow,false));
    }
    [Fact]public void RestartRequiredWorkWaitsForTheRestartBoundaryToClear()
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());store.SaveOperation(Operation(app) with{RebootRequired=true});var updated=app with{InstalledVersion=app.AvailableVersion};
        Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[updated],DateTimeOffset.UtcNow,true));Assert.Equal(1,QueueEngine.ReconcileInstalledVersions(store,[updated],DateTimeOffset.UtcNow,false));Assert.True(Assert.Single(store.LoadOperations()).RebootRequired);
    }
    [Theory][InlineData(OperationState.Running)][InlineData(OperationState.Verifying)]
    public void ActiveAtCrashKeepsItsHoldEvenWhenTheInstalledVersionAlreadyChanged(OperationState state)
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());store.SaveOperation(Operation(app) with{State=state});var result=Assert.Single(QueueEngine.Recover(store,[app with{InstalledVersion=app.AvailableVersion}]));
        Assert.Equal(OperationState.AwaitingVerification,result.State);Assert.True(result.RequiresInspection);
    }
    [Fact]public void OldHistoryWithoutSelectorsNeverClaimsAutomaticReconciliation()
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());store.SaveOperation(Operation(app) with{Installation=null});Assert.Equal(0,QueueEngine.ReconcileInstalledVersions(store,[app with{InstalledVersion=app.AvailableVersion}],DateTimeOffset.UtcNow,false));
    }
    [Fact]public async Task QueuePersistsOriginalSelectorsBeforeAnExecutorCanRun()
    {
        var app=Fixture.App();var store=new StateStore(Fixture.Temp());var executor=new SimulatedExecutor{BeforeExecute=_=>{var operation=Assert.Single(store.LoadOperations());Assert.NotNull(operation.Installation);Assert.True(operation.Installation.Matches(app));return Task.CompletedTask;}};
        await new QueueEngine(executor,store,new SimulatedVendor()).RunAsync(Fixture.Plan(app),false,new ImmediateProgress<QueueProgress>(_=>{}),_=>null);
    }
}
