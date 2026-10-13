using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tizi.Core;

/// <summary>User-scoped Windows DPAPI. Credentials never fall back to plaintext.</summary>
public sealed class Vault(string directory)
{
    public string DirectoryPath { get; } = directory;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, MaxDepth = 32 };
    public bool Exists(string name) => System.IO.File.Exists(Path.Combine(DirectoryPath, name));
    public T? Read<T>(string name)
    {
        lock (_gate)
        {
            var path = Path.Combine(DirectoryPath, name);
            if (!System.IO.File.Exists(path)) return default;
            var bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length > 24 * 1024 * 1024) throw new InvalidDataException("Saved configuration is too large.");
            var clear = Protect(bytes, false);
            try { return JsonSerializer.Deserialize<T>(clear, JsonOptions) ?? throw new InvalidDataException("Saved configuration is empty."); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
    }
    public void Write<T>(string name, T value)
    {
        lock (_gate)
        {
            var clear = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            byte[] cipher;
            try { cipher = Protect(clear, true); }
            finally { CryptographicOperations.ZeroMemory(clear); }
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, name); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                System.IO.File.WriteAllBytes(temp, cipher);
                if (name == "settings.dat" && System.IO.File.Exists(path)) PreserveProfile(path);
                System.IO.File.Move(temp, path, true);
            }
            finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
        }
    }
    public IReadOnlyList<string> ProfileBackups()
    {
        var directory = Path.Combine(DirectoryPath, "backups");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "settings-*.dat").OrderDescending(StringComparer.Ordinal).ToArray() : [];
    }
    private void PreserveProfile(string path)
    {
        // Retain encrypted bytes, including an unreadable original during explicit recovery.
        // A failed backup prevents the replacement instead of silently risking the profile.
        var directory = Path.Combine(DirectoryPath, "backups"); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "settings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".dat");
        System.IO.File.Copy(path, target);
        foreach (var stale in ProfileBackups().Skip(5)) System.IO.File.Delete(stale);
    }
    public void Delete(string name) { lock (_gate) System.IO.File.Delete(Path.Combine(DirectoryPath, name)); }
    private static byte[] Protect(byte[] value, bool encrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("TIZI credentials require Windows DPAPI.");
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        var entropyBytes = Encoding.UTF8.GetBytes("TIZI.config.v1");
        var entropy = new Blob { Length = entropyBytes.Length, Data = Marshal.AllocHGlobal(entropyBytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length); Marshal.Copy(entropyBytes, 0, entropy.Data, entropy.Length);
            var ok = encrypt ? CryptProtectData(ref input, "TIZI", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows could not decrypt or protect TIZI settings.", new Win32Exception(Marshal.GetLastWin32Error()));
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var index = 0; index < input.Length; index++) Marshal.WriteByte(input.Data, index, 0);
            Marshal.FreeHGlobal(input.Data); Marshal.FreeHGlobal(entropy.Data);
            if (output.Data != IntPtr.Zero) { if (!encrypt) for (var index = 0; index < output.Length; index++) Marshal.WriteByte(output.Data, index, 0); LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob data, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
