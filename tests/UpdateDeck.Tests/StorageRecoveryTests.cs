using System.Text.Json;
using Microsoft.Data.Sqlite;
using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public sealed class StorageRecoveryTests
{
    [Fact]public void TruncatedOrNullIdentityArraysCannotAuthorizeAnUpdate()
    {
        foreach(var app in new[]{Fixture.App() with{ProductCodes=Enumerable.Repeat("code",129).ToArray()},Fixture.App() with{ProductCodes=["code",null!]}})
        {var normalized=SoftwareValidation.Normalize(app);Assert.True(normalized.Ambiguous);Assert.False(normalized.CanUpdate);Assert.Equal(UpdateState.Attention,normalized.State);}
    }
    [Fact]public void SavingSettingsCannotOverwriteLiveIgnorePolicyCheckTimeOrPlacement(){var now=DateTimeOffset.UtcNow;var active=new Preferences{LastCheck=now,WindowBounds=new(20,30,1200,800),Sort="Provider"};active.Ignored.Add("keep-ignored");var draft=new Preferences{Theme="Light",QuietStart=19,PortableDirectories=[@"C:\Portable"]};active.ApplyUserSettings(draft);Assert.Contains("keep-ignored",active.Ignored);Assert.Equal(now,active.LastCheck);Assert.Equal(new StoredWindowBounds(20,30,1200,800),active.WindowBounds);Assert.Equal("Provider",active.Sort);Assert.Equal("Light",active.Theme);draft.PortableDirectories.Clear();Assert.Single(active.PortableDirectories);}
    [Fact]public void UnwritablePortableStateFallsBackToAnExplicitWritableLocation(){string portable=Fixture.Temp(),fallback=Fixture.Temp();File.WriteAllText(Path.Combine(portable,"portable.mode"),"");File.WriteAllText(Path.Combine(portable,"Data"),"A file prevents creating the Data directory");var store=StateStore.OpenDefault(portable,fallback);Assert.True(store.Portable);Assert.Equal(fallback,store.DirectoryPath);Assert.Contains("not writable",store.StorageNotice);Assert.True(store.LoadPreferences().PauseInstallations);store.SavePreferences(new(){Theme="Light"});Assert.Equal("Light",store.LoadPreferences().Theme);Assert.Equal("A file prevents creating the Data directory",File.ReadAllText(Path.Combine(portable,"Data")));}
    [Fact]public void PortableFallbackPreservesLocalPolicyAndHistoryButRequiresANewReview()
    {
        string portable=Fixture.Temp(),fallback=Fixture.Temp();File.WriteAllText(Path.Combine(portable,"portable.mode"),"");File.WriteAllText(Path.Combine(portable,"Data"),"Owned unavailable-store fixture");
        var existing=new StateStore(fallback);var preferences=new Preferences{Theme="Light",PauseInstallations=false};preferences.Ignored.Add("local-ignore");existing.SavePreferences(preferences);existing.SaveOperation(new(){Id="existing-hold",State=OperationState.AwaitingVerification,RequiresInspection=true});
        var alternate=StateStore.OpenDefault(portable,fallback);var loaded=alternate.LoadPreferences();Assert.True(loaded.PauseInstallations);Assert.Equal("Light",loaded.Theme);Assert.Contains("local-ignore",loaded.Ignored);Assert.True(Assert.Single(alternate.LoadOperations()).RequiresInspection);Assert.Contains("unfinished installers",alternate.StorageNotice);
        loaded.PauseInstallations=false;alternate.SavePreferences(loaded);Assert.True(StateStore.OpenDefault(portable,fallback).LoadPreferences().PauseInstallations);Assert.Equal("Owned unavailable-store fixture",File.ReadAllText(Path.Combine(portable,"Data")));
    }
    [Fact]public void PortableFallbackCannotAuthorizeAutomaticWorkOrPublisherHandoffs()
    {
        string portable=Fixture.Temp(),fallback=Fixture.Temp();File.WriteAllText(Path.Combine(portable,"portable.mode"),"");File.WriteAllText(Path.Combine(portable,"Data"),"Owned unavailable-store fixture");
        var policy=StateStore.OpenDefault(portable,fallback).LoadPreferences();var app=Fixture.App();var manager=app with{Id="publisher",VendorId="adobe",State=UpdateState.VendorManaged};
        var plan=UpdatePolicy.Plan([app,manager],policy,DateTimeOffset.UtcNow,false,_=>false,long.MaxValue);Assert.Empty(plan.Automatic);Assert.Empty(plan.Handoffs);Assert.Contains(plan.Deferred,x=>x.App.Id==app.Id&&x.Reason.Contains("paused",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]public async Task LostStorageStopsAQueueBeforeAnyChildProcessCanStart(){string directory=Fixture.Temp();var store=new StateStore(directory);File.Move(Path.Combine(directory,"updatedeck.db"),Path.Combine(directory,"updatedeck.db.offline"));File.WriteAllText(Path.Combine(directory,"updatedeck.db"),"Simulated unreadable storage");var executor=new SimulatedExecutor();var queue=new QueueEngine(executor,store,new SimulatedVendor());await Assert.ThrowsAsync<SqliteException>(()=>queue.RunAsync(Fixture.Plan(Fixture.App()),false,new ImmediateProgress<QueueProgress>(_=>{}),_=>null));Assert.Empty(executor.Calls);Assert.True(File.Exists(Path.Combine(directory,"updatedeck.db.offline")));}
    private static void Sql(string directory,string text,params (string,object)[] parameters)
    {using var connection=new SqliteConnection("Data Source="+Path.Combine(directory,"updatedeck.db"));connection.Open();using var command=connection.CreateCommand();command.CommandText=text;foreach(var (name,value) in parameters)command.Parameters.AddWithValue(name,value);command.ExecuteNonQuery();}
    private static string Scalar(string directory,string text)
    {using var connection=new SqliteConnection("Data Source="+Path.Combine(directory,"updatedeck.db"));connection.Open();using var command=connection.CreateCommand();command.CommandText=text;return Convert.ToString(command.ExecuteScalar())??"";}
    [Fact]public void LegacyNumericEnumsRemainReadableAfterStringEnumMigration()
    {
        string directory=Fixture.Temp();var store=new StateStore(directory);var legacy=Fixture.App();var operation=new UpdateOperation{Id="legacy",State=OperationState.Interrupted};
        Sql(directory,"INSERT INTO state(key,json) VALUES('inventory',$json)",("$json",JsonSerializer.Serialize(new[]{legacy})));
        Sql(directory,"INSERT INTO operations(id,started,json) VALUES('legacy','2026-01-01',$json)",("$json",JsonSerializer.Serialize(operation)));
        Assert.Equal(UpdateState.Available,Assert.Single(store.LoadInventory()).State);Assert.Equal(OperationState.Interrupted,Assert.Single(store.LoadOperations()).State);
    }
    [Fact]public void MalformedPolicyIsBackedUpBeforeDefaultsCanBeSaved()
    {
        string directory=Fixture.Temp();var store=new StateStore(directory);const string original="{broken policy fixture";
        Sql(directory,"INSERT INTO state(key,json) VALUES('preferences',$json)",("$json",original));
        var preferences=store.LoadPreferences();Assert.True(preferences.PauseInstallations);store.SavePreferences(preferences);
        Assert.Equal(original,Scalar(directory,"SELECT json FROM state WHERE key LIKE 'recovery/preferences/%'"));Assert.True(store.LoadPreferences().PauseInstallations);
    }
    [Fact]public void CorruptDatabaseRecoveryPausesInstallationsUntilPolicyReview(){string directory=Fixture.Temp();File.WriteAllText(Path.Combine(directory,"updatedeck.db"),"corrupt fixture");var store=new StateStore(directory);Assert.True(store.LoadPreferences().PauseInstallations);Assert.True(new StateStore(directory).LoadPreferences().PauseInstallations);Assert.NotNull(UpdatePolicy.BlockReason(Fixture.App(),store.LoadPreferences(),DateTimeOffset.UtcNow,false,false,long.MaxValue));}
    [Theory][InlineData("unknown-state")][InlineData("missing-state")][InlineData("different-id")][InlineData("null-message")][InlineData("invalid-pid")]
    public void StructurallyCorruptHistoryCannotSilentlyLoseAnInstallationHold(string kind)
    {
        string directory=Fixture.Temp();var store=new StateStore(directory);var operation=new UpdateOperation{Id="held",State=OperationState.AwaitingVerification};
        string raw=kind switch
        {
            "unknown-state"=>JsonSerializer.Serialize(operation with{State=(OperationState)999},StateStore.Json),
            "missing-state"=>"{\"Id\":\"held\",\"Message\":\"state was lost\"}",
            "different-id"=>JsonSerializer.Serialize(operation with{Id="another"},StateStore.Json),
            "null-message"=>JsonSerializer.Serialize(operation with{Message=null!},StateStore.Json),
            _=>JsonSerializer.Serialize(operation with{ActiveProcessId=-1},StateStore.Json)
        };
        Sql(directory,"INSERT INTO operations(id,started,json) VALUES('held','2026-01-01',$json)",("$json",raw));
        var recovered=Assert.Single(store.LoadOperations());Assert.Equal("held",recovered.Id);Assert.True(recovered.RequiresInspection);Assert.Equal(OperationState.Interrupted,recovered.State);
        Assert.Equal(raw,Scalar(directory,"SELECT json FROM state WHERE key LIKE 'recovery/operation/held/%'"));Assert.True(Assert.Single(new StateStore(directory).LoadOperations()).RequiresInspection);
    }
    [Fact]public void AnInvalidDatabaseHistoryKeyIsReplacedWithAReadableHeldRecord()
    {
        string directory=Fixture.Temp();var store=new StateStore(directory);string badId=new('x',300);string raw=JsonSerializer.Serialize(new UpdateOperation{Id=badId},StateStore.Json);
        Sql(directory,"INSERT INTO operations(id,started,json) VALUES($id,'2026-01-01',$json)",("$id",badId),("$json",raw));
        var first=Assert.Single(store.LoadOperations());Assert.True(first.RequiresInspection);Assert.True(OperationValidation.SafeStoredId(first.Id));Assert.NotEqual(badId,first.Id);
        Assert.Equal(first.Id,Assert.Single(new StateStore(directory).LoadOperations()).Id);
        using var preserved=JsonDocument.Parse(Scalar(directory,"SELECT json FROM state WHERE key LIKE 'recovery/operation/invalid-key-%'"));Assert.Equal(raw,preserved.RootElement.GetProperty("json").GetString());Assert.Equal(badId,preserved.RootElement.GetProperty("storedId").GetString());
    }
    [Fact]public void NullPolicyIsPreservedAndPausesInstallations(){string directory=Fixture.Temp();var store=new StateStore(directory);Sql(directory,"INSERT INTO state(key,json) VALUES('preferences','null')");Assert.True(store.LoadPreferences().PauseInstallations);Assert.Equal("null",Scalar(directory,"SELECT json FROM state WHERE key LIKE 'recovery/preferences/%'"));}
    [Fact]public void MalformedHistoryCannotLoseAnActiveInstallerHold()
    {
        string directory=Fixture.Temp();var store=new StateStore(directory);const string raw="{unreadable operation";
        Sql(directory,"INSERT INTO operations(id,started,json) VALUES('broken','2026-01-01',$json)",("$json",raw));
        var operation=Assert.Single(store.LoadOperations());Assert.True(operation.RequiresInspection);Assert.Equal(OperationState.Interrupted,operation.State);Assert.Equal(raw,Scalar(directory,"SELECT json FROM state WHERE key LIKE 'recovery/operation/broken/%'"));
        Assert.True(Assert.Single(store.LoadOperations()).RequiresInspection);
    }
    [Fact]public void FutureSchemaIsNeverOverwrittenOrCalledCorrupt(){string directory=Fixture.Temp();_ =new StateStore(directory);Sql(directory,"PRAGMA user_version=99");Assert.Throws<InvalidOperationException>(()=>new StateStore(directory));Assert.Equal("99",Scalar(directory,"PRAGMA user_version"));Assert.Empty(Directory.GetFiles(directory,"*.corrupt-*"));}
    [Fact]public void MalformedIgnoredPolicyCannotBecomePermissive(){var p=new Preferences{Ignored=null!};p.Validate();Assert.True(p.PauseInstallations);}
    [Fact]public void MalformedProviderMetadataDoesNotBreakUnrelatedInventory(){string directory=Fixture.Temp();var store=new StateStore(directory);Sql(directory,"INSERT INTO state(key,json) VALUES('providers','[null,{\"Name\":null,\"Available\":false,\"Capabilities\":0,\"Message\":null,\"CheckedAt\":\"2026-01-01T00:00:00Z\"}]')");var provider=Assert.Single(store.LoadReports());Assert.NotNull(provider.Name);Assert.NotNull(provider.Message);}
}
