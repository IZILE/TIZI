using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class AiModelCatalogChecks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        var original=new DiscoverySettings{ApiKey="synthetic-key-never-sent",Model="gpt-6-sol",Profiles=new(){["DeepSeek"]=new("chat","https://api.deepseek.com/chat/completions","deepseek-flash","private-fixture-credential"){Models=["deepseek-flash","deepseek-v4-pro"]}}};
        DiscoverySettings? saved=null;var owner=new MainWindow(true,new(){AutoUpdate=false,RestoreConnection=false});owner.Show();
        var dialog=new AiSearchSettingsDialog(original,value=>{saved=value;return Task.CompletedTask;},detectOnOpen:false);var shown=dialog.ShowDialog(owner);
        try
        {
            dialog.ModelHttpClientFactory=()=>new HttpClient(new Reply(HttpStatusCode.OK,"{\"data\":[{\"id\":\"gpt-6-sol\"},{\"id\":\"gpt-6-luna\"},{\"id\":\"gpt-audio\"},{\"id\":\"gpt-4o\"}]}"));
            await dialog.DetectModelsAsync();
            check(dialog.ModelField.Items.Contains("gpt-6-sol")&&dialog.ModelField.Items.Contains("gpt-6-luna")&&Equals(dialog.ModelField.SelectedItem,"gpt-6-sol")&&!dialog.ModelField.Items.Contains("gpt-audio")&&!dialog.ModelField.Items.Contains("gpt-4o"),"Model detection retains a valid selected text model while excluding audio and legacy choices");
            dialog.ModelField.SelectedItem="gpt-6-luna";dialog.UpdateLayout();Dispatcher.UIThread.RunJobs();
            check(Equals(dialog.ModelField.SelectedItem,"gpt-6-luna"),"Choosing a detected model changes the actual model field");
            dialog.ProviderChoice.SelectedItem="DeepSeek";
            check(dialog.KeyField.Text=="private-fixture-credential"&&Equals(dialog.ModelField.SelectedItem,"deepseek-flash")&&dialog.ModelField.Items.Contains("deepseek-v4-pro")&&!dialog.ModelField.Items.Contains("gpt-6-sol"),"Each provider restores its own secret, selected model and cached model list");
            var pending=new TaskCompletionSource<HttpResponseMessage>();dialog.ModelHttpClientFactory=()=>new HttpClient(new Pending(pending.Task));
            var detection=dialog.DetectModelsAsync();dialog.ProviderChoice.SelectedItem="OpenAI";
            pending.SetResult(new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"late-deepseek\"}]}")});await detection;
            check(Equals(dialog.ModelField.SelectedItem,"gpt-6-luna")&&dialog.ModelField.Items.Contains("gpt-6-sol")&&!dialog.ModelField.Items.Contains("late-deepseek"),"A late model response cannot replace another provider's model list after switching");
            dialog.ModelHttpClientFactory=()=>new HttpClient(new Reply(HttpStatusCode.Unauthorized,"{}"));await dialog.DetectModelsAsync();
            check(Equals(dialog.ModelField.SelectedItem,"gpt-6-luna")&&dialog.ModelField.Items.Contains("gpt-6-sol")&&dialog.Notification.IsError,"Failed model detection retains the selected model and cached list");
            dialog.Notification.Dismiss();dialog.ProviderChoice.SelectedItem="DeepSeek";CenteredDialogChecks.Ready(dialog);owner.UpdateLayout();Dispatcher.UIThread.RunJobs();
            await dialog.SaveAsync();await shown;
            check(saved?.Provider=="DeepSeek"&&saved.Profiles["OpenAI"].Model=="gpt-6-luna"&&saved.Profiles["OpenAI"].Models.Contains("gpt-6-sol")&&saved.Profiles["DeepSeek"].Models.Contains("deepseek-v4-pro"),"Saving persists per-provider selected models and concise catalogs together");
            await owner.Controller.ApplyAsync(owner.Controller.State with{Discovery=saved!});
            var persisted=new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Discovery;
            check(persisted.Provider=="DeepSeek"&&persisted.Profiles["OpenAI"].ApiKey==original.ApiKey&&persisted.Profiles["DeepSeek"].ApiKey==original.Profiles["DeepSeek"].ApiKey&&persisted.Profiles["OpenAI"].Models.Contains("gpt-6-sol"),"The real encrypted profile roundtrip preserves both API keys and provider model lists");
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
            await Until(()=>automatic.ModelField.Items.Contains("gpt-6-luna"));CenteredDialogChecks.Ready(automatic);automaticOwner.UpdateLayout();Dispatcher.UIThread.RunJobs();Motion.SettlePaint(automaticOwner);
            check(handler.Requests==1&&Equals(automatic.ModelField.SelectedItem,original.Model),"Opening API settings automatically fetches models once without changing a valid selected model");
            check(!automatic.GetVisualDescendants().OfType<Button>().Any(b=>b.Content is TextBlock{Text:"检测模型"}),"API settings has no separate model-detection button or window");
            var input=automatic.ModelField.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="PART_EditableTextBox");
            check(automatic.ModelField.Bounds.Height==automatic.ProtocolChoice.Bounds.Height&&!automatic.ModelField.IsEditable&&!input.IsVisible,"The model choice has the standard dropdown height and no editable input");
            void Top(StableChoice choice)
            {
                var order=choice.Items.Cast<object>().ToArray();choice.IsDropDownOpen=true;Dispatcher.UIThread.RunJobs();automaticOwner.UpdateLayout();Dispatcher.UIThread.RunJobs();
                var popup=choice.GetVisualDescendants().OfType<Popup>().Single();var scroll=popup.Child!.GetVisualDescendants().OfType<ScrollViewer>().Single();
                var row=choice.ContainerFromIndex(choice.SelectedIndex)!;
                check(row.Bounds.Height>0&&Math.Abs(row.TranslatePoint(default,scroll)!.Value.Y)<.5&&order.SequenceEqual(choice.Items.Cast<object>()),"Opening the selected "+(choice==automatic.ModelField?"model":"provider")+" aligns its visible row with the list top without sorting");
                var arrow=choice.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p=>p.Name=="choiceArrow");
                check(arrow.Transitions is null||!arrow.Transitions.Any(t=>t is Avalonia.Animation.TransformOperationsTransition),"Dropdown arrows have no rotating intermediate shape");
                PopupMotion.CloseImmediately(choice);
            }
            automatic.ModelField.SelectedIndex=automatic.ModelField.ItemCount-1;Top(automatic.ModelField);
            foreach(var x in new[]{8d,automatic.ModelField.Bounds.Width-8})
            {
                var point=automatic.ModelField.TranslatePoint(new(x,automatic.ModelField.Bounds.Height/2),automaticOwner)!.Value;
                automaticOwner.MouseMove(point);automaticOwner.MouseDown(point,MouseButton.Left);automaticOwner.MouseUp(point,MouseButton.Left);Dispatcher.UIThread.RunJobs();
                check(automatic.ModelField.IsDropDownOpen,"The entire model dropdown opens from its padded edge at "+x);
                PopupMotion.CloseImmediately(automatic.ModelField);
            }
            automatic.ModelField.SelectedItem="gpt-6-sol";Top(automatic.ModelField);Top(automatic.ProviderChoice);
            automatic.ProviderChoice.SelectedItem="DeepSeek";await Until(()=>automatic.ModelField.Items.Contains("deepseek-v4-pro"));
            check(handler.Requests==2&&!automatic.ModelField.Items.Contains("gpt-6-luna")&&Equals(automatic.ModelField.SelectedItem,"deepseek-flash"),"Switching provider automatically loads the matching catalog and retains its saved model");
            Top(automatic.ProviderChoice);automatic.ProviderChoice.SelectedItem="Custom";Top(automatic.ProviderChoice);automatic.ProviderChoice.SelectedItem="DeepSeek";
            automaticOwner.UpdateLayout();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var frame=automaticOwner.CaptureRenderedFrame()!)frame.Save(Path.Combine(output,"ai-model-picker.png"),new PngBitmapEncoderOptions());
            await automatic.CloseSheetAsync();await automaticShown;
        }
        finally {automatic.ForceClose();automaticOwner.Close();await automaticOwner.Controller.DisposeAsync();}
        var budgetOwner=new MainWindow(true,new(){AutoUpdate=false,RestoreConnection=false});budgetOwner.Show();
        var presets=AiApiProfiles.Presets.Where(p=>p.Name!="Custom").ToArray();
        var settings=new DiscoverySettings{ApiKey="synthetic-key-never-sent",Profiles=presets.ToDictionary(p=>p.Name,p=>new AiApiProfile(p.Protocol,p.Endpoint,p.Model,"synthetic-key-never-sent"))};
        var budget=new AiSearchSettingsDialog(settings,_=>Task.CompletedTask,detectOnOpen:false);var budgetShown=budget.ShowDialog(budgetOwner);
        var newer=new Dictionary<string,string>{{"OpenAI","gpt-6.1-mini"},{"DeepSeek","deepseek-v5-flash"},{"Qwen","qwen3.7-plus"},{"Kimi","kimi-k3"},{"GLM","glm-5.2-flash"},{"Gemini","gemini-3.8-flash"},{"Claude","claude-sonnet-4-6"}};
        try
        {
            foreach(var preset in presets)
            {
                budget.ProviderChoice.SelectedItem=preset.Name;
                var ids=new[]{preset.Model,newer[preset.Name]};
                var text=preset.Protocol=="gemini"?System.Text.Json.JsonSerializer.Serialize(new{models=ids.Select(id=>new{name="models/"+id,supportedGenerationMethods=new[]{"generateContent"}})}):System.Text.Json.JsonSerializer.Serialize(new{data=ids.Select(id=>new{id})});
                budget.ModelHttpClientFactory=()=>new HttpClient(new Reply(HttpStatusCode.OK,text));await budget.DetectModelsAsync();
                check(Equals(budget.ModelField.SelectedItem,preset.Model)&&ids.All(id=>budget.ModelField.Items.Contains(id)),preset.Name+" retains its available default alongside a newer model in the real dropdown");
            }
            budget.ProviderChoice.SelectedItem="OpenAI";
            budget.ModelHttpClientFactory=()=>new HttpClient(new Reply(HttpStatusCode.OK,"{\"data\":[{\"id\":\"gpt-5.4-mini\",\"deprecated\":true},{\"id\":\"gpt-6.1-mini\"}]}"));
            await budget.DetectModelsAsync();
            check(budget.ModelField.SelectedItem is null&&!budget.ModelField.Items.Contains("gpt-5.4-mini")&&budget.ModelField.Items.Contains("gpt-6.1-mini"),"A fresh catalog cannot restore a retired saved model or silently choose a replacement");
            await budget.CloseSheetAsync();await budgetShown;
        }
        finally {budget.ForceClose();budgetOwner.Close();await budgetOwner.Controller.DisposeAsync();}
    }
    private sealed class Reply(HttpStatusCode status,string text):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(text)});}
    private sealed class Pending(Task<HttpResponseMessage> result):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>result;}
    private sealed class AutomaticReply:HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests++;var models=request.RequestUri!.Host=="api.deepseek.com"?new[]{"deepseek-flash","deepseek-v4-pro"}:new[]{"gpt-6-sol","gpt-6-luna"};return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{data=models.Select(id=>new{id})}))});}
    }
}
