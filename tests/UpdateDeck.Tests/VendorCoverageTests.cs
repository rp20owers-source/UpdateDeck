using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;

namespace UpdateDeck.Tests;

public class VendorCoverageTests
{
    [Theory]
    [InlineData("Pianoverse version 1.0.11","IK Multimedia","ik")]
    [InlineData("ARC System 3 version 3.1.0","IK Multimedia","ik")]
    [InlineData("SampleTron 2 version 2.0.5","IK Multimedia","ik")]
    [InlineData("MixBox version 1.5.2","IK Multimedia","ik")]
    [InlineData("Custom Shop version 1.8.0","IK Multimedia","ik")]
    [InlineData("NVIDIA HD Audio Driver 1.4.6.6","NVIDIA Corporation","nvidia")]
    [InlineData("ME-90 IR Loader version 1.0.0","Roland Corporation","boss")]
    [InlineData("Microsoft .NET SDK 10.0.402 (x64) from Visual Studio","Microsoft Corporation","visualstudio")]
    public void RecognizesEstablishedPublisherWorkflowWithoutClaimingAnUpdate(string name,string publisher,string expected)
    {
        var launchers=new VendorLaunchers();var app=Assert.Single(launchers.Attach([new Software{Id="registered",Name=name,Publisher=publisher,State=UpdateState.Unknown}]));
        Assert.Equal(expected,app.VendorId);Assert.Equal(UpdateState.VendorManaged,app.State);Assert.False(app.CanUpdate);
        Assert.Null(app.CheckedAt);Assert.Empty(app.AvailableVersion);Assert.Contains("handoff",app.Explanation);
    }
    [Fact]
    public void SimilarProductNameWithWrongPublisherRemainsUnknown()
    {
        var app=Assert.Single(new VendorLaunchers().Attach([new Software{Id="wrong",Name="Pianoverse",Publisher="Different publisher"}]));
        Assert.Empty(app.VendorId);Assert.Equal(UpdateState.Unknown,app.State);
    }
}
