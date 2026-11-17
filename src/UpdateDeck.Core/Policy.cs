namespace UpdateDeck.Core;

public static class UpdatePolicy
{
    public static Risk Classify(Software app)
    {
        var n=$"{app.Name} {app.PackageId}".ToLowerInvariant();
        if(app.Kind is SoftwareKind.AudioPlugin or SoftwareKind.Game or SoftwareKind.GameLauncher)return Risk.Caution;
        if(n.Contains("firmware")||n.Contains("bios")||n.Contains("uefi")) return Risk.High;
        if(app.IsDriver||n.Contains("driver")||n.Contains("asio")||n.Contains("chipset")||n.Contains("nvidia app")||n.Contains("audio interface")) return Risk.SystemSensitive;
        if(app.Publisher.Contains("MOTU",StringComparison.OrdinalIgnoreCase)||app.Publisher.Contains("Focusrite",StringComparison.OrdinalIgnoreCase)||n.Contains("boss tone studio"))return Risk.SystemSensitive;
        if(new[]{"virtualbox","vmware","wireguard","openvpn","tailscale","crowdstrike","bitdefender","malwarebytes","kaspersky","sophos","norton antivirus"}.Any(n.Contains))return Risk.SystemSensitive;
        if(n.Contains("visual studio code")||n.Contains("visualstudiocode"))return Risk.Routine;
        if(new[]{"blender","fl studio","rekordbox","serato","sdk","python","node.js","nodejs","java","visual studio","docker","runtime",".net","redistributable","aida64","cpu-z","hwmonitor","hwinfo","afterburner","rivatuner"}.Any(n.Contains)) return Risk.Caution;
        return app.Risk;
    }
    public static string? BlockReason(Software app,Preferences preferences,DateTimeOffset now,bool pendingRestart,bool running,long freeBytes,bool supervised=false)
    {
        if(app.Kind is SoftwareKind.Game or SoftwareKind.GameLauncher || GamePlatforms.Contains(app.VendorId))return "Manage games and launcher updates in Games using the official platform.";
        if(app.VendorId.Length>0)return "Use this application's established publisher workflow. A known manager does not independently verify a newer release.";
        if(preferences.PauseInstallations)return "Installations are paused in Settings. Review your saved update policy before clearing this pause.";
        if(preferences.Ignored.Contains(app.Id)) return "Ignored by your saved policy.";
        if(app.Ambiguous) return "Installation identity needs inspection.";
        if(app.VersionEvidenceConflict)return "Installed version sources disagree for this registration. Inspect the raw version evidence and recheck before updating.";
        if(app.Pinned) return "The package manager has pinned this application.";
        if(app.EffectiveDelivery==UpdateDelivery.WinGet&&!app.PinVerified) return "Package pins could not be checked reliably.";
        if(app.State!=UpdateState.Available) return app.Explanation;
        if(app.EffectiveDelivery==UpdateDelivery.OfficialInstaller&&!supervised) return "An official installer is available. Review and launch it individually; it requires your interaction.";
        if(app.EffectiveDelivery==UpdateDelivery.None) return "This application uses its established update manager.";
        if(app.EffectiveDelivery==UpdateDelivery.OfficialInstaller&&!app.CanSupervise) return "This official installer is not eligible for supervised execution.";
        if(app.Scope is not ("User" or "Machine")) return "The installation scope could not be verified. Inspect it before choosing an installer.";
        if(app.Architecture is not ("X64" or "X86" or "Arm64")) return "The installed application architecture could not be verified. Use its publisher updater or inspect the installation.";
        if(!Versioning.Known(app.InstalledVersion)||Versioning.Compare(app.AvailableVersion,app.InstalledVersion) is not >0) return "A newer version could not be verified.";
        if(!Versioning.SameReleaseTrack(app.InstalledVersion,app.AvailableVersion)) return "Changing release tracks or an unknown prerelease channel requires a publisher-managed update.";
        if(!app.InstalledChannel.Equals(app.AvailableChannel,StringComparison.OrdinalIgnoreCase))return "The installed and target release channels differ or could not be reconciled. Use the publisher's updater.";
        if(app.EffectiveDelivery==UpdateDelivery.WinGet&&app.InstalledChannel.Length>0)return "This named release channel cannot be selected explicitly by the reviewed WinGet command. Use the publisher's updater.";
        var risk=Classify(app);
        if(risk!=Risk.Routine&&!(supervised&&risk==Risk.Caution&&app.EffectiveDelivery==UpdateDelivery.WinGet&&app.VendorId.Length==0)) return "Supervised update recommended for compatibility-sensitive software. Use its reviewed individual action or publisher workflow.";
        if(app.CheckedAt==null||now-app.CheckedAt>TimeSpan.FromMinutes(30)||app.CheckedAt-now>TimeSpan.FromMinutes(2)) return "Update metadata is stale or its timestamp is invalid. Check again before installing.";
        if(app.EffectiveDelivery==UpdateDelivery.WinGet&&!CatalogFreshness.IsFresh(app.CatalogUpdatedAt,now))return "The WinGet catalog cache is older than 30 minutes or its refresh time could not be verified. Refresh the official source in WinGet and check again; source policy is not overridden.";
        if(pendingRestart) return "Restart Windows before starting another installation.";
        if(string.IsNullOrWhiteSpace(app.InstallLocation)) return "The installation folder could not be verified. Inspect this application or use its own updater.";
        if(running) return "This application is running or its process state could not be verified. Close it normally or inspect the installation, then retry.";
        if(freeBytes<1024L*1024*1024) return "At least 1 GB of free space is required; the installer may require more.";
        return null;
    }
    public static QueuePlan Plan(IEnumerable<Software> apps,Preferences preferences,DateTimeOffset now,bool pendingRestart,Func<Software,bool> running,long freeBytes)
    {
        var automatic=new List<PlanItem>(); var handoffs=new List<PlanItem>(); var deferred=new List<PlanItem>();
        foreach(var app in apps.DistinctBy(x=>x.Id).OrderBy(x=>x.State==UpdateState.Available?0:x.State==UpdateState.Attention?1:2).ThenBy(x=>x.PackageId.Length>0?x.PackageId:x.Name,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Id,StringComparer.Ordinal))
        {
            // Gaming belongs exclusively to Games, including conflicts and cached records.
            // Do not create desktop updates, handoffs, or deferred-update rows for it.
            if(GamePlatforms.IsGaming(app))continue;
            if(!preferences.PauseInstallations&&app.VendorId.Length>0&&app.VendorId!="nvidia"&&!GamePlatforms.Contains(app.VendorId)&&app.Kind is not (SoftwareKind.Game or SoftwareKind.GameLauncher)&&!preferences.Ignored.Contains(app.Id)&&Classify(app) is not (Risk.SystemSensitive or Risk.High))
            {
                if(!handoffs.Any(x=>x.App.VendorId==app.VendorId)) handoffs.Add(new(app,"Handoff","Use the official manager. Completion must be rechecked."));
            }
            // Only actual update candidates/conflicting evidence belong in the update plan.
            // Unknown inventory and publisher workflows are not failed/deferred updates.
            if(app.State!=UpdateState.Available) { if(app.State==UpdateState.Attention) deferred.Add(new(app,"Defer",app.Explanation)); continue; }
            var reason=BlockReason(app,preferences,now,pendingRestart,running(app),freeBytes);
            if(reason==null) automatic.Add(new(app,"Update","Verified newer version from the established update provider.")); else deferred.Add(new(app,"Defer",reason));
        }
        return new(automatic,handoffs,deferred);
    }
    public static string DeferredSummary(QueuePlan plan)=>plan.Deferred.Count==0?"No newer automatic updates were found. Other software remains available in Installed Apps and Vendor Updaters.":string.Join(" ",plan.Deferred.GroupBy(x=>x.Reason).OrderByDescending(x=>x.Count()).Take(3).Select(group=>$"{group.Count()} update(s): {group.Key}"));
    public static bool ScheduleDue(Preferences p,DateTimeOffset now)
    {
        if(p.Schedule is not ("Daily" or "Weekly")) return false;
        int h=now.LocalDateTime.Hour;
        bool quiet=p.QuietStart<p.QuietEnd ? h>=p.QuietStart&&h<p.QuietEnd : p.QuietStart>p.QuietEnd&&(h>=p.QuietStart||h<p.QuietEnd);
        if(quiet)return false;
        if(p.LastScheduledAttempt.HasValue&&now-p.LastScheduledAttempt.Value is var attempted&&attempted>=TimeSpan.Zero&&attempted<ScheduledRetryDelay(p))return false;
        return p.LastCheck==null||p.LastCheck-now>TimeSpan.FromMinutes(2)||now-p.LastCheck>=TimeSpan.FromDays(p.Schedule=="Weekly"?7:1);
    }
    public static TimeSpan ScheduledRetryDelay(Preferences p)=>p.ScheduledFailureCount<=0?TimeSpan.FromMinutes(1):TimeSpan.FromMinutes(15*(1<<Math.Clamp(p.ScheduledFailureCount-1,0,5)));
}
