using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class ImportChecks
{
    internal static int IKuuuCache(string dataDirectory, string reportPath)
    {
        using var mutex = new Mutex(true, "Local\\TIZI-" + Environment.UserName, out var created);
        if (!created) { Console.Error.WriteLine("Close TIZI before importing its saved configuration."); return 1; }
        try
        {
            string ProxySnapshot()
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                return string.Join('|', new[] { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect" }.Select(n => n + ":" + key?.GetValue(n)));
            }
            int[] EngineIds() => Process.GetProcesses().Select(p => { using (p) return p.ProcessName is "mihomo" or "iKuuuVPNCore" ? p.Id : (int?)null; }).Where(id => id.HasValue).Select(id => id!.Value).Order().ToArray();
            var proxy = ProxySnapshot(); var engines = EngineIds();
            var input = Console.In.ReadToEnd(); if (input.Length > NodeImport.MaxBytes + 4096) throw new InvalidDataException("Cached configuration too large.");
            var payload = JsonNode.Parse(input) as JsonObject ?? throw new InvalidDataException("Invalid cache payload.");
            var nodes = NodeImport.Parse(payload["content"]?.ToString() ?? "");
            var vault = new Vault(dataDirectory); var prior = vault.Read<AppState>("settings.dat") ?? new();
            var current = prior.Nodes.ToList(); var added = 0;
            foreach (var node in nodes)
                if (!current.Any(n => n.Name == node.Name && JsonNode.DeepEquals(n.Options, node.Options))) { current.Add(node with { SourceName = "iKuuu · local cache" }); added++; }
            if (current.Count > NodeImport.MaxNodes) throw new InvalidDataException("Maximum 3000 nodes.");
            var next = prior with { Nodes = current }; // Keep selected node, capture mode and every other preference.
            vault.Write("settings.dat", next);
            var saved = vault.Read<AppState>("settings.dat")!;
            var parametersPreserved = nodes.All(node => saved.Nodes.Any(n => n.Name == node.Name && JsonNode.DeepEquals(n.Options, node.Options)));
            var exported = ImportDocument.Parse(NodeExport.Json(nodes, []));
            var exportRoundtrip = nodes.Zip(exported.Nodes).All(p => p.First.Name == p.Second.Name && JsonNode.DeepEquals(p.First.Options, p.Second.Options));
            var proxyUnchanged = proxy == ProxySnapshot(); var enginesUnchanged = engines.SequenceEqual(EngineIds());
            if (!parametersPreserved || !exportRoundtrip || !proxyUnchanged || !enginesUnchanged) throw new InvalidDataException("Offline import verification failed.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = true, imported = nodes.Count, added, totalNodes = saved.Nodes.Count, protocols = nodes.GroupBy(n => n.Type).ToDictionary(g => g.Key, g => g.Count()), cachedAt = payload["lastUpdateDate"]?.ToString(), automaticSubscriptionUpdate = false, connected = false, latencyTested = false, parametersPreserved, exportRoundtrip, proxyUnchanged, enginesUnchanged, credentialsIncluded = false }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {nodes.Count} cached iKuuu nodes imported ({added} new); no connection or latency test."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("Offline iKuuu import failed: " + e.GetType().Name); return 1; }
        finally { mutex.ReleaseMutex(); }
    }
    // Deliberately contains no EngineClient, DNS request, latency test or proxy mutation.
    internal static int WireGuardSample(string firstImage, string secondImage, string dataDirectory, string searchVault, string reportPath)
    {
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidDataException(text); checks.Add(text); }
        string ProxySnapshot()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            return string.Join('|', new[] { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect" }.Select(n => n + ":" + key?.GetValue(n)));
        }
        var beforeProxy = ProxySnapshot();
        var enginesBefore = Process.GetProcessesByName("mihomo").Select(p => { using (p) return p.Id; }).Order().ToArray();
        try
        {
            var url = Console.ReadLine() ?? throw new InvalidDataException("Missing sample link.");
            var json = Console.ReadLine() ?? throw new InvalidDataException("Missing sample JSON.");
            var linkNode = ImportDocument.Parse(url).Nodes.Single(); var jsonNode = ImportDocument.Parse(json).Nodes.Single();
            Check(linkNode.Type == "wireguard" && jsonNode.Type == "wireguard", "URL and Shadowrocket JSON recognized as WireGuard");
            Check(JsonNode.DeepEquals(linkNode.Options, jsonNode.Options), "URL and JSON produce identical engine parameters");
            foreach (var file in new[] { firstImage, secondImage })
            {
                var decoded = QrImport.Decode(File.ReadAllBytes(file)); var qrNode = ImportDocument.Parse(decoded.Single()).Nodes.Single();
                Check(JsonNode.DeepEquals(qrNode.Options, linkNode.Options), "Attached QR image matches the link parameters");
            }
            Check(linkNode.CountryCode == "GB" && jsonNode.CountryCode == "GB", "GB flag metadata retained");
            Check(linkNode.Options["mtu"]!.GetValue<int>() == 1419 && linkNode.Options["dns"] is JsonArray { Count: 2 }, "MTU and both DNS servers retained with correct types");
            Check(linkNode.Options["udp"]!.GetValue<bool>() && linkNode.Options["remote-dns-resolve"]!.GetValue<bool>(), "UDP and imported DNS enabled in saved node options");
            var shared = NodeExport.ShareLink(linkNode); var roundtrip = ImportDocument.Parse(shared).Nodes.Single();
            Check(JsonNode.DeepEquals(linkNode.Options, roundtrip.Options), "WireGuard share-link export and reimport preserve every parameter");
            var qrRoundtrip = ImportDocument.Parse(QrImport.Decode(NodeExport.QrPng(shared)).Single()).Nodes.Single();
            Check(JsonNode.DeepEquals(linkNode.Options, qrRoundtrip.Options), "Exported QR decodes back to the same node");
            foreach (var exported in new[] { NodeExport.Json([linkNode], []), NodeExport.Yaml([linkNode], []) })
                Check(JsonNode.DeepEquals(linkNode.Options, ImportDocument.Parse(exported).Nodes.Single().Options), "Configuration export and reimport preserve every parameter");
            var vault = new Vault(dataDirectory); var prior = vault.Read<AppState>("settings.dat") ?? new AppState();
            var known = prior.Nodes.FirstOrDefault(n => JsonNode.DeepEquals(n.Options, linkNode.Options));
            var saved = known ?? linkNode with { Name = "WireGuard · GB" };
            var next = prior with { Nodes = known is null ? [.. prior.Nodes, saved] : prior.Nodes, SelectedNode = prior.SelectedNode ?? saved.Id };
            if (string.IsNullOrEmpty(prior.Discovery.ApiKey) && new Vault(searchVault).Read<AppState>("settings.dat") is { } testState)
                next = next with { Discovery = testState.Discovery with { Model = "gpt-5.4-mini" } };
            vault.Write("settings.dat", next);
            Check(vault.Read<AppState>("settings.dat")!.Nodes.Any(n => JsonNode.DeepEquals(n.Options, linkNode.Options)), "One node saved to encrypted user configuration");
            Check(ProxySnapshot() == beforeProxy, "Windows proxy settings unchanged");
            Check(enginesBefore.SequenceEqual(Process.GetProcessesByName("mihomo").Select(p => { using (p) return p.Id; }).Order()), "No proxy engine started");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = checks.Count, connected = false, latencyTested = false, credentialsIncluded = false, checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks.Count} offline sample checks. Imported one encrypted node; no connection."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("Offline import verification failed: " + e.GetType().Name); return 1; }
    }
}
