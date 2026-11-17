using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class PayloadRetentionTests
{
    [Fact]public async Task ValidatedPayloadRemainsLockedUntilActualMonitoringCompletes()
    {
        string path=Path.Combine(Fixture.Temp(),"owned-payload.bin");File.WriteAllText(path,"harmless fixture");var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InstallerPayloadRetention.Retain(path,lease,completed.Task);Assert.Throws<IOException>(()=>File.Open(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());Assert.True(File.Exists(path));
        completed.SetResult();for(int i=0;i<100&&File.Exists(path);i++)await Task.Delay(10);Assert.False(File.Exists(path));
    }
    [Fact]public async Task FailedMonitorCannotDeleteOrUnprotectAnUncertainPayload()
    {
        string path=Path.Combine(Fixture.Temp(),"unresolved-payload.bin");File.WriteAllText(path,"harmless fixture");var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InstallerPayloadRetention.Retain(path,lease,completed.Task);completed.SetException(new IOException("fixture monitor failure"));await Task.Delay(30);
        Assert.True(File.Exists(path));Assert.Throws<IOException>(()=>File.Open(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());
        // The test process owns this tiny unresolved lease; Windows releases it on process exit.
    }
}
