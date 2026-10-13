using System.Text.Json;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class DiscoveryChecks
{
    internal static int Configure(string path)
    {
        var key = Console.ReadLine()?.Trim() ?? "";
        if (key.Length < 20 || key.Length > 1024 || key.Any(char.IsControl)) return 1;
        var vault = new Vault(path); var state = vault.Read<AppState>("settings.dat") ?? new();
        vault.Write("settings.dat", state with { Discovery = state.Discovery with { ApiKey = key } });
        Console.WriteLine("Search key saved using Windows DPAPI."); return 0;
    }
    internal static async Task<int> VerifyAsync(string path, string output)
    {
        Directory.CreateDirectory(output); var state = new Vault(path).Read<AppState>("settings.dat") ?? throw new InvalidOperationException("No search settings."); state = state with { Discovery = state.Discovery with { Model = "gpt-5.4-mini" } };
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(12) }) { Timeout = Timeout.InfiniteTimeSpan };
            var criteria = new ProviderSearchOptions { ModeId = "budget", MonthlyBudget = 30, Currency = "CNY", Regions = "香港、日本、新加坡" };
            var results = await new ProviderDiscovery(http, json => File.WriteAllText(Path.Combine(output, "response.json"), json)).SearchAsync(state.Discovery, ProviderSearchModes.BuildQuery(criteria), 2);
            if (results.Count == 0) throw new FormatException("No personal subscription provider passed the evidence checks.");
            File.WriteAllText(Path.Combine(output, "public-provider-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "discovery-check.json"), JsonSerializer.Serialize(new { passed = true, results = results.Count, plans = results.Sum(r => r.Profile?.Plans.Count ?? 0), seconds = started.Elapsed.TotalSeconds, systemProxyChanged = false, model = state.Discovery.Model }));
            Console.WriteLine($"PASS: authenticated live search, {results.Count} source-backed providers, {results.Sum(p => p.Profile?.Plans.Count ?? 0)} plans."); return 0;
        }
        catch (Exception e)
        {
            // Error text never contains response bodies, headers, credentials or subscription links.
            var message = e is HttpRequestException or FormatException ? e.Message : e.GetType().Name + ": search could not be verified.";
            File.WriteAllText(Path.Combine(output, "discovery-check.json"), JsonSerializer.Serialize(new { passed = false, message, seconds = started.Elapsed.TotalSeconds })); Console.WriteLine(message); return 1;
        }
    }
}
