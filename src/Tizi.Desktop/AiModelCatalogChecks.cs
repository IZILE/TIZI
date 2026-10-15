using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class AiModelCatalogChecks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        var original=new DiscoverySettings{ApiKey="synthetic-key-never-sent",Model="manual-model",Profiles=new(){["DeepSeek"]=new("chat","https://api.deepseek.com/chat/completions","deepseek-flash","private-fixture-credential"){Models=["deepseek-flash","deepseek-v4-pro"]}}};
        DiscoverySettings? saved=null;var owner=new MainWindow(true,new(){AutoUpdate=false,RestoreConnection=false});owner.Show();
        var dialog=new AiSearchSettingsDialog(original,value=>{saved=value;return Task.CompletedTask;},detectOnOpen:false);var shown=dialog.ShowDialog(owner);
        try
        {
            dialog.ModelHttpClientFactory=()=>new HttpClient(new Reply(HttpStatusCode.OK,"{\"data\":[{\"id\":\"gpt-a\"},{\"id\":\"gpt-b\"}]}"));
            await dialog.DetectModelsAsync();
            check(dialog.ModelField.Items.Contains("gpt-a")&&dialog.ModelField.Items.Contains("gpt-b")&&dialog.ModelField.Text=="manual-model","Detecting a model list populates editable choices without changing the chosen or manually entered model");
            dialog.ModelField.SelectedItem="gpt-b";dialog.UpdateLayout();Dispatcher.UIThread.RunJobs();
            check(dialog.ModelField.Text=="gpt-b","Choosing a detected model changes the actual model field");
            dialog.ProviderChoice.SelectedItem="DeepSeek";
            check(dialog.KeyField.Text=="private-fixture-credential"&&dialog.ModelField.Text=="deepseek-flash"&&dialog.ModelField.Items.Contains("deepseek-v4-pro")&&!dialog.ModelField.Items.Contains("gpt-a"),"Each provider restores its own secret, selected model and cached model list");
            var pending=new TaskCompletionSource<HttpResponseMessage>();dialog.ModelHttpClientFactory=()=>new HttpClient(new Pending(pending.Task));
            var detection=dialog.DetectModelsAsync();dialog.ProviderChoice.SelectedItem="OpenAI";
            pending.SetResult(new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"late-deepseek\"}]}")});await detection;
            check(dialog.ModelField.Text=="gpt-b"&&dialog.ModelField.Items.Contains("gpt-a")&&!dialog.ModelField.Items.Contains("late-deepseek"),"A late model response cannot replace another provider's model list after switching");
            dialog.ModelField.Text="custom-manual";dialog.ModelHttpClientFactory=()=>new HttpClient(new Reply(HttpStatusCode.Unauthorized,"{}"));await dialog.DetectModelsAsync();
            check(dialog.ModelField.Text=="custom-manual"&&dialog.ModelField.Items.Contains("gpt-a")&&dialog.Notification.IsError,"Failed model detection retains the user's input and the previous list");
            dialog.Notification.Dismiss();dialog.ProviderChoice.SelectedItem="DeepSeek";CenteredDialogChecks.Ready(dialog);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=owner.CaptureRenderedFrame()!)frame.Save(Path.Combine(output,"ai-model-picker.png"),new PngBitmapEncoderOptions());
            await dialog.SaveAsync();await shown;
            check(saved?.Provider=="DeepSeek"&&saved.Profiles["OpenAI"].Model=="custom-manual"&&saved.Profiles["OpenAI"].Models.Contains("gpt-a")&&saved.Profiles["DeepSeek"].Models.Contains("deepseek-v4-pro"),"Saving persists per-provider model choices, cached availability and manual models together");
            await owner.Controller.ApplyAsync(owner.Controller.State with{Discovery=saved!});
            var persisted=new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Discovery;
            check(persisted.Provider=="DeepSeek"&&persisted.Profiles["OpenAI"].ApiKey==original.ApiKey&&persisted.Profiles["DeepSeek"].ApiKey==original.Profiles["DeepSeek"].ApiKey&&persisted.Profiles["OpenAI"].Models.Contains("gpt-a"),"The real encrypted profile roundtrip preserves both API keys and provider model lists");
            await owner.Controller.SaveSearchCriteriaAsync(new(){ModeId="budget"});
            check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Discovery.Profiles.Count==persisted.Profiles.Count,"Saving airport search criteria retains every configured API provider");
        }
        finally {dialog.ForceClose();owner.Close();await owner.Controller.DisposeAsync();}
        var automaticOwner=new MainWindow(true,new(){AutoUpdate=false,RestoreConnection=false});automaticOwner.Show();
        var handler=new AutomaticReply();var automatic=new AiSearchSettingsDialog(original,_=>Task.CompletedTask);
        automatic.ModelHttpClientFactory=()=>new HttpClient(handler,disposeHandler:false);var automaticShown=automatic.ShowDialog(automaticOwner);
        async Task Until(Func<bool> ready){using var stop=new CancellationTokenSource(5000);while(!ready())await Task.Delay(10,stop.Token);}
        try
        {
            await Until(()=>automatic.ModelField.Items.Contains("openai-detected"));CenteredDialogChecks.Ready(automatic);automaticOwner.UpdateLayout();Dispatcher.UIThread.RunJobs();Motion.SettlePaint(automaticOwner);
            check(handler.Requests==1&&automatic.ModelField.Text==original.Model,"Opening API settings automatically fetches models once without changing the selected model");
            check(!automatic.GetVisualDescendants().OfType<Button>().Any(b=>b.Content is TextBlock{Text:"检测模型"}),"API settings has no separate model-detection button or window");
            var input=automatic.ModelField.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="PART_EditableTextBox");
            var face=input.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="PART_BorderElement");
            check(automatic.ModelField.Bounds.Height==automatic.ProtocolChoice.Bounds.Height&&(face.Background is null||face.Background is ISolidColorBrush{Color.A:0}),"The model choice has the standard dropdown height and no dark inner textbox surface");
            automatic.ProviderChoice.SelectedItem="DeepSeek";await Until(()=>automatic.ModelField.Items.Contains("deepseek-detected"));
            check(handler.Requests==2&&!automatic.ModelField.Items.Contains("openai-detected")&&automatic.ModelField.Text=="deepseek-flash","Switching provider automatically loads the matching catalog and retains its saved model");
            await automatic.CloseSheetAsync();await automaticShown;
        }
        finally {automatic.ForceClose();automaticOwner.Close();await automaticOwner.Controller.DisposeAsync();}
    }
    private sealed class Reply(HttpStatusCode status,string text):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(text)});}
    private sealed class Pending(Task<HttpResponseMessage> result):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>result;}
    private sealed class AutomaticReply:HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests++;var model=request.RequestUri!.Host=="api.deepseek.com"?"deepseek-detected":"openai-detected";return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\""+model+"\"}]}")});}
    }
}
