using Microsoft.Win32;

namespace Tizi.Core;

public static class LoginStartup
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Set(bool enabled, string executable, bool hidden)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (executable.Contains('"') || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new IOException("Application path unavailable.");
        using var key = Registry.CurrentUser.CreateSubKey(Run);
        if (enabled) key.SetValue("TIZI", Command(executable, hidden), RegistryValueKind.String); else key.DeleteValue("TIZI", false);
    }
    public static string Command(string executable, bool hidden) => $"\"{executable}\"" + (hidden ? " --background" : "");
}
