using System.Text;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public sealed class AudioPluginTests
{
    // A PE data fixture with an export table and no executable code. It is never loaded.
    private static byte[] Module(string export)
    {
        var bytes=new byte[2048];using var stream=new MemoryStream(bytes);using var w=new BinaryWriter(stream);
        w.Write((ushort)0x5A4D);stream.Position=60;w.Write(128);stream.Position=128;w.Write(0x4550);w.Write((ushort)0x8664);w.Write((ushort)1);
        stream.Position=148;w.Write((ushort)240);w.Write((ushort)0x2022);stream.Position=152;w.Write((ushort)0x20b);
        stream.Position=184;w.Write(0x1000);w.Write(0x200);stream.Position=208;w.Write(0x2000);w.Write(0x200);stream.Position=260;w.Write(16);w.Write(0x1000);w.Write(0x100);
        stream.Position=392;w.Write(Encoding.ASCII.GetBytes(".rdata\0\0"));w.Write(0x600);w.Write(0x1000);w.Write(0x600);w.Write(0x200);
        stream.Position=428;w.Write(0x40000040);stream.Position=512+20;w.Write(1);w.Write(1);w.Write(0x1090);w.Write(0x1080);w.Write(0x1098);
        stream.Position=640;w.Write(0x10A0);stream.Position=672;w.Write(Encoding.ASCII.GetBytes(export+"\0"));return bytes;
    }
    [Theory][InlineData("GetPluginFactory",".vst3","VST3")][InlineData("clap_entry",".clap","CLAP")][InlineData("VSTPluginMain",".dll","VST2")]
    public async Task RecognizesStaticPluginExportsWithoutLoading(string symbol,string extension,string format)
    {
        string directory=Fixture.Temp();File.WriteAllBytes(Path.Combine(directory,"ReadOnlyFixture"+extension),Module(symbol));
        var prefs=new Preferences{AudioPluginInventory=true,AudioPluginDirectories=[directory]};var result=await new AudioPluginInventory(prefs,()=>[]).DiscoverAsync(true,CancellationToken.None);
        var app=Assert.Single(result.Apps);Assert.Equal(SoftwareKind.AudioPlugin,app.Kind);Assert.Equal(format,app.InstallerType);Assert.Equal(UpdateState.Unknown,app.State);Assert.False(app.CanUpdate);Assert.Equal(Risk.Caution,app.Risk);
    }
    [Fact]public async Task ArbitraryDllsAndExecutablesAreNotPluginInventory(){string directory=Fixture.Temp();File.WriteAllBytes(Path.Combine(directory,"helper.dll"),Module("UnrelatedLibraryEntry"));File.WriteAllBytes(Path.Combine(directory,"not-a-plugin.exe"),Module("VSTPluginMain"));File.WriteAllText(Path.Combine(directory,"broken.clap"),"not a PE");var result=await new AudioPluginInventory(new(){AudioPluginInventory=true,AudioPluginDirectories=[directory]},()=>[]).DiscoverAsync(false,CancellationToken.None);Assert.Empty(result.Apps);}
    [Fact]public async Task OptionalInventoryOffDoesNotTouchPluginDirectories(){var result=await new AudioPluginInventory(new(),()=>throw new InvalidOperationException("Must not enumerate opt-out paths")).DiscoverAsync(true,CancellationToken.None);Assert.Empty(result.Apps);Assert.Contains("off",Assert.Single(result.Providers).Message);}
    [Fact]public async Task OverlappingRootsDoNotDuplicateTheSameModule(){string root=Fixture.Temp();string child=Directory.CreateDirectory(Path.Combine(root,"plugins")).FullName;File.WriteAllBytes(Path.Combine(child,"Fixture.clap"),Module("clap_entry"));var result=await new AudioPluginInventory(new(){AudioPluginInventory=true,AudioPluginDirectories=[root,child]},()=>[]).DiscoverAsync(false,CancellationToken.None);Assert.Single(result.Apps);}
    [Fact]public async Task ExcludedPluginFoldersAreNotRead(){string root=Fixture.Temp();File.WriteAllBytes(Path.Combine(root,"Fixture.clap"),Module("clap_entry"));var result=await new AudioPluginInventory(new(){AudioPluginInventory=true,AudioPluginDirectories=[root],ExcludedDirectories=[root]},()=>[]).DiscoverAsync(false,CancellationToken.None);Assert.Empty(result.Apps);}
    [Fact]public async Task BundleTraversalHasAFixedDepthLimit(){string root=Fixture.Temp(),deep=root;for(int i=0;i<7;i++)deep=Directory.CreateDirectory(Path.Combine(deep,"level"+i)).FullName;File.WriteAllBytes(Path.Combine(deep,"Fixture.clap"),Module("clap_entry"));var result=await new AudioPluginInventory(new(){AudioPluginInventory=true,AudioPluginDirectories=[root]},()=>[]).DiscoverAsync(false,CancellationToken.None);Assert.Empty(result.Apps);}
    [Fact]public void DifferentPluginFormatsAndTheHostApplicationStayDistinct(){var plugin=Fixture.App() with{Kind=SoftwareKind.AudioPlugin,PrimaryExecutable=@"C:\Plugins\Example.vst3"};Assert.Equal(0,IdentityResolver.Confidence(plugin,plugin with{Id="other",PrimaryExecutable=@"C:\Plugins\Example.dll"}));Assert.Equal(0,IdentityResolver.Confidence(plugin,plugin with{Kind=SoftwareKind.Application}));}
    [Fact]public void MalformedExportPointersAndCountsAreRejected(){var count=Module("clap_entry");BitConverter.GetBytes(uint.MaxValue).CopyTo(count,512+24);var address=Module("clap_entry");BitConverter.GetBytes(uint.MaxValue).CopyTo(address,640);foreach(var bytes in new[]{count,address}){var file=Path.Combine(Fixture.Temp(),"broken.dll");File.WriteAllBytes(file,bytes);Assert.False(PeExports.Contains(file,"clap_entry"));}}
    [Fact]public void ExportReaderInspectsAnActualNativeDependency(){var file=Path.Combine(AppContext.BaseDirectory,"e_sqlite3.dll");Assert.True(File.Exists(file));Assert.True(PeExports.Contains(file,"sqlite3_libversion"));Assert.False(PeExports.Contains(file,"VSTPluginMain"));}
}
