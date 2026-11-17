using System.Net;
using System.Net.Http;
using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;
namespace UpdateDeck.Tests;

public class OfficialTransferTimeoutTests
{
    private sealed class StalledStream : Stream
    {
        public bool Closed;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){await Task.Delay(Timeout.Infinite,cancellationToken);return 0;}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
    }
    private sealed class BodyHandler(StalledStream stream) : HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stream)});}
    private sealed class HeaderHandler : HttpMessageHandler
    {protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){await Task.Delay(Timeout.Infinite,cancellation);return new(HttpStatusCode.OK);}}
    private static OfficialRelease Release()=>new("notepad-plus-plus","2.0","X64",new("https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v2.0/npp.2.0.Installer.x64.exe"),new string('0',64),110000,new("https://github.com/notepad-plus-plus/notepad-plus-plus/releases/tag/v2.0"));
    [Fact]public async Task ABodyThatStopsAfterHeadersHasAnInactivityLimit()
    {
        var stalled=new StalledStream();using var download=new OfficialDownloads(new BodyHandler(stalled),TimeSpan.FromSeconds(2),TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAsync<TimeoutException>(()=>download.DownloadAsync(Release(),new MemoryStream(),new ImmediateProgress<string>(_=>{}),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)));Assert.True(stalled.Closed);
    }
    [Fact]public async Task TheWholeTransferLimitAlsoCoversWaitingForHeaders()
    {
        using var download=new OfficialDownloads(new HeaderHandler(),TimeSpan.FromMilliseconds(80));
        var error=await Assert.ThrowsAsync<TimeoutException>(()=>download.DownloadAsync(Release(),new MemoryStream(),new ImmediateProgress<string>(_=>{}),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)));Assert.Contains("official download",error.Message,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]public async Task ReleaseMetadataCannotWaitForeverForItsBody()
    {
        var stalled=new StalledStream();using var download=new OfficialDownloads(new BodyHandler(stalled),TimeSpan.FromSeconds(2),TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAsync<TimeoutException>(()=>download.MetadataAsync(OfficialInstallerProvider.MetadataUri,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)));Assert.True(stalled.Closed);
    }
    [Fact]public async Task CallerCancellationRemainsCancellationRatherThanATransferFailure()
    {
        var stalled=new StalledStream();using var download=new OfficialDownloads(new BodyHandler(stalled));using var cancel=new CancellationTokenSource(80);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>download.DownloadAsync(Release(),new MemoryStream(),new ImmediateProgress<string>(_=>{}),cancel.Token));Assert.True(stalled.Closed);
    }
}
