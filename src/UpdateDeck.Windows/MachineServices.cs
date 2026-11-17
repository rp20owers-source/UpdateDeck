using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public sealed class MachineServices(IProcessRunner process)
{
    public async Task<MachineInfo> InspectAsync(CancellationToken cancellation)
    {
        string cpu="Processor details unavailable";var graphics=new List<string>();IReadOnlyList<HardwareSection> hardware=[];
        try
        {
            using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");cpu=key?.GetValue("ProcessorNameString")?.ToString()?.Trim()??cpu;
            var result=await process.RunAsync(ProcessRunner.PowerShell,["-NoProfile","-NonInteractive","-Command",HardwareInventory.Script],TimeSpan.FromSeconds(35),cancellation,ProcessPurpose.ReadOnly);
            if(result.ExitCode==0&&!result.TimedOut&&!result.OutputIncomplete&&!result.OutputTruncated)
            {
                hardware=HardwareInventory.Parse(result.Output);
                foreach(var gpu in hardware.Where(x=>x.Name=="GPU"))graphics.Add(gpu.Summary+" · driver "+gpu.Fields.FirstOrDefault(x=>x.Label=="Driver")?.Value);
                cpu=hardware.FirstOrDefault(x=>x.Name=="CPU")?.Summary??cpu;
            }
            else hardware=[new("Hardware inventory unavailable","Windows did not complete the hardware query",[new("Status",result.TimedOut?"Query timed out. Scan again to retry.":"Hardware reporting may be restricted on this PC. Scan again to retry.")])];
        }catch(Exception ex)when(ex is not OperationCanceledException){}
        var restart=InspectRestart();
        return new(RuntimeInformation.OSDescription,RuntimeInformation.OSArchitecture.ToString(),cpu,graphics.ToArray(),restart.RestartRecommended){InstallationRestartRequired=restart.BlocksInstallation,RestartNotice=restart.Message,Hardware=hardware};
    }
    public static bool PendingRestart()=>InspectRestart().RestartRecommended;
    public static bool InstallationRestartRequired()=>InspectRestart().BlocksInstallation;
    public static RestartAssessment InspectRestart()
    {
        try
        {
            using var a=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var b=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var c=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return new(a!=null,b!=null,c?.GetValue("PendingFileRenameOperations") is string[]{Length:>0});
        }catch{return new(false,false,false,true);}
    }
    public static long FreeSpace(Software? app=null)
    {
        try{long system=new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!).AvailableFreeSpace;return app?.InstallLocation is {Length:>0} folder?Math.Min(system,new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace):system;}catch{return 0;}
    }
    public static bool Running(Software app)
    {
        return RunningApplications.Capture().UnsafeToInstall(app);
    }
    public static bool? InstallerStillRunning(UpdateOperation operation)
    {
        if(!operation.ActiveProcessId.HasValue||!operation.ActiveProcessStartedUtcTicks.HasValue)return null;
        try{using var process=Process.GetProcessById(operation.ActiveProcessId.Value);return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==operation.ActiveProcessStartedUtcTicks.Value;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return null;}
    }
    public static bool ScheduledCheckAllowed(Preferences p)
    {
        if(!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())return false;
        try
        {
            var profile=global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if(profile==null)return false;var cost=profile.GetConnectionCost();
            if(!p.AllowMeteredChecks&&(cost.NetworkCostType!=global::Windows.Networking.Connectivity.NetworkCostType.Unrestricted||cost.Roaming||cost.OverDataLimit))return false;
            if(global::Windows.System.Power.PowerManager.EnergySaverStatus==global::Windows.System.Power.EnergySaverStatus.On)return false;
        }catch{return false;}
        return true;
    }
}
