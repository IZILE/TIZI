using Avalonia.Controls;
using Avalonia.Threading;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private AboutPage? _aboutView;
    private AboutReturn? _aboutReturn;
    private sealed record AboutReturn(string Page,object Content,Control[] Actions,bool Search,bool Count,bool Paging);
    internal Task OpenAboutAsync()
    {
        if(_aboutView is not null)return Task.CompletedTask;
        ActiveMoreSheet?.DismissImmediately();
        if(_page=="Settings" && SettingsDirty)_notification.Show(UiText.Get("Unsaved changes"),isError:false,isWarning:true);
        _aboutReturn=new(_page,_pageHost.Content!,_pageActions.Children.ToArray(),_search.IsVisible,_count.IsVisible,_paging.IsVisible);
        _pageActions.Children.Clear();var back=Ui.Button("Back",()=>_ = ReturnFromAboutAsync());back.Tag="about-back";_pageActions.Children.Add(back);
        _pageActions.Children.Add(Ui.Button("Open-source project",()=>OpenUrl("https://github.com/IZILE/TIZI")));
        _pageActionsHost.IsVisible=true;_search.IsVisible=_count.IsVisible=_paging.IsVisible=false;
        UiText.Set(_heading,TextBlock.TextProperty,"About TIZI");
        var about=_aboutView=new AboutPage(AppVersion);_pageHost.Content=about;
        Dispatcher.UIThread.Post(()=> { if(ReferenceEquals(_aboutView,about))about.Reveal(); },DispatcherPriority.Loaded);
        Responsive();
        return Task.CompletedTask;
    }
    internal async Task ReturnFromAboutAsync()
    {
        if(_aboutView is not { IsHitTestVisible:true } about || _aboutReturn is not { } previous)return;
        about.Dismiss();await Task.Delay(160);
        if(_shutdown || !ReferenceEquals(_aboutView,about) || _page!=previous.Page)return;
        _aboutView=null;_aboutReturn=null;_pageHost.Content=previous.Content;_pageActions.Children.Clear();
        foreach(var action in previous.Actions)_pageActions.Children.Add(action);
        _pageActionsHost.IsVisible=previous.Actions.Length>0;_search.IsVisible=previous.Search;_count.IsVisible=previous.Count;_paging.IsVisible=previous.Paging;
        UiText.Set(_heading,TextBlock.TextProperty,_page=="Nodes" ? "Subscriptions and nodes" : _page);
        RefreshRows();Refresh();if(_page=="Logs")RefreshLog();UpdateSelectionActions();
    }
}
