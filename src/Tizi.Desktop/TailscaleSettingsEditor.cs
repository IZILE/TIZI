using Avalonia.Controls;
using Avalonia.Layout;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed class TailscaleSettingsEditor : StackPanel
{
    internal CheckBox Enabled { get; }
    internal CheckBox ThroughProxy { get; }
    internal TextBox Hostname { get; }
    internal TextBox AuthKey { get; }
    internal TextBox ControlUrl { get; }
    internal TextBox ExitNode { get; }
    internal CheckBox AcceptRoutes { get; }
    internal CheckBox AllowLan { get; }
    internal Control[] Inputs => [Enabled, ThroughProxy, Hostname, AuthKey, ControlUrl, ExitNode, AcceptRoutes, AllowLan];
    internal TailscaleSettings Value => new()
    {
        Enabled = Enabled.IsChecked == true, ThroughProxy = ThroughProxy.IsChecked == true,
        Hostname = Hostname.Text?.Trim() ?? "", AuthKey = AuthKey.Text?.Trim() ?? "",
        ControlUrl = ControlUrl.Text?.Trim().TrimEnd('/') ?? "", ExitNode = ExitNode.Text?.Trim() ?? "",
        AcceptRoutes = AcceptRoutes.IsChecked == true, AllowLanAccess = AllowLan.IsChecked == true
    };
    internal TailscaleSettingsEditor(TailscaleSettings settings)
    {
        Spacing = 16; Tag = "tailscale-settings";
        CheckBox Toggle(string key, string text, bool value)
        {
            var box = new CheckBox { Content = Ui.Text(text), IsChecked = value, Tag = key };
            Children.Add(box); return box;
        }
        TextBox Field(string key, string label, string value, string? placeholder = null)
        {
            var box = new TextBox { Text = value, PlaceholderText = placeholder, Tag = key, Classes = { "text-input" } };
            var caption = Ui.Text(label, true); Avalonia.Automation.AutomationProperties.SetLabeledBy(box, caption);
            Children.Add(new StackPanel { Spacing = 7, Children = { caption, box } }); return box;
        }
        Enabled = Toggle("tailscale-enabled", "Use Tailscale exit node", settings.Enabled);
        ThroughProxy = Toggle("tailscale-transit", "Use the selected proxy as transit", settings.ThroughProxy);
        Hostname = Field("tailscale-hostname", "Tailscale device name", settings.Hostname);
        AuthKey = Field("tailscale-auth-key", "Tailscale authentication key", settings.AuthKey, "tskey-auth-…"); AuthKey.PasswordChar = '●';
        ControlUrl = Field("tailscale-control-url", "Control server URL (optional)", settings.ControlUrl, "https://controlplane.tailscale.com");
        ExitNode = Field("tailscale-exit-node", "iPhone Tailscale IP", settings.ExitNode, "100.x.x.x");
        AcceptRoutes = Toggle("tailscale-accept-routes", "Accept Tailscale subnet routes", settings.AcceptRoutes);
        AllowLan = Toggle("tailscale-allow-lan", "Allow local network access", settings.AllowLanAccess);
        var route = Ui.Text("", true, 12); route.TextWrapping = Avalonia.Media.TextWrapping.Wrap; route.Tag = "tailscale-route"; Children.Add(route);
        void Refresh()
        {
            foreach (var input in Inputs.Where(input => !ReferenceEquals(input, Enabled))) input.IsEnabled = Enabled.IsChecked == true;
            UiText.Set(route, TextBlock.TextProperty, ThroughProxy.IsChecked == true
                ? "PC → selected proxy → DERP relay → iPhone → internet. Transit mode always uses DERP."
                : "PC → Tailscale → iPhone → internet. The selected proxy is not used.");
        }
        Enabled.IsCheckedChanged += (_, _) => Refresh(); ThroughProxy.IsCheckedChanged += (_, _) => Refresh(); Refresh();
    }
}
