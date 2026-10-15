using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed class AiSearchSettingsDialog : CenteredDialog
{
    internal StableChoice ProviderChoice { get; } = new();
    internal StableChoice ProtocolChoice { get; } = new();
    internal TextBox EndpointField { get; } = new() { Classes={"text-input"} };
    internal ComboBox ModelField { get; } = new() { IsEditable=true,Height=36,HorizontalAlignment=HorizontalAlignment.Stretch,MaxDropDownHeight=300 };
    internal TextBox KeyField { get; } = new() { PasswordChar='●',Classes={"text-input"} };
    internal ClientNotification Notification { get; } = new();
    private string[] _protocols=[];
    private readonly Dictionary<string,AiApiProfile> _profiles;
    private string _provider;
    private bool _switching, _saving;
    private bool _closed, _detecting;
    private List<string> _models=[];
    private CancellationTokenSource? _modelsCancellation;
    private long _modelRequest;
    internal Func<HttpClient>? ModelHttpClientFactory { private get; set; }
    private readonly bool _detectOnOpen;
    private (string Provider,string Protocol,string Endpoint,string Key)? _autoConfiguration;
    internal Task SaveAsync() => _save();
    private readonly Func<Task> _save;
    internal AiSearchSettingsDialog(DiscoverySettings original,Func<DiscoverySettings,Task> apply,bool detectOnOpen=true)
    {
        _detectOnOpen=detectOnOpen;
        Title=UiText.Get("AI API settings");Width=620;MinWidth=460;MaxHeight=740;SizeToContent=SizeToContent.Height;
        Background=Skin.ChromeBrush;
        UiText.WireChoices(this);
        _profiles=new(original.Profiles);_provider=original.Provider;
        _profiles[_provider]=new(original.Protocol,original.Endpoint,original.Model,original.ApiKey){Models=_profiles.GetValueOrDefault(_provider)?.Models??[]};
        ProviderChoice.ItemsSource=AiApiProfiles.Presets.Select(p=>p.Name).ToArray();
        var body=new StackPanel{Margin=new(24),Spacing=16};
        void Field(string label,Control control)=>body.Children.Add(new StackPanel{Spacing=7,Children={Ui.Text(label,true),control}});
        Field("AI provider",ProviderChoice);Field("API protocol",ProtocolChoice);Field("API endpoint",EndpointField);Field("Search model",ModelField);Field("API key",KeyField);
        var description=Ui.Text("Keys are encrypted locally for each provider. Public web sources are collected separately when the API has no native search.",true,12);description.TextWrapping=TextWrapping.Wrap;body.Children.Add(description);
        var save=Ui.Button("Save",()=>{},true);var cancel=Ui.Button("Cancel",()=>Close());
        body.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Children={cancel,save}});
        Content=new Grid{Children={Ui.Scroll(body),Notification}};
        void Load(string provider)
        {
            var preset=AiApiProfiles.Presets.FirstOrDefault(p=>p.Name==provider)??AiApiProfiles.Presets[^1];
            var profile=_profiles.GetValueOrDefault(provider)??new(preset.Protocol,preset.Endpoint,preset.Model,"");
            _switching=true;_protocols=AiApiProfiles.ProtocolsFor(provider);
            ProtocolChoice.ItemsSource=_protocols.Select(p=>p switch {"responses"=>"Responses API","chat"=>"Chat Completions (compatible)","claude"=>"Anthropic Messages",_=>"Gemini GenerateContent"}).ToArray();
            ProtocolChoice.SelectedIndex=Math.Max(0,Array.IndexOf(_protocols,profile.Protocol));EndpointField.Text=profile.Endpoint.Length>0?profile.Endpoint:AiApiProfiles.PresetEndpoint(provider,_protocols[ProtocolChoice.SelectedIndex]);
            _models=(profile.Models??[]).Where(m=>m.Length is >0 and <=80&&!m.Any(char.IsControl)).Distinct().Take(500).ToList();
            ModelField.ItemsSource=_models.Append(profile.Model).Where(m=>m.Length>0).Distinct().ToArray();ModelField.Text=profile.Model;KeyField.Text=profile.ApiKey;_switching=false;
        }
        void Remember()=>_profiles[_provider]=new(_protocols[Math.Max(0,ProtocolChoice.SelectedIndex)],EndpointField.Text?.Trim()??"",ModelField.Text?.Trim()??"",KeyField.Text?.Trim()??""){Models=[.._models]};
        void ClearModels()
        {
            if(_switching||_saving)return;CancelModelDetection();_autoConfiguration=null;_models=[];var current=ModelField.Text;
            ModelField.ItemsSource=string.IsNullOrEmpty(current)?Array.Empty<string>():new[]{current};ModelField.Text=current;
        }
        EndpointField.PropertyChanged+=(_,change)=>{if(change.Property==TextBox.TextProperty)ClearModels();};
        KeyField.PropertyChanged+=(_,change)=>{if(change.Property==TextBox.TextProperty)ClearModels();};
        EndpointField.LostFocus+=(_,_)=>AutoDetect();KeyField.LostFocus+=(_,_)=>AutoDetect();
        ProtocolChoice.SelectionChanged+=(_,_)=>
        {
            if(_switching||_saving||ProtocolChoice.SelectedIndex<0)return;
            ClearModels();
            var endpoint=EndpointField.Text?.Trim()??"";
            if(endpoint.Length==0||_protocols.Any(p=>AiApiProfiles.PresetEndpoint(_provider,p)==endpoint))
                EndpointField.Text=AiApiProfiles.PresetEndpoint(_provider,_protocols[ProtocolChoice.SelectedIndex]);
            AutoDetect();
        };
        ProviderChoice.SelectionChanged+=(_,_)=>
        {
            if(_switching||_saving||ProviderChoice.SelectedItem is not string next||next==_provider)return;
            CancelModelDetection();Remember();_provider=next;Load(next);AutoDetect();
        };
        _switching=true;ProviderChoice.SelectedItem=AiApiProfiles.Presets.Any(p=>p.Name==_provider)?_provider:"Custom";_switching=false;Load(_provider);
        _save=async()=>
        {
            if(_saving)return;_saving=true;save.IsEnabled=cancel.IsEnabled=false;
            CancelModelDetection();
            try
            {
                Remember();var profile=_profiles[_provider];var settings=original with {Provider=_provider,Protocol=profile.Protocol,Endpoint=profile.Endpoint,Model=profile.Model,ApiKey=profile.ApiKey,Profiles=new(_profiles)};
                _=AiApiProfiles.Endpoint(settings);
                if(settings.Model.Length is 0 or >80||settings.Model.Any(char.IsControl))throw new FormatException("Enter a valid model name.");
                await apply(settings);_saving=false;await CloseSheetAsync();
            }
            catch(Exception error){Notification.Show(UiText.Get(error.Message));}
            finally{_saving=false;save.IsEnabled=cancel.IsEnabled=true;}
        };
        save.Click+=async(_,_)=>await SaveAsync();Closing+=(_,e)=>{if(_saving)e.Cancel=true;};
        Closed+=(_,_)=>{_closed=true;CancelModelDetection();};
        Opened+=(_,_)=>AutoDetect();
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!_saving){Close();e.Handled=true;}};
    }
    private void CancelModelDetection()
    {
        _modelRequest++;_modelsCancellation?.Cancel();_detecting=false;ToolTip.SetTip(ModelField,null);
    }
    private void AutoDetect(){if(_detectOnOpen&&IsVisible)_=DetectModelsAsync(true);}
    internal async Task DetectModelsAsync(bool automatic=false)
    {
        if(_saving||_closed||_detecting)return;
        var provider=_provider;var protocol=_protocols[Math.Max(0,ProtocolChoice.SelectedIndex)];var endpoint=EndpointField.Text?.Trim()??"";var key=KeyField.Text?.Trim()??"";
        if(automatic)
        {if(key.Length==0||_autoConfiguration==(provider,protocol,endpoint,key))return;_autoConfiguration=(provider,protocol,endpoint,key);}
        var settings=new DiscoverySettings{Provider=provider,Protocol=protocol,Endpoint=endpoint,Model=ModelField.Text?.Trim()??"",ApiKey=key};
        using var cancellation=new CancellationTokenSource();_modelsCancellation=cancellation;var request=++_modelRequest;_detecting=true;
        ToolTip.SetTip(ModelField,UiText.Get("Loading models…"));
        try
        {
            using var http=ModelHttpClientFactory?.Invoke()??new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(8)}){Timeout=Timeout.InfiniteTimeSpan};
            var found=await new AiModelCatalog(http).ListAsync(settings,cancellation.Token);
            if(_closed||request!=_modelRequest)return;
            if(provider!=_provider||protocol!=_protocols[Math.Max(0,ProtocolChoice.SelectedIndex)]||endpoint!=(EndpointField.Text?.Trim()??"")||key!=(KeyField.Text?.Trim()??""))return;
            var current=ModelField.Text;_models=found;ModelField.ItemsSource=found.Append(current??"").Where(m=>m.Length>0).Distinct().ToArray();ModelField.Text=current;
            ToolTip.SetTip(ModelField,UiText.Format("Detected {0} models.",found.Count));
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
        catch(Exception error)
        {
            if(!_closed&&request==_modelRequest)
            {
                var message=error is HttpRequestException http&&http.StatusCode is { } status?UiText.Format("Model detection failed (HTTP {0}). You can enter a model manually.",(int)status):UiText.Get(error is OperationCanceledException?"Model detection timed out. You can enter a model manually.":error.Message);
                ToolTip.SetTip(ModelField,message);if(!automatic)Notification.Show(message);
            }
        }
        finally
        {
            if(ReferenceEquals(_modelsCancellation,cancellation))_modelsCancellation=null;
            if(!_closed&&request==_modelRequest)_detecting=false;
        }
    }
}
