using System.Text;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class ProfileRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TIZI-recovery-tests-" + Guid.NewGuid().ToString("N"));
    [Fact] public void MissingContentsMergeWithoutReplacingPreferencesOrCurrentItems()
    {
        var active = Node("Current", "current.example"); var old = Node("Old", "old.example");
        var subscription = new Subscription { Name = "Saved subscription", Url = "https://subscription.example/private" };
        old = old with { SubscriptionId = subscription.Id };
        var existingRule = new RoutingRule("DOMAIN", "current.example", "DIRECT"); var oldRule = new RoutingRule("DOMAIN", "old.example", "TIZI");
        var current = new AppState { Nodes = [active], SelectedNode = active.Id, MixedPort = 17897, Capture = CaptureMode.Tun, Rules = [existingRule], Discovery = new() { ApiKey = "current-private-key" } };
        var backup = new AppState { Nodes = [old], Subscriptions = [subscription], SelectedNode = old.Id, Rules = [oldRule] };
        var recovered = ProfileRecovery.MergeContents(current, backup);
        Assert.Equal(2, recovered.Nodes.Count); Assert.Single(recovered.Subscriptions); Assert.Equal(2, recovered.Rules.Count);
        Assert.Same(active, recovered.Nodes[0]); Assert.Equal(active.Id, recovered.SelectedNode); Assert.Equal(current.MixedPort, recovered.MixedPort);
        Assert.Equal(current.Capture, recovered.Capture); Assert.Same(current.Discovery, recovered.Discovery); Assert.Empty(current.Subscriptions);
        Assert.Equal(recovered, ProfileRecovery.MergeContents(recovered, backup) with { Nodes = recovered.Nodes, Subscriptions = recovered.Subscriptions, Rules = recovered.Rules });
    }
    [Fact] public void SameSubscriptionUrlKeepsCurrentIdAndRemapsRecoveredNodeOwner()
    {
        var currentSubscription = new Subscription { Name = "Current name", Url = "https://subscription.example/private" };
        var oldSubscription = currentSubscription with { Id = Guid.NewGuid(), Name = "Old name" };
        var node = Node("Recovered", "node.example") with { SubscriptionId = oldSubscription.Id };
        var restored = ProfileRecovery.MergeContents(new AppState { Subscriptions = [currentSubscription] }, new AppState { Subscriptions = [oldSubscription], Nodes = [node] });
        Assert.Single(restored.Subscriptions); Assert.Equal(currentSubscription, restored.Subscriptions[0]); Assert.Equal(currentSubscription.Id, restored.Nodes[0].SubscriptionId);
    }
    [Fact] public void AReimportedManualNodeDoesNotBecomeADuplicateDuringRecovery()
    {
        var current = Node("Current label", "same.example"); var previous = current with { Id = Guid.NewGuid(), Name = "Old label", Options = current.Options.DeepClone().AsObject() };
        previous.Options["name"] = "Old label";
        var restored = ProfileRecovery.MergeContents(new AppState { Nodes = [current], SelectedNode = current.Id }, new AppState { Nodes = [previous] });
        Assert.Single(restored.Nodes); Assert.Same(current, restored.Nodes[0]); Assert.Equal(current.Id, restored.SelectedNode);
    }
    [Fact] public void SavesKeepFiveEncryptedPreviousVersionsAndNoTemporaryFiles()
    {
        var vault = new Vault(_directory); var state = new AppState { Discovery = new() { ApiKey = "private-fixture-credential" } };
        for (var i = 0; i < 8; i++) vault.Write("settings.dat", state with { MixedPort = 17890 + i });
        var backups = vault.ProfileBackups(); Assert.Equal(5, backups.Count);
        Assert.Equal(17896, new Vault(Path.GetDirectoryName(backups[0])!).Read<AppState>(Path.GetFileName(backups[0]))!.MixedPort);
        foreach (var path in backups) Assert.DoesNotContain("private-fixture-credential", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }
    [Fact] public void ExplicitRecoveryPreservesUnreadableOriginalBytes()
    {
        Directory.CreateDirectory(_directory); var original = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(Path.Combine(_directory, "settings.dat"), original);
        var vault = new Vault(_directory); vault.Write("settings.dat", new AppState());
        Assert.Equal(original, File.ReadAllBytes(vault.ProfileBackups().Single())); Assert.NotNull(vault.Read<AppState>("settings.dat"));
    }
    private static ProxyNode Node(string name, string server) => new() { Name = name, Options = new JsonObject { ["name"] = name, ["type"] = "ss", ["server"] = server, ["port"] = 443, ["cipher"] = "aes-128-gcm", ["password"] = "fixture" } };
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
