using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace UpdateDeck.Core;

public sealed class StateStore : IOperationStore
{
    private readonly string database;
    private readonly object gate=new();
    private bool recoveredDatabase;
    public string DirectoryPath { get; }
    public string StorageNotice { get; private set; } = "";
    public bool Portable { get; }
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    public StateStore(string directory,bool portable=false,string notice="")
    {
        DirectoryPath=Path.GetFullPath(directory); Portable=portable; StorageNotice=notice;
        Directory.CreateDirectory(DirectoryPath); database=Path.Combine(DirectoryPath,"updatedeck.db");
        try { Initialize(); }
        catch(SqliteException ex) when(ex.SqliteErrorCode is 11 or 26)
        {
            File.Move(database,database+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            StorageNotice="The database was corrupt. Its original file was preserved beside a new database. Inspect saved policies before updating.";
            recoveredDatabase=true;
            Initialize();
            Write("preferences",new Preferences{PauseInstallations=true});
        }
    }
    public static StateStore OpenDefault(string baseDirectory,string? fallbackDirectory=null)
    {
        bool portable=File.Exists(Path.Combine(baseDirectory,"portable.mode"));
        string fallback=fallbackDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UpdateDeck");
        var wanted=portable?Path.Combine(baseDirectory,"Data"):fallback;
        try { return new StateStore(wanted,portable); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            if(!portable) throw;
            var alternate=new StateStore(fallback,true,"Portable storage is not writable. Settings and history are stored in LocalAppData\\UpdateDeck on this PC. Installations are paused: review the available policy and any unfinished installers from the original portable location before resuming.");
            // The unavailable store may contain different ignores or unresolved installer holds.
            // Never inherit permissive defaults (or a different local policy) across this boundary.
            var preferences=alternate.LoadPreferences();preferences.PauseInstallations=true;alternate.SavePreferences(preferences);
            return alternate;
        }
    }
    private SqliteConnection Open()
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=database,Pooling=false,DefaultTimeout=5}.ToString());
        try{c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText="PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;";cmd.ExecuteNonQuery();return c;}
        catch{c.Dispose();throw;}
    }
    private void Initialize()
    {
        using var c=Open(); using var check=c.CreateCommand(); check.CommandText="PRAGMA quick_check";
        if((string?)check.ExecuteScalar()!="ok") throw new SqliteException("Database integrity check failed",11);
        using var version=c.CreateCommand(); version.CommandText="PRAGMA user_version";
        long v=(long)(version.ExecuteScalar()??0L);
        if(v>1) throw new InvalidOperationException("This database was created by a newer UpdateDeck. Upgrade UpdateDeck to open it.");
        if(v==0)
        {
            if(new FileInfo(database).Length>4096) File.Copy(database,database+".schema-backup",true);
            using var tx=c.BeginTransaction(); using var cmd=c.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS operations (id TEXT PRIMARY KEY, started TEXT NOT NULL, json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_operations_started ON operations(started); PRAGMA user_version=1;";
            cmd.ExecuteNonQuery();tx.Commit();
        }
    }
    private T Read<T>(string key,T fallback)
    {
        lock(gate)
        {
            using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT json FROM state WHERE key=$key";cmd.Parameters.AddWithValue("$key",key);
            var json=(string?)cmd.ExecuteScalar(); if(json==null)return fallback;
            try {var value=JsonSerializer.Deserialize<T>(json,Json);if(value==null&&fallback is Preferences)throw new JsonException("Saved policy is null.");return value??fallback;}
            catch(JsonException)
            {
                PreserveUnreadable(c,key,json);
                StorageNotice=$"Saved {key} could not be read. Its original content was preserved in database recovery storage. Inspect your saved policy before updating.";
                if(fallback is Preferences preferences)preferences.PauseInstallations=true;
                return fallback;
            }
        }
    }
    private static void PreserveUnreadable(SqliteConnection connection,string key,string json)
    {
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        using var command=connection.CreateCommand();command.CommandText="INSERT OR IGNORE INTO state(key,json) VALUES($key,$json)";
        command.Parameters.AddWithValue("$key","recovery/"+key+"/"+hash);command.Parameters.AddWithValue("$json",json);command.ExecuteNonQuery();
    }
    private void Write<T>(string key,T value)
    {
        lock(gate)
        {
            using var c=Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO state(key,json) VALUES($key,$json) ON CONFLICT(key) DO UPDATE SET json=excluded.json";
            cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(value,Json));cmd.ExecuteNonQuery();tx.Commit();
        }
    }
    public Preferences LoadPreferences(){var p=Read("preferences",new Preferences{PauseInstallations=recoveredDatabase});p.Validate();return p;}
    public void SavePreferences(Preferences preferences){preferences.Validate();Write("preferences",preferences);}
    public IReadOnlyList<Software> LoadInventory()=>Read<Software[]>("inventory",[]).Where(x=>x!=null).Select(SoftwareValidation.Normalize).ToArray();
    public void SaveInventory(IReadOnlyList<Software> apps)=>Write("inventory",apps);
    public void SaveReports(IReadOnlyList<ProviderReport> reports)=>Write("providers",reports);
    public IReadOnlyList<ProviderReport> LoadReports()=>Read<ProviderReport[]>("providers",[]).Where(x=>x!=null).Select(x=>x with{Name=x.Name??"Unavailable source",Message=x.Message??"Source metadata is unavailable."}).ToArray();
    public MachineInfo? LoadMachine()=>Read<MachineInfo?>("machine",null);
    public void SaveMachine(MachineInfo machine)=>Write("machine",machine);
    public void SaveOperation(UpdateOperation operation)
    {
        lock(gate)
        {
            using var c=Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="""
                INSERT INTO operations(id,started,json) VALUES($id,$started,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json;
                DELETE FROM operations WHERE id IN (
                    SELECT id FROM operations
                    WHERE CASE WHEN json_valid(json) THEN
                        json_extract(json,'$.RequiresInspection')=0 AND
                        json_extract(json,'$.State') IN ('Verified','Failed','Deferred','InteractionRequired','Handoff','Cancelled','Observed')
                    ELSE 0 END
                    ORDER BY started DESC LIMIT -1 OFFSET 2000
                )
                """;
            cmd.Parameters.AddWithValue("$id",operation.Id);cmd.Parameters.AddWithValue("$started",operation.StartedAt.ToString("O"));cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(operation,Json));cmd.ExecuteNonQuery();tx.Commit();
        }
    }
    public IReadOnlyList<UpdateOperation> LoadOperations()
    {
        lock(gate)
        {
            using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,json FROM operations ORDER BY started DESC";using var reader=cmd.ExecuteReader();var list=new List<UpdateOperation>();var damaged=new List<(string Id,string Json)>();
            while(reader.Read()) {try{var op=JsonSerializer.Deserialize<UpdateOperation>(reader.GetString(1),Json);if(OperationValidation.Readable(op,reader.GetString(0)))list.Add(op!);else damaged.Add((reader.GetString(0),reader.GetString(1)));}catch(JsonException){damaged.Add((reader.GetString(0),reader.GetString(1)));}}
            reader.Close();
            foreach(var item in damaged)
            {
                bool safeId=OperationValidation.SafeStoredId(item.Id);
                string recoveredId=safeId?item.Id:"recovered-"+Software.Key(item.Id);
                PreserveUnreadable(c,"operation/"+(safeId?item.Id:"invalid-key-"+Software.Key(item.Id)),safeId?item.Json:JsonSerializer.Serialize(new{storedId=item.Id,json=item.Json},Json));
                var op=new UpdateOperation{Id=recoveredId,AppId="recovery:"+Software.Key(item.Id),Name="Unreadable update record",Provider="Recovery",State=OperationState.Interrupted,RequiresInspection=true,FinishedAt=DateTimeOffset.UtcNow,Message="The original history content was preserved in database recovery storage. Inspect any installers before clearing this monitoring hold; no operation was rerun."};
                SaveOperation(op);list.Add(op);StorageNotice="A damaged history record was preserved. Inspect the installation and resolve its monitoring notice in History.";
                if(!safeId){using var remove=c.CreateCommand();remove.CommandText="DELETE FROM operations WHERE id=$id";remove.Parameters.AddWithValue("$id",item.Id);remove.ExecuteNonQuery();}
            }
            return list;
        }
    }
    public void Log(string kind,string message)
    {
        try
        {
            lock(gate)
            {
                string path=Path.Combine(DirectoryPath,"diagnostics.jsonl");
                if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024) File.Move(path,path+".1",true);
                var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if(profile.Length>0)message=message.Replace(profile,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);
                File.AppendAllText(path,JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,kind,message})+Environment.NewLine);
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
