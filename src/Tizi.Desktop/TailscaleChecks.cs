using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class TailscaleChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output); File.Delete(Path.Combine(output, "tailscale-failure.txt")); var checks = new List<string>();
        void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException(label); checks.Add(label); }
        var proxyBefore = new WindowsProxyRegistry().Read();
        try
        {
            await VerifyEditorAsync(output, Check);
            VerifyExitCountry(output, Check);
            await VerifyEngineAsync(output, Check);
            var proxyAfter = new WindowsProxyRegistry().Read();
            Check(proxyBefore.All(p => p.Value is null ? proxyAfter[p.Key] is null : p.Value.Same(proxyAfter[p.Key])), "Isolated checks leave the Windows proxy unchanged");
            File.WriteAllText(Path.Combine(output, "tailscale-checks.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, binary = VerificationStamp.Current(), syntheticData = true, realTailnetJoined = false, phoneExitVerified = false, personalConfigurationAccessed = false }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "tailscale-failure.txt"), string.Join('\n', checks) + "\n" + error); return 1; }
    }
    private static void VerifyExitCountry(string output,Action<bool,string> check)
    {
        var result=new IpQualityResult("8.8.8.8",[new("Fixture",null,"JP",null,null,null,"OK")],"US",DateTimeOffset.UtcNow);
        foreach(var language in new[]{"zh-CN","en"})
        {
            UiText.Language=language;
            var country=ExitCountryPresentation.Text(result,true,false);
            check(country.Contains(language=="en" ? "Japan" : "日本",StringComparison.Ordinal),language+": footer country uses IP geolocation instead of its different registered country");
            check(ExitCountryPresentation.Text(result,false,false)==UiText.Get("Exit: disconnected"),language+": disconnect immediately removes the old country");
            check(ExitCountryPresentation.Text(null,true,true)==UiText.Get("Exit: checking…") && ExitCountryPresentation.Text(null,true,false)==UiText.Get("Exit: unknown"),language+": pending and unavailable observations remain explicit");
            check(ExitCountryPresentation.Details(result,true).Contains(result.Ip),language+": tooltip identifies the observed exit IP");
            var conflict=result with {Sources=[..result.Sources,new("Other",null,"GB",null,null,null,"OK")]};
            check(ExitCountryPresentation.Text(conflict,true,false)==UiText.Get("Exit: unknown"),language+": conflicting country observations are not guessed");
            var footer=new Border {Background=Skin.ChromeBrush,Padding=new(16),Child=Ui.Value(country,true,11)};
            var window=new Window {Width=280,Height=65,Background=Skin.ChromeBrush,Content=footer};window.Show();window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame()!;frame.Save(Path.Combine(output,"exit-country-"+language+".png"),new PngBitmapEncoderOptions());window.Close();
        }
        UiText.Language="zh-CN";
    }
    private static async Task VerifyEditorAsync(string output, Action<bool, string> check)
    {
        var node = NodeImport.FromOptions(new JsonObject { ["name"] = "Synthetic transit", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = 18080 });
        var state = new AppState { Nodes = [node], SelectedNode = node.Id, AutoUpdate = false, StartHidden = false, RestoreConnection = false, IpQuality = new() { Enabled = false } };
        new Vault(UserData.DirectoryPath).Write("settings.dat", state);
        var window = new MainWindow { Width = 1240, Height = 960 }; window.Show(); await window.Controller.InitializeAsync();
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        async Task Wait(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!done()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Settings save did not settle"); Layout(); await Task.Delay(10); }
        }
        try
        {
            await window.Controller.ApplyAsync(state); window.ShowPage("Settings"); Layout(); window.SettingsSections!.ButtonAt(2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            var editor = window.GetVisualDescendants().OfType<TailscaleSettingsEditor>().Single();
            check(!editor.Enabled.IsChecked!.Value && !editor.AuthKey.IsEnabled, "Tailscale starts disabled and does not alter existing profiles");
            editor.Enabled.IsChecked = true; editor.AuthKey.Text = "fixture-auth-ui-not-valid"; editor.ExitNode.Text = "100.100.101.102";
            check(window.SettingsDirty && !window.Controller.State.Tailscale.Enabled, "Editing phone settings creates a draft and does not change the saved route");
            check(editor.AuthKey.PasswordChar != '\0' && editor.AuthKey.IsEnabled, "The authentication field is masked and enabled only with the feature");
            window.SettingsSections.ButtonAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); window.SettingsSections.ButtonAt(2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            check(ReferenceEquals(editor, window.GetVisualDescendants().OfType<TailscaleSettingsEditor>().Single()) && editor.ExitNode.Text == "100.100.101.102", "The Tailscale draft survives switching settings sections");
            var save = window.PageView.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "settings-save");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => !window.Controller.Busy && !window.SettingsDirty);
            check(window.Controller.State.Tailscale.Enabled && window.Controller.State.Tailscale.ExitNode == "100.100.101.102", "Saving commits the phone settings to the application profile");
            var saved = new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!;
            check(saved.Tailscale.AuthKey == editor.AuthKey.Text && !Encoding.UTF8.GetString(File.ReadAllBytes(UserData.File("settings.dat"))).Contains(editor.AuthKey.Text!), "The saved authentication key round-trips through the encrypted profile");
            editor.ExitNode.Text = "127.0.0.1"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => !window.Controller.Busy);
            check(window.SettingsDirty && window.Controller.State.Tailscale.ExitNode == "100.100.101.102" && window.PageView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Tag is "settings-error" && t.IsVisible), "An invalid phone address keeps the draft and the last saved route intact");
            editor.ExitNode.Text = "100.100.101.102";
            foreach (var language in new[] { "zh-CN", "en" })
            {
                UiText.Language = language;
                var card = new Border { Background = Skin.ChromeBrush, CornerRadius = new(14), Padding = new(24), Child = new TailscaleSettingsEditor(saved.Tailscale) };
                var preview = new Window { Width = 500, Height = 850, Background = Skin.PasteboardBrush, Content = new ScrollViewer { Content = card } }; preview.Show(); preview.UpdateLayout(); Dispatcher.UIThread.RunJobs(); preview.UpdateLayout();
                try
                {
                    check(card.GetVisualDescendants().OfType<TextBox>().All(t => t.Bounds.Width > 200), language + ": Tailscale fields retain readable width");
                    check(card.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get("Use Tailscale exit node")), language + ": phone settings are translated");
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = preview.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, "tailscale-settings-" + language + ".png"), new PngBitmapEncoderOptions());
                }
                finally { preview.Close(); }
            }
        }
        finally { UiText.Language = "zh-CN"; await window.Controller.DisposeAsync(); window.Close(); }
    }
    private static async Task VerifyEngineAsync(string output, Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var first = NodeImport.FromOptions(new JsonObject { ["name"] = "Local transit A", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = port });
        var second = first with { Id = Guid.NewGuid(), Name = "Local transit B" };
        var state = new AppState { Nodes = [first, second], SelectedNode = first.Id, Capture = CaptureMode.LocalOnly, Mode = RouteMode.Rule, MixedPort = EngineClient.FreePort(), Tailscale = new() { Enabled = true, AuthKey = "fixture-auth-engine-not-valid", ExitNode = "100.100.101.102", ControlUrl = "https://ts-control.test" }, Rules = [new("MATCH", "", "DIRECT")], Dns = new() { Hosts = new() { ["ts-control.test"] = "127.0.0.1" } } };
        await using var engine = new EngineClient();
        await engine.StartAsync(state, Path.Combine(AppContext.BaseDirectory, "engine/mihomo.exe"), Path.Combine(UserData.DirectoryPath, "tailscale-engine"));
        var runtime = engine.RuntimeDirectory; var config = JsonNode.Parse(File.ReadAllText(Path.Combine(runtime, "config.json")))!;
        check(engine.IsRunning, "The bundled real core validates and starts the Tailscale configuration without enabling system capture");
        check(config["listeners"]![0]!["listen"]!.ToString() == "127.0.0.1" && config["listeners"]![0]!["proxy"]!.ToString() == TailscaleSettings.ProxyName, "Exit checks use an extra loopback listener fixed to the phone route");
        using var api = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri("http://127.0.0.1:" + engine.ApiPort) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["secret"]!.ToString());
        await engine.SelectNodeAsync(second);
        var selected = JsonNode.Parse(await api.GetStringAsync("/proxies/" + TailscaleSettings.TransitGroup))!;
        check(selected["now"]!.ToString() == second.CoreName, "Selecting a node updates only the transit selector");
        var final = JsonNode.Parse(await api.GetStringAsync("/proxies/TIZI"))!;
        check(final["now"]!.ToString() == TailscaleSettings.ProxyName, "The final selector remains the phone when transit changes");
        var rejected = false;
        try { await engine.ReloadAsync(state with { Tailscale = state.Tailscale with { ExitNode = "100.100.101.103" } }); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Changing phone identity or exit settings requires a disconnect");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        var request = engine.CheckConnectionAsync("http://example.test/exit-check", stop.Token);
        using (var client = await listener.AcceptTcpClientAsync(stop.Token))
        {
            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, leaveOpen: true);
            var header = await reader.ReadLineAsync(stop.Token);
            check(header is not null && (header.Contains("ts-control.test", StringComparison.Ordinal) || header.StartsWith("CONNECT 127.0.0.1:443 ", StringComparison.Ordinal)), "The real core sends Tailscale control traffic to the selected local proxy even when normal rules select DIRECT (" + header + ")");
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 503 Synthetic proxy\r\nContent-Length: 0\r\n\r\n"), stop.Token);
        }
        stop.Cancel();
        try { await request; } catch (Exception e) when (e is OperationCanceledException or HttpRequestException) { }
        check(!engine.RecentLogs.Any(line => line.Contains(state.Tailscale.AuthKey, StringComparison.Ordinal)), "Engine diagnostics do not expose the authentication key");
        await engine.StopAsync(); check(!Directory.Exists(runtime), "Disconnect removes the temporary Tailscale runtime and plaintext configuration");
    }
}
