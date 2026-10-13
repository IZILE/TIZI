using Microsoft.Win32;
using System.Text.Json;

namespace Tizi.Core;

public sealed record RegistryDatum(int Kind, JsonElement Value)
{
    public static RegistryDatum String(string value) => new((int)RegistryValueKind.String, JsonSerializer.SerializeToElement(value));
    public static RegistryDatum DWord(int value) => new((int)RegistryValueKind.DWord, JsonSerializer.SerializeToElement(value));
    public bool Same(RegistryDatum? other) => other is not null && Kind == other.Kind && JsonElement.DeepEquals(Value, other.Value);
}
public interface IProxyRegistry
{
    Dictionary<string, RegistryDatum?> Read();
    void Write(IReadOnlyDictionary<string, RegistryDatum?> values);
}
public sealed class ProxySettingsRejectedException(IEnumerable<string> mismatched) : IOException("Windows proxy settings did not accept the requested values.")
{ public string[] MismatchedOptions { get; } = mismatched.ToArray(); }
public sealed record ProxyJournal(Dictionary<string, RegistryDatum?> Before, Dictionary<string, RegistryDatum?> Applied);
/// <summary>Write-ahead journal; restore only settings still owned by this app.</summary>
public sealed class ProxyLease(IProxyRegistry registry, Vault vault)
{
    public bool IsActive => vault.Exists("proxy-lease.dat");
    public void Acquire(int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (IsActive) throw new InvalidOperationException("Restore the previous proxy session first.");
        var before = registry.Read();
        var applied = new Dictionary<string, RegistryDatum?> { ["ProxyEnable"] = RegistryDatum.DWord(1), ["ProxyServer"] = RegistryDatum.String($"127.0.0.1:{port}"), ["ProxyOverride"] = RegistryDatum.String("localhost;127.*;[::1];<local>"), ["AutoConfigURL"] = null, ["AutoDetect"] = RegistryDatum.DWord(0) };
        if (before.ContainsKey("ConnectionFlags")) applied["ConnectionFlags"] = RegistryDatum.DWord(3);
        vault.Write("proxy-lease.dat", new ProxyJournal(before, applied));
        try
        {
            registry.Write(applied);
            var actual = registry.Read();
            var mismatched = applied.Where(p => !Equal(p.Value, actual.GetValueOrDefault(p.Key))).Select(p => p.Key).ToArray();
            if (mismatched.Length > 0) throw new ProxySettingsRejectedException(mismatched);
        }
        catch { Restore(); throw; }
    }
    public bool Restore()
    {
        var journal = vault.Read<ProxyJournal>("proxy-lease.dat"); if (journal is null) return false;
        var current = registry.Read();
        // Another proxy app may have taken control. In that case leave its settings intact.
        if (!Equal(current.GetValueOrDefault("ProxyServer"), journal.Applied.GetValueOrDefault("ProxyServer"))) { vault.Delete("proxy-lease.dat"); return false; }
        var restore = journal.Before.Where(pair => Equal(current.GetValueOrDefault(pair.Key), journal.Applied.GetValueOrDefault(pair.Key))).ToDictionary(pair => pair.Key, pair => pair.Value);
        registry.Write(restore); vault.Delete("proxy-lease.dat"); return true;
    }
    private static bool Equal(RegistryDatum? a, RegistryDatum? b) => a is null ? b is null : a.Same(b);
}
