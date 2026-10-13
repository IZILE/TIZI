using Tizi.Core;

namespace Tizi.Desktop;

// An interactive build check using the normal Windows application and an isolated vault.
// It never restores a personal profile, changes startup settings, or permits a connection.
internal static class DesktopChecks
{
    internal static bool Active { get; private set; }
    internal static string? PipeName { get; private set; }
    internal static void Prepare(string output)
    {
        Active = true; PipeName = "TIZI-desktop-check-" + Guid.NewGuid().ToString("N");
        UserData.DirectoryPath = Path.Combine(Path.GetFullPath(output), "settings");
        Directory.CreateDirectory(UserData.DirectoryPath);
        var primary = new Subscription { Name = "桌面检查订阅", Url = "https://fixture.example/subscription", Usage = new(0, 89_000_000_000, 300_000_000_000, DateTimeOffset.Now.AddDays(30)) };
        var other = new Subscription { Name = "其他订阅", Url = "https://other.example/subscription" };
        var nodes = NodeImport.Parse("ss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@jp.example.net:443#Japan%20%7C%2001\nss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@sg.example.net:8443#Singapore%20%7C%2002\nss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@us.example.net:443#United%20States%20%7C%2003\nss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@gb.example.net:443#Other%20subscription");
        for (var i = 0; i < nodes.Count; i++) nodes[i] = nodes[i] with { SubscriptionId = i < 3 ? primary.Id : other.Id, CountryCode = new[] { "JP", "SG", "US", "GB" }[i] };
        var state = new AppState
        {
            Nodes = nodes, SelectedNode = nodes[0].Id, Subscriptions = [primary, other],
            Providers = Enumerable.Range(1, 8).Select(i => new ProviderSite { Name = "示例机场 " + i, Url = $"https://provider{i}.example/", Region = "示例资料" }).ToList(),
            Rules = Enumerable.Range(0, 9233).Select(i => new RoutingRule("DOMAIN", $"fixture{i}.example", "DIRECT")).ToList(),
            AutoUpdate = false, AutoUpdateProviders = false, StartHidden = false, LastConnected = false, RestoreConnection = false
        };
        new Vault(UserData.DirectoryPath).Write("settings.dat", state);
    }
}
