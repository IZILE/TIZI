using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace Tizi.Desktop;

// Headless verification routes hosted dialog input through its real main-window input root.
internal static class DialogCheckInput
{
    internal static void Show(this CenteredDialog dialog)
    {
        var fixture=new MainWindow(true,new Tizi.Core.AppState { AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false });
        fixture.Show();dialog.Closed+=(_,_)=>fixture.Close();dialog.Show(fixture);
    }
    private static TopLevel Root(Control control)=>TopLevel.GetTopLevel(control)??throw new InvalidOperationException("Detached input fixture");
    private static Point Point(Control control,Point point)=>control.TranslatePoint(point,Root(control))!.Value;
    internal static void MouseMove(this Control control,Point point,RawInputModifiers modifiers=RawInputModifiers.None)=>HeadlessWindowExtensions.MouseMove(Root(control),Point(control,point),modifiers);
    internal static void MouseDown(this Control control,Point point,MouseButton button,RawInputModifiers modifiers=RawInputModifiers.None)=>HeadlessWindowExtensions.MouseDown(Root(control),Point(control,point),button,modifiers);
    internal static void MouseUp(this Control control,Point point,MouseButton button,RawInputModifiers modifiers=RawInputModifiers.None)=>HeadlessWindowExtensions.MouseUp(Root(control),Point(control,point),button,modifiers);
    internal static void MouseWheel(this Control control,Point point,Vector delta,RawInputModifiers modifiers=RawInputModifiers.None)=>HeadlessWindowExtensions.MouseWheel(Root(control),Point(control,point),delta,modifiers);
    internal static void KeyPress(this Control control,Key key,RawInputModifiers modifiers=RawInputModifiers.None,PhysicalKey physicalKey=PhysicalKey.None,string? symbol=null)=>HeadlessWindowExtensions.KeyPress(Root(control),key,modifiers,physicalKey,symbol);
    internal static void KeyRelease(this Control control,Key key,RawInputModifiers modifiers=RawInputModifiers.None,PhysicalKey physicalKey=PhysicalKey.None,string? symbol=null)=>HeadlessWindowExtensions.KeyRelease(Root(control),key,modifiers,physicalKey,symbol);
    internal static WriteableBitmap? CaptureRenderedFrame(this Control control)=>HeadlessWindowExtensions.CaptureRenderedFrame(Root(control));
}
