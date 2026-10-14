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
    internal TextBox ModelField { get; } = new() { Classes={"text-input"} };
    internal TextBox KeyField { get; } = new() { PasswordChar='●',Classes={"text-input"} };
    internal ClientNotification Notification { get; } = new();
    private string[] _protocols=[];
    private readonly Dictionary<string,AiApiProfile> _profiles;
    private string _provider;
    private bool _switching, _saving;
    internal Task SaveAsync() => _save();
    private readonly Func<Task> _save;
    internal AiSearchSettingsDialog(DiscoverySettings original,Func<DiscoverySettings,Task> apply)
    {
        Title=UiText.Get("AI API settings");Width=620;MinWidth=460;MaxHeight=740;SizeToContent=SizeToContent.Height;
        Background=Skin.ChromeBrush;
        UiText.WireChoices(this);
        _profiles=new(original.Profiles);_provider=original.Provider;
        _profiles[_provider]=new(original.Protocol,original.Endpoint,original.Model,original.ApiKey);
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
            ProtocolChoice.SelectedIndex=Math.Max(0,Array.IndexOf(_protocols,profile.Protocol));EndpointField.Text=profile.Endpoint.Length>0?profile.Endpoint:AiApiProfiles.PresetEndpoint(provider,_protocols[ProtocolChoice.SelectedIndex]);ModelField.Text=profile.Model;KeyField.Text=profile.ApiKey;_switching=false;
        }
        void Remember()=>_profiles[_provider]=new(_protocols[Math.Max(0,ProtocolChoice.SelectedIndex)],EndpointField.Text?.Trim()??"",ModelField.Text?.Trim()??"",KeyField.Text?.Trim()??"");
        ProtocolChoice.SelectionChanged+=(_,_)=>
        {
            if(_switching||_saving||ProtocolChoice.SelectedIndex<0)return;
            var endpoint=EndpointField.Text?.Trim()??"";
            if(endpoint.Length==0||_protocols.Any(p=>AiApiProfiles.PresetEndpoint(_provider,p)==endpoint))
                EndpointField.Text=AiApiProfiles.PresetEndpoint(_provider,_protocols[ProtocolChoice.SelectedIndex]);
        };
        ProviderChoice.SelectionChanged+=(_,_)=>
        {
            if(_switching||_saving||ProviderChoice.SelectedItem is not string next||next==_provider)return;
            Remember();_provider=next;Load(next);
        };
        _switching=true;ProviderChoice.SelectedItem=AiApiProfiles.Presets.Any(p=>p.Name==_provider)?_provider:"Custom";_switching=false;Load(_provider);
        _save=async()=>
        {
            if(_saving)return;_saving=true;save.IsEnabled=cancel.IsEnabled=false;
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
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!_saving){Close();e.Handled=true;}};
    }
}
