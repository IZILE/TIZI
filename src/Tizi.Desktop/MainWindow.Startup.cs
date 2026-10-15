using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private StartupAnimation? _startupAnimation;
    private readonly BlurEffect _startupBlur=new(){Radius=16};
    internal StartupAnimation? StartupOverlay=>_startupAnimation;
    private void PrepareStartupAnimation()
    {
        if(_preview||((Program.Headless||DesktopChecks.Active)&&!StartupAnimation.AllowVerification)||_shellRoot is null||_wordmarkHost is null||!StartupAnimation.AnimationsEnabled())return;
        _wordmarkHost.Opacity=0;_shellRoot.Effect=_startupBlur;_shellRoot.IsHitTestVisible=false;
        var animation=_startupAnimation=new StartupAnimation(()=>_app.Ready||_app.LoadFailure is not null,
            ()=>_wordmarkHost.TranslatePoint(new(_wordmarkHost.Bounds.Width/2,_wordmarkHost.Bounds.Height/2),_clientSurface)??new(36,62),
            frost=>{_startupBlur.Radius=16*frost;});
        animation.ZIndex=10;_clientSurface.Children.Add(animation);
        void Finish()
        {
            if(!ReferenceEquals(_startupAnimation,animation))return;
            _startupAnimation=null;_clientSurface.Children.Remove(animation);_shellRoot.Effect=null;_shellRoot.IsHitTestVisible=true;_wordmarkHost.Opacity=1;
        }
        animation.Completed+=Finish;Closed+=(_,_)=>animation.Finish();
        Opened+=async (_,_)=>
        {
            if(InitialPresentation is {} first)await first.Completed.Task;
            await Dispatcher.UIThread.InvokeAsync(()=>{if(!ReferenceEquals(_startupAnimation,animation))return;if(!IsVisible||WindowState==WindowState.Minimized)animation.Finish();else animation.Start();});
        };
    }
}
