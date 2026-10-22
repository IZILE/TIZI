using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private StartupAnimation? _startupAnimation;
    internal StartupAnimation? StartupOverlay=>_startupAnimation;
    internal bool StartupContentHidden=>_shellRoot?.Opacity==0&&_notification.Opacity==0;
    internal bool StartupContentVisible=>_shellRoot?.Opacity==1&&_notification.Opacity==1;
    private void PrepareStartupAnimation()
    {
        if(_preview||((Program.Headless||DesktopChecks.Active)&&!StartupAnimation.AllowVerification)||_shellRoot is null||_wordmarkHost is null||!StartupAnimation.AnimationsEnabled())return;
        // Opacity preserves layout and the logo's docking coordinates while
        // keeping every functional control out of the startup composition.
        _wordmarkHost.Opacity=0;_shellRoot.Opacity=0;_notification.Opacity=0;_shellRoot.IsHitTestVisible=false;
        var animation=_startupAnimation=new StartupAnimation(()=>_app.Ready||_app.LoadFailure is not null,
            ()=>_wordmarkHost.TranslatePoint(new(_wordmarkHost.Bounds.Width/2,_wordmarkHost.Bounds.Height/2),_clientSurface)??new(36,62),
            _=>{});
        animation.ZIndex=10;_clientSurface.Children.Add(animation);
        void Finish()
        {
            if(!ReferenceEquals(_startupAnimation,animation))return;
            _startupAnimation=null;_clientSurface.Children.Remove(animation);_shellRoot.Opacity=1;_notification.Opacity=1;_shellRoot.IsHitTestVisible=true;_wordmarkHost.Opacity=1;
        }
        animation.Completed+=Finish;Closed+=(_,_)=>animation.Finish();
        Opened+=async (_,_)=>
        {
            if(InitialPresentation is {} first)await first.Completed.Task;
            await Dispatcher.UIThread.InvokeAsync(()=>{if(!ReferenceEquals(_startupAnimation,animation))return;if(!IsVisible||WindowState==WindowState.Minimized)animation.Finish();else animation.Start();});
        };
    }
}
