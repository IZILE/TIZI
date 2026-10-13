using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>Keep the initial HWND composed but out of view until its content has actually rendered.</summary>
internal sealed class FirstFramePresentation
{
    private readonly Window _window;
    private IntPtr _handle;
    private bool _closed;
    internal bool WasCloaked { get; private set; }
    internal bool FrameRendered { get; private set; }
    internal bool TimedOut { get; private set; }
    internal bool IsCloaked { get; private set; }
    internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal FirstFramePresentation(Window window)
    {
        _window = window;
        window.Closed += (_,_) => { _closed = true; Reveal(); };
        window.Opened += (_,_) =>
        {
            if (IsCloaked) Dispatcher.UIThread.Post(WaitForFrame, DispatcherPriority.Loaded);
            else Completed.TrySetResult();
        };
    }

    internal void Prepare()
    {
        if (!OperatingSystem.IsWindows() || _window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } platform) return;
        _handle = platform.Handle;
        var cloak = 1;
        // DWMWA_CLOAK retains DWM composition, unlike Hide, and leaves the native caption intact.
        IsCloaked = WasCloaked = DwmSetWindowAttribute(_handle,13,ref cloak,sizeof(int)) == 0;
    }

    private async void WaitForFrame()
    {
        try
        {
            if (_closed) return;
            _window.UpdateLayout();
            var compositor = ElementComposition.GetElementVisual(_window)?.Compositor;
            if (compositor is null) return;
            var rendered = compositor.RequestCompositionBatchCommitAsync().Rendered;
            var done = await Task.WhenAny(rendered,Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            FrameRendered = done == rendered && rendered.IsCompletedSuccessfully;
            TimedOut = done != rendered;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { System.Diagnostics.Trace.WriteLine($"First frame: {error.Message}"); }
        finally { await Dispatcher.UIThread.InvokeAsync(Reveal); }
    }

    private void Reveal()
    {
        if (IsCloaked)
        {
            if (!_closed && FrameRendered) DwmFlush();
            var cloak = 0;
            DwmSetWindowAttribute(_handle,13,ref cloak,sizeof(int));
            IsCloaked = false;
        }
        Completed.TrySetResult();
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
