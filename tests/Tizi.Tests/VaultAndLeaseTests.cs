using System.Text;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class VaultAndLeaseTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TIZI-tests-" + Guid.NewGuid().ToString("N"));
    private Vault Vault => new(_directory);
    [Fact] public void StateIsEncryptedAndRoundTrips()
    {
        var state = new AppState { Subscriptions = [new Subscription { Name = "Test", Url = "https://example.com/secret-token" }] };
        Vault.Write("settings.dat", state); var bytes = File.ReadAllBytes(Path.Combine(_directory, "settings.dat")); Assert.DoesNotContain("secret-token", Encoding.UTF8.GetString(bytes)); Assert.Equal(state.Subscriptions[0].Url, Vault.Read<AppState>("settings.dat")!.Subscriptions[0].Url); Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
    [Fact] public void CorruptStateDoesNotBecomeEmptyState()
    {
        Vault.Write("settings.dat", new AppState()); var path = Path.Combine(_directory, "settings.dat"); var bytes = File.ReadAllBytes(path); bytes[^1] ^= 0x1; File.WriteAllBytes(path, bytes);
        Assert.ThrowsAny<Exception>(() => Vault.Read<AppState>("settings.dat")); Assert.True(File.Exists(path));
    }
    [Fact] public void ExactProxyAndPacRestoreUsesFakeRegistryOnly()
    {
        var registry = new FakeRegistry(); var before = registry.Read(); var lease = new ProxyLease(registry, Vault); lease.Acquire(17897);
        Assert.True(lease.IsActive); Assert.Equal("127.0.0.1:17897", registry.Values["ProxyServer"]!.Value.GetString()); Assert.Null(registry.Values["AutoConfigURL"]);
        Assert.True(lease.Restore()); Assert.False(lease.IsActive); foreach (var key in before.Keys) Assert.True(Same(before[key], registry.Values[key]));
    }
    [Fact] public void AnotherProxyProgramKeepsItsChanges()
    {
        var registry = new FakeRegistry(); var lease = new ProxyLease(registry, Vault); lease.Acquire(17897); registry.Values["ProxyServer"] = RegistryDatum.String("other-proxy:1080");
        Assert.False(lease.Restore()); Assert.Equal("other-proxy:1080", registry.Values["ProxyServer"]!.Value.GetString()); Assert.False(lease.IsActive);
    }
    [Fact] public void RecoveryAfterAppRestartRestoresPriorSettings()
    {
        var registry = new FakeRegistry(); var original = registry.Read(); new ProxyLease(registry, Vault).Acquire(17897);
        Assert.True(new ProxyLease(registry, Vault).Restore()); foreach (var key in original.Keys) Assert.True(Same(original[key], registry.Values[key]));
    }
    [Fact] public void UserChangesToIndividualValuesArePreserved()
    {
        var registry = new FakeRegistry(); var lease = new ProxyLease(registry, Vault); lease.Acquire(17897); registry.Values["ProxyOverride"] = RegistryDatum.String("user.example"); lease.Restore();
        Assert.Equal("user.example", registry.Values["ProxyOverride"]!.Value.GetString()); Assert.Equal("old.example:8888", registry.Values["ProxyServer"]!.Value.GetString());
    }
    [Fact] public void RejectedSystemProxySettingsCannotLookLikeSuccessfulCapture()
    {
        var registry = new FakeRegistry { IgnoreWrites = true }; var before = registry.Read();
        var lease = new ProxyLease(registry, Vault);
        Assert.Throws<ProxySettingsRejectedException>(() => lease.Acquire(17897)); Assert.False(lease.IsActive);
        foreach (var key in before.Keys) Assert.True(Same(before[key], registry.Values[key]));
    }
    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(9)] [InlineData(5)] [InlineData(13)]
    public void WinInetCaptureAndRestartRestorePreserveActualConnectionFlags(int flags)
    {
        var before = new WindowsProxyOptions(flags, "old.example:8888", "localhost;<local>", "https://pac.example/config");
        var api = new FakeOptions(before); var registry = new WindowsProxyRegistry(api);
        var lease = new ProxyLease(registry, Vault); lease.Acquire(17897);
        Assert.Equal(new WindowsProxyOptions(3, "127.0.0.1:17897", "localhost;127.*;[::1];<local>", null), api.Current);
        Assert.True(new ProxyLease(registry, Vault).Restore()); Assert.Equal(before, api.Current);
    }
    [Fact] public void EmptyWinInetStringsAndJsonEscapingDoNotRejectEquivalentSettings()
    {
        var api = new FakeOptions(new(1, "", "", "")) { EmptyStringsOnRead = true };
        var lease = new ProxyLease(new WindowsProxyRegistry(api), Vault); lease.Acquire(17897); Assert.True(lease.Restore());
        using var json = System.Text.Json.JsonDocument.Parse("\"localhost;<local>\"");
        Assert.True(RegistryDatum.String("localhost;<local>").Same(new(1, json.RootElement.Clone())));
    }
    [Fact] public void WinInetDoesNotOverwriteAnotherApplicationsProxyOrUserBypassChange()
    {
        var api = new FakeOptions(new(9, "old:80", "before", null));
        var lease = new ProxyLease(new WindowsProxyRegistry(api), Vault); lease.Acquire(17897);
        api.Current = api.Current with { Bypass = "user.example" };
        Assert.True(lease.Restore()); Assert.Equal(9, api.Current.Flags); Assert.Equal("user.example", api.Current.Bypass);
        lease.Acquire(17897); api.Current = api.Current with { Server = "other:1080", Flags = 3 };
        Assert.False(lease.Restore()); Assert.Equal("other:1080", api.Current.Server);
    }
    [Fact] public void RejectedWinInetReadbackReportsOnlyOptionNamesAndLeavesOriginalSettings()
    {
        var original = new WindowsProxyOptions(9, "old:80", null, null);
        var api = new FakeOptions(original) { IgnoreWrites = true }; var lease = new ProxyLease(new WindowsProxyRegistry(api), Vault);
        var error = Assert.Throws<ProxySettingsRejectedException>(() => lease.Acquire(17897));
        Assert.Contains("ConnectionFlags", error.MismatchedOptions); Assert.Equal(original, api.Current); Assert.False(lease.IsActive);
    }
    [Fact] public void CurrentWindowsProxyCanBeQueriedWithoutChangingItsSettings()
    {
        var registry = new WindowsProxyRegistry(); var first = registry.Read(); var second = registry.Read();
        Assert.Equal(WindowsProxyRegistry.Keys.Length, first.Count);
        Assert.All(first, p => Assert.True(Same(p.Value, second[p.Key])));
        Assert.Equal((first["ConnectionFlags"]!.Value.GetInt32() & 8) != 0 ? 1 : 0, first["AutoDetect"]!.Value.GetInt32());
    }
    private sealed class FakeOptions(WindowsProxyOptions current) : IWindowsProxyOptions
    {
        public WindowsProxyOptions Current { get; set; } = current;
        public bool EmptyStringsOnRead { get; init; }
        public bool IgnoreWrites { get; init; }
        public WindowsProxyOptions Read() => EmptyStringsOnRead ? Current with { Server = Current.Server ?? "", Bypass = Current.Bypass ?? "", AutoConfigUrl = Current.AutoConfigUrl ?? "" } : Current;
        public void Write(WindowsProxyOptions options) { if (!IgnoreWrites) Current = options; }
    }
    private static bool Same(RegistryDatum? a, RegistryDatum? b) => a is null ? b is null : a.Same(b);
    private sealed class FakeRegistry : IProxyRegistry
    {
        public bool IgnoreWrites { get; init; }
        public Dictionary<string, RegistryDatum?> Values { get; } = new() { ["ProxyEnable"] = RegistryDatum.DWord(1), ["ProxyServer"] = RegistryDatum.String("old.example:8888"), ["ProxyOverride"] = null, ["AutoConfigURL"] = RegistryDatum.String("https://pac.example/private"), ["AutoDetect"] = null };
        public Dictionary<string, RegistryDatum?> Read() => new(Values);
        public void Write(IReadOnlyDictionary<string, RegistryDatum?> values) { if (!IgnoreWrites) foreach (var p in values) Values[p.Key] = p.Value; }
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
