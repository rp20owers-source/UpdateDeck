using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public sealed record ObservedProcess(string Name,string? Executable);
public enum RunningState { NotRunning, Running, Unknown }

/// <summary>A single read-only process census per plan. Limited-query handles avoid enumerating loaded modules.</summary>
public sealed class RunningApplications(IReadOnlyList<ObservedProcess> processes)
{
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int processId);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder name,ref int size);
    public static RunningApplications Capture()
    {
        var observed=new List<ObservedProcess>();
        foreach(var process in Process.GetProcesses())
        {
            using(process)
            {
                if(process.Id==Environment.ProcessId)continue;
                string name="";string? path=null;
                try
                {
                    name=process.ProcessName;
                    using var handle=OpenProcess(0x1000,false,process.Id);
                    if(!handle.IsInvalid){var text=new StringBuilder(32768);int length=text.Capacity;if(QueryFullProcessImageName(handle,0,text,ref length))path=text.ToString();}
                }
                catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException){}
                observed.Add(new(name,path));
            }
        }
        return new(observed);
    }
    public RunningState StateFor(Software app)
    {
        if(string.IsNullOrWhiteSpace(app.InstallLocation)||!Path.IsPathFullyQualified(app.InstallLocation))return RunningState.Unknown;
        string root;
        try{root=Path.GetFullPath(app.InstallLocation).TrimEnd('\\','/')+Path.DirectorySeparatorChar;}
        catch(ArgumentException){return RunningState.Unknown;}catch(NotSupportedException){return RunningState.Unknown;}
        var executable=app.PrimaryExecutable.Length>0?app.PrimaryExecutable:ExecutableMetadata.LocalIconExecutable(app.IconPath);
        var primaryName=executable==null?null:Path.GetFileNameWithoutExtension(executable);
        bool uncertain=false;
        foreach(var process in processes)
        {
            if(process.Executable!=null&&process.Executable.StartsWith(root,StringComparison.OrdinalIgnoreCase))return RunningState.Running;
            if(process.Executable==null&&primaryName!=null&&primaryName.Equals(process.Name,StringComparison.OrdinalIgnoreCase))uncertain=true;
        }
        return uncertain?RunningState.Unknown:RunningState.NotRunning;
    }
    public bool UnsafeToInstall(Software app)=>StateFor(app)!=RunningState.NotRunning;
}
