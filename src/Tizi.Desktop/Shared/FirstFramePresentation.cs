using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>Keep the first native surface composed out of view until its first actual draw completes.</summary>
internal sealed class FirstFramePresentation
{
    private const int TransitionsDisabled = 3, Cloak = 13;
    private readonly Window _window;
    private readonly Panel _root;
    private readonly FrameWitness _witness = new();
    private nint _handle;
    private int _previousTransitions;
    private bool _closed, _released;
    internal bool WasCloaked { get; private set; }
    internal bool FrameRendered { get; private set; }
    internal bool TimedOut { get; private set; }
    internal bool IsCloaked { get; private set; }
    internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal FirstFramePresentation(Window window, Panel root)
    { _window = window; _root = root; }

    internal void Prepare()
    {
        if (!OperatingSystem.IsWindows() || _window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } platform)
        { Completed.TrySetResult(); return; }
        _handle = platform.Handle;
        var cloak = 1;
        IsCloaked = WasCloaked = DwmSetWindowAttribute(_handle, Cloak, ref cloak, sizeof(int)) == 0;
        if (!IsCloaked) { Completed.TrySetResult(); return; }
        DwmGetWindowAttribute(_handle, TransitionsDisabled, out _previousTransitions, sizeof(int));
        var disabled = 1; DwmSetWindowAttribute(_handle, TransitionsDisabled, ref disabled, sizeof(int));
        _root.Children.Add(_witness);
        _window.Opened += Opened;
        _window.Closed += Closed;
    }

    internal void Abort() => Reveal();
    private void Closed(object? sender, EventArgs args) { _closed = true; Reveal(); }
    private void Opened(object? sender, EventArgs args)
    { Dispatcher.UIThread.Post(WaitForFrame, DispatcherPriority.Loaded); }

    private async void WaitForFrame()
    {
        try
        {
            if (_closed || _released) return;
            // Layout and animation callbacks are too early. The witness executes inside
            // the ready render target; Rendered then waits until the draw has been submitted.
            _window.UpdateLayout();
            await _witness.Drawn.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var compositor = ElementComposition.GetElementVisual(_window)?.Compositor;
            if (compositor is null) return;
            await compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(TimeSpan.FromSeconds(3));
            FrameRendered = true;
            DwmFlush();
        }
        catch (TimeoutException) { TimedOut = true; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Trace.WriteLine($"First frame: {error.Message}"); }
        finally { await Dispatcher.UIThread.InvokeAsync(Reveal); }
    }

    private void Reveal()
    {
        if (_released) return; _released = true;
        _window.Opened -= Opened; _window.Closed -= Closed;
        if (IsCloaked)
        {
            var cloak = 0; DwmSetWindowAttribute(_handle, Cloak, ref cloak, sizeof(int));
            DwmFlush();
            DwmSetWindowAttribute(_handle, TransitionsDisabled, ref _previousTransitions, sizeof(int));
            IsCloaked = false;
        }
        _root.Children.Remove(_witness);
        Completed.TrySetResult();
    }

    // This no-op operation changes no pixels and intercepts no input. Unlike Control.Render,
    // its callback executes on the renderer, after the native render target becomes ready.
    private sealed class FrameWitness : Control
    {
        internal TaskCompletionSource Drawn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal FrameWitness() { IsHitTestVisible = false; Focusable = false; }
        public override void Render(DrawingContext context) => context.Custom(new WitnessDraw(new Rect(Bounds.Size), Drawn));
    }
    private sealed class WitnessDraw(Rect bounds, TaskCompletionSource drawn) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Render(ImmediateDrawingContext context) => drawn.TrySetResult();
        public void Dispose() { }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
