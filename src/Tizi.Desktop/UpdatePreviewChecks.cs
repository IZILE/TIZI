using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class UpdatePreviewChecks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        Directory.CreateDirectory(output);UiText.Language="zh-CN";
        var transport=new Fixture();
        var window=new MainWindow(true,new(){AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,CloseToTray=false});
        window.UpdateHttpClientFactory=()=>new HttpClient(transport,disposeHandler:false);window.Show();window.ShowPage("Settings");
        void Save(string name)
        {
            window.UpdateLayout();Dispatcher.UIThread.RunJobs();Motion.SettlePaint(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame()!;frame.Save(Path.Combine(output,name),new PngBitmapEncoderOptions());
        }
        try
        {
            Save("update-normal.png");await window.CheckStartupUpdatesAsync();await window.CheckStartupUpdatesAsync();
            var button=window.VersionUpdateButton!;
            check(transport.Requests==1&&button.Content is TextBlock{Text:"发现新版本"},"Startup update discovery runs once and changes the existing settings action");
            check(button.Background is ISolidColorBrush brush&&brush.Color==Color.Parse("#2979F5")&&((TextBlock)button.Content!).Foreground==Skin.LabelBrush,"An available release uses the existing blue accent and normal readable label");
            Save("update-available.png");window.ShowUpdates();var dialog=window.OpenDialogs.Single();CenteredDialogChecks.Ready(dialog);window.UpdateLayout();
            var actions=dialog.GetVisualDescendants().OfType<Button>().Where(b=>b.IsVisible).ToArray();
            check(actions.Any(b=>b.Content is TextBlock{Text:"下载新版本"})&&!actions.Any(b=>b.Content is TextBlock t&&t.Text==UiText.Get("GitHub releases")),"A cached new release opens directly with Download new version and no GitHub releases action");
            check(transport.Requests==1,"Opening a cached release does not check metadata a second time");
            Save("update-details.png");await dialog.CloseSheetAsync();
        }
        finally {window.Close();await window.Controller.DisposeAsync();}
        var failure=new MainWindow(true,new(){AutoUpdate=false,RestoreConnection=false});failure.Show();failure.ShowPage("Settings");
        var failed=new Fixture{Fail=true};failure.UpdateHttpClientFactory=()=>new HttpClient(failed,disposeHandler:false);
        await failure.CheckStartupUpdatesAsync();await failure.CheckStartupUpdatesAsync();
        check(failed.Requests==1&&!failure.Notification.Showing&&failure.VersionUpdateButton!.Content is TextBlock{Text:"检查更新"},"Startup network failures stay silent and leave manual update checking available");
        failure.Close();await failure.Controller.DisposeAsync();
    }
    private sealed class Fixture:HttpMessageHandler
    {
        internal int Requests;internal bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Requests++;
            if(Fail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var release=new JsonObject{["tag_name"]="v0.1.36",["draft"]=false,["prerelease"]=false,["html_url"]="https://github.com/IZILE/TIZI/releases/tag/v0.1.36",["body"]="- 改进订阅导入兼容性。\n- 优化模型选择与机场搜索。\n- 优化版本更新体验。",["assets"]=new JsonArray(new JsonObject{["name"]="TIZI-Setup.exe",["browser_download_url"]="https://github.com/IZILE/TIZI/releases/download/v0.1.36/TIZI-Setup.exe"},new JsonObject{["name"]="SHA256SUMS.txt",["browser_download_url"]="https://github.com/IZILE/TIZI/releases/download/v0.1.36/SHA256SUMS.txt"})};
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(release.ToJsonString())});
        }
    }
}
