using System.Diagnostics;
using System.Text;

namespace UpdateDeck.Windows;

public enum ProcessPurpose { ReadOnly, Installer }
public sealed record ProcessResult(int ExitCode,string Output,string Error,bool TimedOut=false,int? ProcessId=null,long? StartedUtcTicks=null,bool OutputIncomplete=false,bool OutputTruncated=false,bool WasCancelled=false)
{
    [System.Text.Json.Serialization.JsonIgnore]public Task? MonitoringCompletion {get;init;}
}
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string executable,IEnumerable<string> arguments,TimeSpan timeout,CancellationToken cancellation,ProcessPurpose purpose);
}

/// <summary>Bounded output and monitoring. Installer intent, rather than timeout length, determines whether termination is allowed.</summary>
public sealed class ProcessRunner(TimeSpan? outputDrainTimeout=null) : IProcessRunner
{
    private readonly TimeSpan drainTimeout=outputDrainTimeout??TimeSpan.FromSeconds(2);
    public async Task<ProcessResult> RunAsync(string executable,IEnumerable<string> arguments,TimeSpan timeout,CancellationToken cancellation,ProcessPurpose purpose)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!Enum.IsDefined(purpose))throw new ArgumentOutOfRangeException(nameof(purpose));
        if(!Path.IsPathFullyQualified(executable))throw new ArgumentException("A provider executable must have an absolute path.",nameof(executable));
        if(timeout<=TimeSpan.Zero||drainTimeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(timeout));
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        var process=new Process{StartInfo=info};
        var stdout=new OutputCapture();var stderr=new OutputCapture();
        var reading=new CancellationTokenSource();
        bool detached=false;
        try
        {
            if(!process.Start())throw new IOException("Windows could not start the provider process.");
            var captured=Task.WhenAll(stdout.ReadAsync(process.StandardOutput,reading.Token),stderr.ReadAsync(process.StandardError,reading.Token));
            using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch(OperationCanceledException)
            {
                bool cancelled=cancellation.IsCancellationRequested;
                if(purpose==ProcessPurpose.Installer)
                {
                    long? started=Started(process);int id=process.Id;
                    // Keep draining in the background. Closing inherited pipes could disturb the installer or its children.
                    detached=true;_ = DrainDetachedAsync(process,captured,reading);
                    return new(-1,stdout.Text,"Installer monitoring ended; the process was not terminated. Inspect it before retrying.",true,id,started,OutputIncomplete:true,OutputTruncated:stdout.Truncated||stderr.Truncated,WasCancelled:cancelled);
                }
                string reason=cancelled?"The read-only provider query was cancelled.":"The read-only provider query timed out.";
                try{if(!process.HasExited)process.Kill(entireProcessTree:true);}
                catch(Exception ex)when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){reason+=" Windows could not confirm query termination.";}
                await WaitForCaptureAsync(captured);
                return new(-1,stdout.Text,reason,true,OutputIncomplete:!captured.IsCompletedSuccessfully||stdout.Incomplete||stderr.Incomplete,OutputTruncated:stdout.Truncated||stderr.Truncated,WasCancelled:cancelled);
            }
            int exitCode=process.ExitCode;
            bool complete=await WaitForCaptureAsync(captured);
            if(!complete&&purpose==ProcessPurpose.Installer)
            {
                // Parent exit is not proof that a child installer holding inherited handles has finished.
                detached=true;_ = DrainDetachedAsync(process,captured,reading);
                return new(exitCode,stdout.Text,"The command ended but its output pipes remain open. Inspect any child installer before retrying.",OutputIncomplete:true,OutputTruncated:stdout.Truncated||stderr.Truncated);
            }
            return new(exitCode,stdout.Text,stderr.Text,OutputIncomplete:!complete||stdout.Incomplete||stderr.Incomplete,OutputTruncated:stdout.Truncated||stderr.Truncated);
        }
        finally
        {
            if(!detached){reading.Cancel();process.Dispose();reading.Dispose();}
        }
    }
    private async Task<bool> WaitForCaptureAsync(Task captured)
    {
        try{await captured.WaitAsync(drainTimeout);return true;}
        catch(TimeoutException){_ = captured.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);return false;}
    }
    private static async Task DrainDetachedAsync(Process process,Task captured,CancellationTokenSource reading)
    {
        try{await process.WaitForExitAsync();await captured;}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException){}
        finally{reading.Cancel();process.Dispose();reading.Dispose();}
    }
    private static long? Started(Process process)
    {try{return process.StartTime.ToUniversalTime().Ticks;}catch(Exception ex)when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){return null;}}
    private sealed class OutputCapture
    {
        private readonly object gate=new();
        private readonly StringBuilder text=new();
        private bool truncated,incomplete;
        public string Text{get{lock(gate)return text.ToString();}}
        public bool Truncated{get{lock(gate)return truncated;}}
        public bool Incomplete{get{lock(gate)return incomplete;}}
        public async Task ReadAsync(StreamReader reader,CancellationToken cancellation)
        {
            var buffer=new char[4096];
            try
            {
                int count;
                while((count=await reader.ReadAsync(buffer.AsMemory(),cancellation))>0)
                    lock(gate){text.Append(buffer,0,count);if(text.Length>32768){text.Remove(0,text.Length-32768);truncated=true;}}
            }
            catch(Exception ex)when(ex is IOException or ObjectDisposedException or OperationCanceledException){lock(gate)incomplete=true;}
        }
    }
    public static string? FindExecutable(string name)
    {
        if(name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return null;
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {try{var file=Path.GetFullPath(Path.Combine(directory.Trim('"'),name));if(File.Exists(file))return file;}catch(ArgumentException){}catch(NotSupportedException){}}
        return null;
    }
    public static string PowerShell=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
}
