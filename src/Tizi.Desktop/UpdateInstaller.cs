using System.Diagnostics;
using System.Security.Cryptography;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class UpdateInstaller
{
    internal static int Apply(int parentId, long parentTicks, string installer, string checksum, string installation)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(UserData.DirectoryPath, "updates")) + Path.DirectorySeparatorChar;
            var file = Path.GetFullPath(installer); var target = Path.GetFullPath(installation);
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file) != "TIZI-Setup.exe"
                || !File.Exists(Path.Combine(target, "TIZI.exe"))) return 2;
            try
            {
                using var parent = Process.GetProcessById(parentId);
                if (parent.StartTime.ToUniversalTime().Ticks != parentTicks || !string.Equals(parent.MainModule?.FileName, Path.Combine(target, "TIZI.exe"), StringComparison.OrdinalIgnoreCase)) return 3;
                if (!parent.WaitForExit(180000)) return 4;
            }
            catch (ArgumentException) { }
            if (!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(checksum, StringComparison.OrdinalIgnoreCase)) return 5;
            var start = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/DIR=" + target, "/LOG=" + Path.Combine(Path.GetDirectoryName(file)!, "install.log") }) start.ArgumentList.Add(arg);
            using var setup = Process.Start(start); if (setup is null) return 6; setup.WaitForExit(); if (setup.ExitCode != 0) return setup.ExitCode;
            using var app = Process.Start(new ProcessStartInfo(Path.Combine(target, "TIZI.exe")) { UseShellExecute = true }); return 0;
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(UserData.DirectoryPath, "update-error.log"), error.GetType().Name); } catch (IOException) { }
            return 1;
        }
    }
}
