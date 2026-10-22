using Avalonia.Controls;
using Avalonia.Media;
using System.Runtime.InteropServices;

namespace Tizi.Desktop;

/// <summary>Ignore the icon's mouse shortcut while retaining the native caption and keyboard menu.</summary>
internal static class NativeCaption
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Attached = new();
    private static readonly Dictionary<nint, nint> CaptionIcons = [];
    internal static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows() || Attached.TryGetValue(window, out _)
            || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" }) return;
        Attached.Add(window, new object());
        Win32Properties.AddWndProcHookCallback(window, Hook);
        if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle)
        {
            // Match the application chrome while preserving Windows caption controls.
            void Refresh()
            {
                var dark=1;DwmSetWindowAttribute(handle.Handle,20,ref dark,sizeof(int));
                var caption=CaptionColor(window);DwmSetWindowAttribute(handle.Handle,35,ref caption,sizeof(int));
            }
            Refresh();
            if (HideCaptionIcon(window))
            {
                // A null HICON falls back to Windows' generic document icon. Only the
                // caption-sized icon is transparent; ICON_BIG and ICON_SMALL2 remain
                // Avalonia's application icons for task switching and the shell.
                var transparent = CreateIcon(0, 16, 16, 1, 1, Enumerable.Repeat((byte)255, 32).ToArray(), new byte[32]);
                if (transparent != 0)
                {
                    CaptionIcons[handle.Handle] = transparent;
                    SendMessage(handle.Handle, 0x80, 0, transparent);
                }
            }
            window.Closed += (_, _) =>
            {
                Win32Properties.RemoveWndProcHookCallback(window, Hook);
                if (CaptionIcons.Remove(handle.Handle, out var icon)) DestroyIcon(icon);
                Attached.Remove(window);
            };
        }
    }
    internal static int CaptionColor(Window window)
    {
        var color = window.Background is ISolidColorBrush brush&&brush.Color.A==255 ? brush.Color : Skin.Chrome;
        return color.R | color.G << 8 | color.B << 16;
    }
    internal static bool HideCaptionIcon(Window window) => !TrayMenuPlacement.IsTrayMenu(window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern nint CreateIcon(nint instance, int width, int height, byte planes, byte bitsPerPixel, byte[] andMask, byte[] xorMask);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    internal static bool Suppress(uint message, nint hitTest) =>
        hitTest == 3 && message is >= 0x00A1 and <= 0x00A6; // HTSYSMENU, non-client mouse buttons.
    private static nint Hook(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (wParam == 0 && CaptionIcons.TryGetValue(window, out var icon))
        {
            if (message == 0x7F) { handled = true; return icon; } // WM_GETICON / ICON_SMALL
            if (message == 0x80) { handled = true; return DefWindowProc(window, message, wParam, icon); }
        }
        if (Suppress(message, wParam)) handled = true;
        return 0;
    }
}
