using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public sealed class RunningApplicationsTests
{
    private static Software App()=>Fixture.App() with{InstallLocation=@"C:\Apps\Example",PrimaryExecutable=@"C:\Apps\Example\Example.exe"};
    [Fact]public void RunningChildProcessBlocksItsInstallation(){var census=new RunningApplications([new("helper",@"C:\Apps\Example\bin\helper.exe")]);Assert.Equal(RunningState.Running,census.StateFor(App()));}
    [Fact]public void SimilarPrefixIsNotTheSameInstallation(){var census=new RunningApplications([new("Example",@"C:\Apps\Example Beta\Example.exe")]);Assert.Equal(RunningState.NotRunning,census.StateFor(App()));}
    [Fact]public void UnreadableMatchingProcessRemainsUnknown(){var census=new RunningApplications([new("Example",null)]);Assert.Equal(RunningState.Unknown,census.StateFor(App()));Assert.True(census.UnsafeToInstall(App()));}
    [Fact]public void UnrelatedProtectedSystemProcessDoesNotBlockEveryApp(){var census=new RunningApplications([new("System",null),new("lsass",null)]);Assert.Equal(RunningState.NotRunning,census.StateFor(App()));}
    [Theory][InlineData("")][InlineData("relative/path")]
    public void MissingOrRelativeInstallationPathIsUnknown(string path)=>Assert.Equal(RunningState.Unknown,new RunningApplications([]).StateFor(App() with{InstallLocation=path}));
    [Theory][InlineData("scope")][InlineData("architecture")]
    public void UnknownInstallIdentityCannotSilentlyChangeScopeOrArchitecture(string unknown){var app=unknown=="scope"?App() with{Scope="Unknown"}:App() with{Architecture="Unknown"};Assert.NotNull(UpdatePolicy.BlockReason(app,new(),DateTimeOffset.UtcNow,false,false,long.MaxValue));}
}
