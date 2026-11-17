using Microsoft.Data.Sqlite;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class PinStorageTests
{
    private static string Database(string major="1",bool pinTable=true)
    {
        string path=Path.Combine(Fixture.Temp(),"pinning.db");
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText="CREATE TABLE metadata(name TEXT PRIMARY KEY,value TEXT);INSERT INTO metadata VALUES('majorVersion',$major),('minorVersion','0');"+(pinTable?"CREATE TABLE pin(package_id TEXT NOT NULL,source_id TEXT NOT NULL,type INTEGER NOT NULL,version TEXT NOT NULL);INSERT INTO pin VALUES('Example.App','winget',1,'');":"");command.Parameters.AddWithValue("$major",major);command.ExecuteNonQuery();return path;
    }
    [Fact]public void ExistingSupportedPinsAreReadOnlyAndProtectedFromDeletion()
    {
        string path=Database();byte[] before=File.ReadAllBytes(path);
        using(var guard=WinGetPinStorage.Acquire([path])){Assert.True(guard.Safe);Assert.True(guard.HasDatabases);Assert.Throws<IOException>(()=>File.Delete(path));Assert.Throws<IOException>(()=>{using var writer=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});}
        Assert.Equal(before,File.ReadAllBytes(path));using var writable=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.None);
    }
    [Theory][InlineData("future")][InlineData("corrupt")][InlineData("missing-table")][InlineData("directory")]
    public async Task UnreadablePinsStayUntouchedAndNoCliOrComProbeRuns(string kind)
    {
        string path=kind switch{"future"=>Database("2"),"missing-table"=>Database(pinTable:false),_=>Path.Combine(Fixture.Temp(),"pinning.db")};
        if(kind=="directory")Directory.CreateDirectory(path);else if(kind=="corrupt")File.WriteAllText(path,"unreadable pins must survive");
        byte[]? before=File.Exists(path)?File.ReadAllBytes(path):null;var runner=new RecordingRunner();
        var provider=new WinGetProvider(runner,()=>"fixture-winget",pinStorage:()=>WinGetPinStorage.Acquire([path]));
        Assert.False((await provider.ReadPinsAsync(CancellationToken.None)).Verified);
        var discovery=await provider.DiscoverAsync(true,CancellationToken.None);Assert.Empty(discovery.Apps);Assert.False(Assert.Single(discovery.Providers).Available);Assert.Empty(runner.Arguments);
        if(before!=null)Assert.Equal(before,File.ReadAllBytes(path));else Assert.True(Directory.Exists(path));
    }
    [Fact]public void NoPinDatabaseIsNeverCreatedByInspection(){string path=Path.Combine(Fixture.Temp(),"pinning.db");using var guard=WinGetPinStorage.Acquire([path]);Assert.True(guard.Safe);Assert.False(guard.HasDatabases);Assert.False(File.Exists(path));}
    [Fact]public void RenamedMicrosoftExecutableIsNotAWinGetCli(){string path=Path.Combine(Fixture.Temp(),"winget.exe");File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),path);Assert.False(WinGetCliLocator.IsMicrosoftExecutable(path));}
}
