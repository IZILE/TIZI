using Avalonia;
using Avalonia.Controls;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tizi.Desktop;

/// <summary>Keep every measure of a tray menu anchored to the original physical cursor point.</summary>
internal static class TrayMenuPlacement
{
    private static readonly ConditionalWeakTable<Window, Placement> Attached = new();
    internal static bool IsTrayMenu(Window window) => window.GetType().Name == "TrayPopupRoot"
        || window.Name?.StartsWith("AvaloniaTrayPopupRoot_", StringComparison.Ordinal) == true;
    internal static PixelPoint PositionFor(PixelPoint anchor, PixelSize size, PixelRect area)
    {
        var x = Math.Clamp(anchor.X, area.X, Math.Max(area.X, area.Right - size.Width));
        var y = anchor.Y + size.Height <= area.Bottom ? anchor.Y : anchor.Y - size.Height;
        return new(x, Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
    }
    internal static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows() || Attached.TryGetValue(window, out _) || !GetCursorPos(out var cursor)) return;
        var anchor = new PixelPoint(cursor.X, cursor.Y);
        var screen = window.Screens.ScreenFromPoint(anchor) ?? window.Screens.Primary;
        if (screen is null) return;
        var placement = new Placement(anchor, screen.WorkingArea); Attached.Add(window, placement);
        Win32Properties.AddWndProcHookCallback(window, placement.Hook);
        window.Closed += (_, _) => { Win32Properties.RemoveWndProcHookCallback(window, placement.Hook); Attached.Remove(window); };
    }
    private sealed class Placement(PixelPoint anchor, PixelRect area)
    {
        internal nint Hook(nint window, uint message, nint wParam, nint lParam, ref bool handled)
        {
            if (message != 0x0046 || lParam == 0) return 0; // WM_WINDOWPOSCHANGING
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            var width = position.Cx; var height = position.Cy;
            if ((position.Flags & 0x0001) != 0 && GetWindowRect(window, out var rect))
            { width = rect.Right - rect.Left; height = rect.Bottom - rect.Top; }
            if (width <= 0 || height <= 0) return 0;
            var target = PositionFor(anchor, new PixelSize(width, height), area);
            position.X = target.X; position.Y = target.Y; position.Flags &= ~0x0002u; // clear SWP_NOMOVE
            Marshal.StructureToPtr(position, lParam, false);
            return 0;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { internal nint Hwnd, InsertAfter; internal int X, Y, Cx, Cy; internal uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rectangle rect);
}
