using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace UpdateDeck.Windows;

/// <summary>Bounded HTTPS reads. Redirects are checked individually; TLS validation stays with Windows.</summary>
public sealed class OfficialDownloads : IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan transferTimeout;
    private readonly TimeSpan inactivityTimeout;
    public OfficialDownloads(HttpMessageHandler? handler=null,TimeSpan? transferTimeout=null,TimeSpan? inactivityTimeout=null)
    {
        this.transferTimeout=transferTimeout??TimeSpan.FromMinutes(10);this.inactivityTimeout=inactivityTimeout??TimeSpan.FromSeconds(30);
        if(this.transferTimeout<=TimeSpan.Zero||this.inactivityTimeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(transferTimeout));
        client=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None}){Timeout=TimeSpan.FromMinutes(5)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateDeck/2.0");
    }
    public async Task<string> MetadataAsync(Uri address,CancellationToken cancellation)
    {
        if(address.AbsoluteUri!="https://api.github.com/repos/notepad-plus-plus/notepad-plus-plus/releases/latest")throw new InvalidDataException("Unreviewed release metadata address.");
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);limit.CancelAfter(transferTimeout<TimeSpan.FromSeconds(20)?transferTimeout:TimeSpan.FromSeconds(20));
        using var request=new HttpRequestMessage(HttpMethod.Get,address);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");request.Headers.Add("X-GitHub-Api-Version","2022-11-28");
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,limit.Token);
            response.EnsureSuccessStatusCode();
            using var memory=new MemoryStream();await CopyBoundedAsync(response,memory,2*1024*1024,null,limit.Token);
            return System.Text.Encoding.UTF8.GetString(memory.ToArray());
        }
        catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested){throw new TimeoutException("Official release metadata did not finish within its time or inactivity limit.",ex);}
    }
    public static bool AllowedAssetUri(Uri uri)=>uri.Scheme==Uri.UriSchemeHttps&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&
        (uri.Host=="github.com"&&uri.AbsolutePath.StartsWith("/notepad-plus-plus/notepad-plus-plus/releases/download/",StringComparison.Ordinal)||
         uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com");
    public async Task DownloadAsync(OfficialRelease release,Stream destination,IProgress<string> progress,CancellationToken cancellation)
    {
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);limit.CancelAfter(transferTimeout);
        var address=release.Download;int redirects=0;
        try
        {
            while(true)
            {
                if(!AllowedAssetUri(address))throw new InvalidDataException("The download redirected outside the reviewed HTTPS asset hosts.");
                using var response=await client.GetAsync(address,HttpCompletionOption.ResponseHeadersRead,limit.Token);
                if((int)response.StatusCode is >=300 and <400)
                {
                    if(++redirects>4||response.Headers.Location==null)throw new InvalidDataException("The official download returned too many or invalid redirects.");
                    address=new Uri(address,response.Headers.Location);continue;
                }
                response.EnsureSuccessStatusCode();
                await CopyBoundedAsync(response,destination,release.Size,progress,limit.Token);return;
            }
        }
        catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested){throw new TimeoutException("The official download stopped because its transfer or inactivity limit was reached. No installer was started.",ex);}
    }
    private async Task CopyBoundedAsync(HttpResponseMessage response,Stream destination,long maximum,IProgress<string>? progress,CancellationToken cancellation)
    {
        long? declared=response.Content.Headers.ContentLength;
        if(declared>maximum)throw new InvalidDataException("The response exceeds the reviewed size limit.");
        await using var input=await response.Content.ReadAsStreamAsync(cancellation);var buffer=new byte[64*1024];long total=0,last=0;int count;
        while(true)
        {
            using(var idle=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){idle.CancelAfter(inactivityTimeout);count=await input.ReadAsync(buffer,idle.Token);}
            if(count==0)break;
            total+=count;if(total>maximum)throw new InvalidDataException("The response exceeds the reviewed size limit.");
            await destination.WriteAsync(buffer.AsMemory(0,count),cancellation);
            if(total-last>=512*1024){progress?.Report($"Downloading official installer · {Math.Clamp(total*100/maximum,0,100)}% · {total/1048576.0:F1} MB");last=total;}
        }
        if(progress!=null&&total!=maximum)throw new InvalidDataException("The installer length differs from the official release metadata.");
    }
    public static async Task<bool> HashMatchesAsync(Stream file,string sha256,CancellationToken cancellation)
    {
        file.Position=0;var actual=await SHA256.HashDataAsync(file,cancellation);file.Position=0;
        return sha256.Length==64&&CryptographicOperations.FixedTimeEquals(actual,Convert.FromHexString(sha256));
    }
    public void Dispose()=>client.Dispose();
}
