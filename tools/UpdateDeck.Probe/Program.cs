using System.Diagnostics;
using System.Text.Json;
using UpdateDeck.Core;
using UpdateDeck.Windows;

// Explicit developer-only, read-only network probe. This tool has no installation or launcher call.
if(args.Length!=2||args[0] is not ("official-download" or "audio-inventory" or "plan" or "catalog-info" or "migration" or "benchmark" or "environment"))
{Console.Error.WriteLine("Usage: UpdateDeck.Probe official-download|audio-inventory|plan|catalog-info|migration|benchmark|environment <report.json> (read-only probes; never installs or launches software)");return 2;}
string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
string coreBinarySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Software).Assembly.Location))).ToLowerInvariant();
if(args[0]=="environment")
{
    using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");connection.Open();using var command=connection.CreateCommand();
    string Scalar(string sql){command.CommandText=sql;return Convert.ToString(command.ExecuteScalar())??"";}
    File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,architecture=System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,sqliteVersion=Scalar("SELECT sqlite_version()"),sqliteSourceId=Scalar("SELECT sqlite_source_id()"),jsonFunctionsAvailable=Scalar("SELECT json_extract('{\"ok\":1}','$.ok')")=="1",inMemoryDatabaseOnly=true,installerExecuted=false,thirdPartySoftwareChanged=false},StateStore.Json));return 0;
}
if(args[0]=="benchmark")
{
    var results=new List<object>();
    foreach(int count in new[]{250,1000,2500,10000})
    {
        var registry=Enumerable.Range(0,count).Select(i=>new Software{Id="registry-"+i,Name="Benchmark application "+i,Publisher="Fixture Publisher",InstalledVersion="1.0",PackageId="Fixture.Application"+i,ProductCodes=["registration-"+i],InstallLocation=@"C:\UpdateDeckBenchmark\"+i,Scope="User",Architecture="X64",Provider="Registry"}).ToArray();
        var packages=registry.Select((x,i)=>x with{Id="catalog-"+i,Provider="WinGet"}).ToArray();long before=GC.GetTotalAllocatedBytes();var timer=Stopwatch.StartNew();var resolved=IdentityResolver.Resolve(registry.Concat(packages));long resolveMs=timer.ElapsedMilliseconds,resolveAllocated=GC.GetTotalAllocatedBytes()-before;
        var current=resolved.Select((x,i)=>x with{Id="new-"+i,InstalledVersion="2.0",ProductCodes=["new-registration-"+i]}).ToArray();before=GC.GetTotalAllocatedBytes();timer.Restart();var continued=InstallationIdentity.Continue(current,resolved);long continuityMs=timer.ElapsedMilliseconds,continuityAllocated=GC.GetTotalAllocatedBytes()-before;
        timer.Restart();int found=0;for(int repeat=0;repeat<100;repeat++)found+=continued.Count(x=>x.Name.Contains("99",StringComparison.OrdinalIgnoreCase)||x.Publisher.Contains("99",StringComparison.OrdinalIgnoreCase));
        long filterMs=timer.ElapsedMilliseconds;var oldIds=resolved.Select(x=>x.Id).ToHashSet();results.Add(new{applications=count,inputRecords=registry.Length+packages.Length,resolved=resolved.Count,preservedIds=continued.Count(x=>oldIds.Contains(x.Id)),resolveMs,resolveAllocatedBytes=resolveAllocated,continuityMs,continuityAllocatedBytes=continuityAllocated,filter100PassesMs=filterMs,filterMatches=found});
    }
    File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,syntheticOwnedData=true,results,installerExecuted=false,thirdPartySoftwareChanged=false},StateStore.Json));return 0;
}
if(args[0]=="migration")
{
    var catalog=new WinGetMigrationCatalog();var results=new List<CatalogPreview>();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
    foreach(var id in new[]{"7zip.7zip","Notepad++.Notepad++","UpdateDeck.DeliberatelyMissingFixture"}){var result=await catalog.ResolveAsync(id,"Unknown",timeout.Token);results.Add(result);if(result.StopBatch)break;}
    File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,targetArchitecture=System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),results,installerExecuted=false,thirdPartySoftwareChanged=false},StateStore.Json));return 0;
}
if(args[0]=="catalog-info")
{
    using var protectedPins=WinGetPinStorage.Acquire();if(!protectedPins.Safe)throw new InvalidDataException("Pin storage could not be protected.");
    var manager=new Microsoft.Management.Deployment.PackageManager();var source=manager.GetPredefinedPackageCatalog(Microsoft.Management.Deployment.PredefinedPackageCatalog.OpenWindowsCatalog);source.AcceptSourceAgreements=false;
    var before=source.Info;var connected=await source.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(90));
    var after=connected.PackageCatalog?.Info;
    File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,connected=connected.Status.ToString(),before=new{before.Name,before.Type,before.Argument,before.LastUpdateTime},after=after==null?null:new{after.Name,after.Type,after.Argument,after.LastUpdateTime}},StateStore.Json));return 0;
}
if(args[0]=="audio-inventory")
{
    var watch=Stopwatch.StartNew();var result=await new AudioPluginInventory(new(){AudioPluginInventory=true}).DiscoverAsync(false,CancellationToken.None);
    File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,elapsedMilliseconds=watch.ElapsedMilliseconds,modules=result.Apps.Count,formats=result.Apps.GroupBy(x=>x.InstallerType).ToDictionary(x=>x.Key,x=>x.Count()),architectures=result.Apps.GroupBy(x=>x.Architecture).ToDictionary(x=>x.Key,x=>x.Count()),providers=result.Providers,sample=result.Apps.Take(12).Select(x=>new{x.Name,x.Publisher,x.InstalledVersion,x.InstallerType,x.Architecture,x.State}),pluginCodeLoaded=false,thirdPartySoftwareChanged=false},StateStore.Json));return 0;
}
if(args[0]=="plan")
{
    var watch=Stopwatch.StartNew();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var provider=new WinGetProvider(new ProcessRunner());
    var registryTask=new RegistryInventory().DiscoverAsync(false,timeout.Token);var catalog=await provider.DiscoverAsync(true,timeout.Token);var registry=await registryTask;
    var merged=IdentityResolver.Resolve(registry.Apps.Concat(catalog.Apps)).Select(x=>x with{Risk=UpdatePolicy.Classify(x)}).ToArray();
    var installed=await provider.DiscoverAsync(false,timeout.Token);var freshRegistry=await new RegistryInventory().DiscoverAsync(false,timeout.Token);var fresh=IdentityResolver.Resolve(freshRegistry.Apps.Concat(installed.Apps));var available=merged.Where(x=>x.Provider=="WinGet"&&x.State==UpdateState.Available).ToArray();
    var processes=RunningApplications.Capture();var plan=UpdatePolicy.Plan(merged,new(){AutomaticVendorHandoffs=false},DateTimeOffset.UtcNow,MachineServices.InstallationRestartRequired(),processes.UnsafeToInstall,MachineServices.FreeSpace());
    File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,elapsedMilliseconds=watch.ElapsedMilliseconds,restartAssessment=MachineServices.InspectRestart(),pendingRestart=MachineServices.PendingRestart(),registryRecords=registry.Apps.Count,registryArchitectureKnown=registry.Apps.Count(x=>x.Architecture!="Unknown"),registryMsiArchitecture=registry.Apps.Count(x=>x.Evidence.Any(e=>e.Contains("Summary Template"))),catalogRecords=catalog.Apps.Count,available=available.Length,automatic=plan.Automatic.Count,deferredReasons=plan.Deferred.GroupBy(x=>x.Reason).ToDictionary(x=>x.Key,x=>x.Count()),freshInstalledChecks=available.Select(app=>{var match=WinGetProvider.MatchingInstallations(app,fresh);return new{app.Name,app.PackageId,app.InstalledVersion,app.AvailableVersion,app.Architecture,app.Scope,matches=match.Length,versionUnchanged=match.Length==1&&Versioning.Compare(match[0].InstalledVersion,app.InstalledVersion)==0};}),providers=catalog.Providers,installerExecuted=false,thirdPartySoftwareChanged=false},StateStore.Json));return 0;
}
string temporary=Path.Combine(Path.GetTempPath(),"UpdateDeck-probe-"+Guid.NewGuid().ToString("N")+".exe");
try
{
    using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));using var download=new OfficialDownloads();
    string architecture=System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
    string metadata=await download.MetadataAsync(OfficialInstallerProvider.MetadataUri,cancellation.Token);
    var release=OfficialInstallerProvider.ParseRelease(metadata,architecture)??throw new InvalidDataException("The official release did not contain a reviewed compatible installer with SHA-256 metadata.");
    var stopwatch=Stopwatch.StartNew();
    await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
    {
        await download.DownloadAsync(release,file,new Progress<string>(s=>Console.WriteLine(s)),cancellation.Token);await file.FlushAsync(cancellation.Token);
        if(!await OfficialDownloads.HashMatchesAsync(file,release.Sha256,cancellation.Token))throw new InvalidDataException("Downloaded SHA-256 mismatch.");
    }
    bool signature=OfficialInstallerProvider.IsNotepadSigned(temporary);var version=FileVersionInfo.GetVersionInfo(temporary);
    bool versionMatches=Versioning.Compare(version.FileVersion,release.Version)==0;
    var registry=await new RegistryInventory().DiscoverAsync(false,cancellation.Token);var candidates=registry.Apps.Where(OfficialInstallerProvider.IsCandidate).ToArray();
    var installedEvidence=candidates.Select(app=>new{app.Name,app.InstalledVersion,scope=app.Scope,signedByExpectedPublisher=ExecutableMetadata.LocalIconExecutable(app.IconPath) is string path&&OfficialInstallerProvider.IsNotepadSigned(path)}).ToArray();
    var report=new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,passed=signature&&versionMatches,release.Version,release.Architecture,release.Download,release.Size,release.Sha256,sha256Matches=true,authenticodeExpectedPublisher=signature,fileVersion=version.FileVersion,versionMatches,elapsedMilliseconds=stopwatch.ElapsedMilliseconds,installedEvidence,installerExecuted=false,thirdPartySoftwareChanged=false};
    File.WriteAllText(output,JsonSerializer.Serialize(report,StateStore.Json));Console.WriteLine($"Read-only official source probe {(signature&&versionMatches?"passed":"failed")}: {output}");return signature&&versionMatches?0:1;
}
catch(Exception ex){File.WriteAllText(output,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,coreBinarySha256,passed=false,error=ex.Message,installerExecuted=false},StateStore.Json));Console.Error.WriteLine(ex.Message);return 1;}
finally{try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}

