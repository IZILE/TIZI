using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tizi.Core;

public sealed record WindowsProxyOptions(int Flags, string? Server, string? Bypass, string? AutoConfigUrl);
public interface IWindowsProxyOptions
{
    WindowsProxyOptions Read();
    void Write(WindowsProxyOptions options);
}

// Keep the journal format compatible with earlier releases, but use the supported
// WinINet API as the authority, including PAC and automatic-detection flags.
public sealed class WindowsProxyRegistry(IWindowsProxyOptions options) : IProxyRegistry
{
    public WindowsProxyRegistry() : this(new WinInetProxyOptions()) { }
    public static readonly string[] Keys = ["ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect", "ConnectionFlags"];
    public Dictionary<string, RegistryDatum?> Read()
    {
        var current = options.Read();
        static RegistryDatum? Text(string? value) => string.IsNullOrEmpty(value) ? null : RegistryDatum.String(value);
        return new() { ["ProxyEnable"] = RegistryDatum.DWord((current.Flags & 2) != 0 ? 1 : 0),
            ["ProxyServer"] = Text(current.Server), ["ProxyOverride"] = Text(current.Bypass),
            ["AutoConfigURL"] = Text(current.AutoConfigUrl), ["AutoDetect"] = RegistryDatum.DWord((current.Flags & 8) != 0 ? 1 : 0),
            ["ConnectionFlags"] = RegistryDatum.DWord(current.Flags) };
    }
    public void Write(IReadOnlyDictionary<string, RegistryDatum?> values)
    {
        var current = options.Read();
        string? Text(string name, string? previous) => values.TryGetValue(name, out var datum) ? datum?.Value.GetString() : previous;
        var flags = current.Flags;
        if (values.TryGetValue("ConnectionFlags", out var exact) && exact is not null) flags = exact.Value.GetInt32();
        else
        {
            foreach (var (name, bit) in new[] { ("ProxyEnable", 2), ("AutoDetect", 8) })
                if (values.TryGetValue(name, out var datum)) flags = datum?.Value.GetInt32() > 0 ? flags | bit : flags & ~bit;
            if (values.TryGetValue("AutoConfigURL", out var pac)) flags = string.IsNullOrEmpty(pac?.Value.GetString()) ? flags & ~4 : flags | 4;
        }
        options.Write(new(flags, Text("ProxyServer", current.Server), Text("ProxyOverride", current.Bypass), Text("AutoConfigURL", current.AutoConfigUrl)));
    }
}

internal sealed class WinInetProxyOptions : IWindowsProxyOptions
{
    private const int PerConnectionOption = 75;
    public WindowsProxyOptions Read()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // FLAGS_UI is the user-selected connection mode. Fall back only on old Windows.
        try { return Query(10); }
        catch (Win32Exception error) when (error.NativeErrorCode is 87 or 12009) { return Query(1); }
    }
    private static WindowsProxyOptions Query(int flagsOption)
    {
        var stride = Marshal.SizeOf<NativeOption>(); var buffer = Marshal.AllocHGlobal(stride * 4);
        try
        {
            var ids = new[] { flagsOption, 2, 3, 4 };
            for (var i = 0; i < ids.Length; i++) Marshal.StructureToPtr(new NativeOption { Option = ids[i] }, buffer + stride * i, false);
            var list = new NativeList { Size = Marshal.SizeOf<NativeList>(), Count = 4, Options = buffer };
            var size = list.Size;
            if (!InternetQueryOption(IntPtr.Zero, PerConnectionOption, ref list, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            string? Text(int index) => Marshal.PtrToStringUni(Marshal.PtrToStructure<NativeOption>(buffer + stride * index).Value.Text);
            return new(Marshal.PtrToStructure<NativeOption>(buffer).Value.Number, Text(1), Text(2), Text(3));
        }
        finally
        {
            for (var i = 1; i < 4; i++) { var pointer = Marshal.PtrToStructure<NativeOption>(buffer + stride * i).Value.Text; if (pointer != IntPtr.Zero) GlobalFree(pointer); }
            Marshal.FreeHGlobal(buffer);
        }
    }
    public void Write(WindowsProxyOptions options)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var stride = Marshal.SizeOf<NativeOption>(); var buffer = Marshal.AllocHGlobal(stride * 4);
        var strings = new[] { options.Server, options.Bypass, options.AutoConfigUrl }.Select(value => Marshal.StringToHGlobalUni(value ?? "")).ToArray();
        try
        {
            Marshal.StructureToPtr(new NativeOption { Option = 1, Value = new() { Number = options.Flags } }, buffer, false);
            for (var i = 0; i < strings.Length; i++) Marshal.StructureToPtr(new NativeOption { Option = i + 2, Value = new() { Text = strings[i] } }, buffer + stride * (i + 1), false);
            var list = new NativeList { Size = Marshal.SizeOf<NativeList>(), Count = 4, Options = buffer };
            if (!InternetSetConnectionOption(IntPtr.Zero, PerConnectionOption, ref list, list.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0) || !InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { foreach (var pointer in strings) Marshal.FreeHGlobal(pointer); Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeList { public int Size; public IntPtr Connection; public int Count; public int Error; public IntPtr Options; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeOption { public int Option; public NativeValue Value; }
    [StructLayout(LayoutKind.Explicit)] private struct NativeValue { [FieldOffset(0)] public int Number; [FieldOffset(0)] public IntPtr Text; [FieldOffset(0)] public long FileTime; }
    [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetQueryOption(IntPtr handle, int option, ref NativeList buffer, ref int size);
    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetConnectionOption(IntPtr handle, int option, ref NativeList buffer, int size);
    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
