using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public class ReleaseUpdateTests
{
    private static readonly byte[] Installer=Encoding.ASCII.GetBytes("MZ-synthetic-installer-never-executed");
    [Fact]public async Task OfficialReleaseAssetsAreDownloadedAndVerified()
    {
        using var http=new HttpClient(new Handler());var updates=new ReleaseUpdates(http);var release=await updates.LatestAsync();
        Assert.Equal(new Version(0,1,23),release.Version);Assert.Equal("https://github.com/IZILE/TIZI/releases/tag/v0.1.23",release.Page.AbsoluteUri);
        var directory=Path.Combine(Path.GetTempPath(),"tizi-update-tests-"+Guid.NewGuid().ToString("N"));
        try{var file=await updates.DownloadAsync(release,directory);Assert.Equal(Installer,await File.ReadAllBytesAsync(file));Assert.False(File.Exists(file+".partial"));}
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact]public async Task ModifiedDownloadIsRejectedAndNoPartialInstallerRemains()
    {
        using var http=new HttpClient(new Handler(corrupt:true));var updates=new ReleaseUpdates(http);var release=await updates.LatestAsync();var directory=Path.Combine(Path.GetTempPath(),"tizi-update-tests-"+Guid.NewGuid().ToString("N"));
        try{await Assert.ThrowsAsync<InvalidDataException>(()=>updates.DownloadAsync(release,directory));Assert.Empty(Directory.GetFiles(directory));}
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Theory][InlineData("https://example.com/setup.exe")][InlineData("http://github.com/IZILE/TIZI/releases/download/v0.1.23/TIZI-Setup.exe")][InlineData("https://github.com/other/repo/releases/download/v0.1.23/TIZI-Setup.exe")]
    public async Task RedirectedMetadataCannotSelectAnUnrelatedInstaller(string asset)
    {using var http=new HttpClient(new Handler(asset:asset));await Assert.ThrowsAsync<FormatException>(()=>new ReleaseUpdates(http).LatestAsync());}
    [Fact]public async Task CancelledChecksDoNotContinueDownloading()
    {using var http=new HttpClient(new Handler());using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ReleaseUpdates(http).LatestAsync(cancellation.Token));}
    private sealed class Handler(bool corrupt=false,string? asset=null):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();var url=request.RequestUri!.AbsoluteUri;HttpContent content;
            if(url==ReleaseUpdates.LatestUrl)
            {
                var root=new JsonObject{["tag_name"]="v0.1.23",["draft"]=false,["prerelease"]=false,["body"]="Synthetic update",["assets"]=new JsonArray(new JsonObject{["name"]="TIZI-Setup.exe",["browser_download_url"]=asset??"https://github.com/IZILE/TIZI/releases/download/v0.1.23/TIZI-Setup.exe"},new JsonObject{["name"]="SHA256SUMS.txt",["browser_download_url"]="https://github.com/IZILE/TIZI/releases/download/v0.1.23/SHA256SUMS.txt"})};content=new StringContent(root.ToJsonString());
            }
            else if(url.EndsWith("SHA256SUMS.txt"))content=new StringContent(Convert.ToHexString(SHA256.HashData(Installer))+"  TIZI-Setup.exe\n");
            else content=new ByteArrayContent(corrupt?Encoding.ASCII.GetBytes("MZ-corrupted"):Installer);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
}
