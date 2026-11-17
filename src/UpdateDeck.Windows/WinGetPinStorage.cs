using Microsoft.Data.Sqlite;

namespace UpdateDeck.Windows;

/// <summary>Protects existing pin storage before invoking APIs that may repair an unreadable index. Never writes or repairs WinGet data.</summary>
public sealed class WinGetPinStorage : IDisposable
{
    private readonly List<FileStream> leases=[];
    public bool Safe {get;private set;}=true;
    public bool HasDatabases=>leases.Count>0;
    public static WinGetPinStorage Acquire(IEnumerable<string>? paths=null)
    {
        var guard=new WinGetPinStorage();
        try
        {
            foreach(var path in (paths??DefaultPaths()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                FileAttributes attributes;
                try{attributes=File.GetAttributes(path);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
                if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0){guard.Safe=false;break;}
                var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);guard.leases.Add(lease);
                if(lease.Length is <512 or >33554432){guard.Safe=false;break;}
                using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=2}.ToString());connection.Open();
                using var command=connection.CreateCommand();command.CommandText="PRAGMA quick_check";
                if((string?)command.ExecuteScalar()!="ok"){guard.Safe=false;break;}
                command.CommandText="SELECT value FROM metadata WHERE name='majorVersion'";string? major=Convert.ToString(command.ExecuteScalar());
                command.CommandText="SELECT value FROM metadata WHERE name='minorVersion'";string? minor=Convert.ToString(command.ExecuteScalar());
                if(major!="1"||minor!="0"){guard.Safe=false;break;}
                command.CommandText="SELECT package_id,source_id,type,version FROM pin LIMIT 10001";
                using var rows=command.ExecuteReader();int count=0;
                while(rows.Read())if(++count>10000||rows.IsDBNull(0)||rows.IsDBNull(1)||rows.IsDBNull(2)||rows.IsDBNull(3)){guard.Safe=false;break;}
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException){guard.Safe=false;}
        return guard;
    }
    private static IEnumerable<string> DefaultPaths()
    {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),packages=Path.Combine(local,"Packages"),state=Path.Combine(local,"Microsoft","WinGet","State");
        yield return Path.Combine(packages,"Microsoft.DesktopAppInstaller_8wekyb3d8bbwe","LocalState","pinning.db");
        if(Directory.Exists(packages))foreach(var folder in BoundedDirectories(packages,"Microsoft.DesktopAppInstaller*_8wekyb3d8bbwe"))yield return Path.Combine(folder,"LocalState","pinning.db");
        yield return Path.Combine(state,"defaultState","pinning.db");
        if(Directory.Exists(state))foreach(var folder in BoundedDirectories(state,"*"))yield return Path.Combine(folder,"pinning.db");
    }
    private static IEnumerable<string> BoundedDirectories(string path,string pattern){int count=0;foreach(var folder in Directory.EnumerateDirectories(path,pattern)){if(++count>64)throw new IOException("Too many WinGet state locations to verify safely.");yield return folder;}}
    public void Dispose(){foreach(var lease in leases)lease.Dispose();leases.Clear();}
}
