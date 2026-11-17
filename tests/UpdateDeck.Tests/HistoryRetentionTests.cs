using System.Text.Json;
using Microsoft.Data.Sqlite;
using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class HistoryRetentionTests
{
    private static void Seed(StateStore store,OperationState state,int count)
    {
        using var connection=new SqliteConnection("Data Source="+Path.Combine(store.DirectoryPath,"updatedeck.db"));connection.Open();using var transaction=connection.BeginTransaction();
        using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO operations(id,started,json) VALUES($id,$started,$json)";
        var id=command.Parameters.Add("$id",SqliteType.Text);var started=command.Parameters.Add("$started",SqliteType.Text);var json=command.Parameters.Add("$json",SqliteType.Text);
        for(int i=0;i<count;i++)
        {var operation=new UpdateOperation{Id="seed-"+i,Name="Owned retention fixture",State=state,StartedAt=DateTimeOffset.UnixEpoch.AddSeconds(i)};id.Value=operation.Id;started.Value=operation.StartedAt.ToString("O");json.Value=JsonSerializer.Serialize(operation,StateStore.Json);command.ExecuteNonQuery();}
        transaction.Commit();
    }
    [Fact]public void PendingQueuesLargerThanTheFinishedHistoryLimitStayDurable()
    {
        var store=new StateStore(Fixture.Temp());Seed(store,OperationState.Pending,2100);store.SaveOperation(new(){Id="seed-0",Name="First installer",State=OperationState.Running,RequiresInspection=true});
        var history=store.LoadOperations();Assert.Equal(2100,history.Count);Assert.Equal(OperationState.Running,history.Single(x=>x.Id=="seed-0").State);Assert.True(history.Single(x=>x.Id=="seed-0").RequiresInspection);
    }
    [Fact]public void OnlyFinishedHistoryIsPrunedAndOlderInspectionHoldsAreRetained()
    {
        var store=new StateStore(Fixture.Temp());Seed(store,OperationState.Verified,2100);
        store.SaveOperation(new(){Id="old-hold",StartedAt=DateTimeOffset.UnixEpoch.AddDays(-1),State=OperationState.Failed,RequiresInspection=true});
        store.SaveOperation(new(){Id="unverified",StartedAt=DateTimeOffset.UnixEpoch.AddDays(-2),State=OperationState.AwaitingVerification});
        store.SaveOperation(new(){Id="latest",State=OperationState.Observed});
        var history=store.LoadOperations();Assert.Equal(2002,history.Count);Assert.Contains(history,x=>x.Id=="old-hold"&&x.RequiresInspection);Assert.Contains(history,x=>x.Id=="unverified");Assert.DoesNotContain(history,x=>x.Id=="seed-0");
    }
    [Fact]public void MalformedHistoricalJsonCannotBreakSafeOperationPersistence()
    {
        var store=new StateStore(Fixture.Temp());using(var connection=new SqliteConnection("Data Source="+Path.Combine(store.DirectoryPath,"updatedeck.db")))
        {connection.Open();using var command=connection.CreateCommand();command.CommandText="INSERT INTO operations(id,started,json) VALUES('bad','2000-01-01','{broken')";command.ExecuteNonQuery();}
        store.SaveOperation(new(){Id="new",State=OperationState.Pending});var history=store.LoadOperations();Assert.Equal(2,history.Count);Assert.Contains(history,x=>x.Id=="bad"&&x.RequiresInspection);Assert.Contains(history,x=>x.Id=="new");
    }
}
