using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed class NodeEditor : Window
{
    internal ComboBox Protocol { get; } = new() { ItemsSource = ProtocolCatalog.Supported };
    private readonly Dictionary<string, Control> _inputs = [];
    private readonly Dictionary<ProtocolEntry, ProtocolDraft> _protocolDrafts = [];
    private ProtocolEntry? _displayedProtocol;
    private sealed record ProtocolDraft(decimal? Port, string Advanced, Dictionary<string, object?> Values);
    private bool _submitting, _allowClose, _closed;
    internal EditorSession Session { get; } = new();
    internal TextBox NameField { get; } = Text();
    internal TextBox ServerField { get; } = Text();
    internal NumericUpDown PortField { get; } = new() { Minimum = 1, Maximum = 65535, Value = 443, FormatString = "0" };
    internal TextBox Advanced { get; } = new() { Text = "{}", AcceptsReturn = true, Height = 180, FontFamily = new("Consolas"), TextWrapping = TextWrapping.Wrap, Classes = { "text-input" } };
    internal Button AdvancedButton { get; }
    internal AnimatedReveal AdvancedReveal { get; }
    internal ClientNotification Notification { get; } = new();
    internal bool Applied { get; private set; }
    internal string? ErrorText { get; private set; }
    private Func<Task> _submit = null!;
    internal Task SubmitAsync() => _submit();
    internal Control Parameter(string key) => _inputs[key];
    private static TextBox Text() => new() { Classes = { "text-input", "form-field" } };

    internal NodeEditor(Func<ProxyNode, Task> apply, ProxyNode? original = null)
    {
        Title = UiText.Get(original is null ? "Add node" : "Edit node"); Width = 700; Height = 700; MinWidth = 480; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Skin.ChromeBrush; ShowInTaskbar = false;
        using var icon = typeof(MainWindow).Assembly.GetManifestResourceStream("Tizi.Desktop.AppIcon.ico"); if (icon is not null) Icon = new WindowIcon(icon);
        UiText.WireChoices(this);
        var index = original is null ? 0 : Array.FindIndex(ProtocolCatalog.Supported, p => p.CoreType == original.Type &&
            (p.CoreType is not ("http" or "socks5") || p.Tls == (original.Options["tls"]?.GetValue<bool>() == true)));
        Protocol.SelectedIndex = Math.Max(0, index);
        NameField.Text = original?.Name ?? ""; ServerField.Text = original?.Server ?? "";
        var fields = new StackPanel { Spacing = 16 };
        var rotation = new RotateTransform();
        rotation.Transitions = new() { new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new SplineEasing(.22, .7, .2, 1) } };
        var arrow = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L4,4 L8,0"), Width = 8, Height = 4,
            Stroke = Skin.LabelBrush, StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = RelativePoint.Center, RenderTransform = rotation, IsHitTestVisible = false };
        var advancedHeading = new Grid { ColumnDefinitions = new("*,16,8"), Children = { Ui.Text("Advanced JSON"), arrow } }; Grid.SetColumn(arrow, 2);
        AdvancedButton = new Button { Content = advancedHeading, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new(12, 6), BorderThickness = default, Classes = { "plain", "disclosure" }, CornerRadius = new(18), MinHeight = 36 };
        var explanation = Ui.Text("Additional protocol parameters (JSON object)", true, 12); explanation.TextWrapping = TextWrapping.Wrap;
        AdvancedReveal = new AnimatedReveal { Child = new StackPanel { Spacing = 10, Margin = new(12, 4, 12, 12), Children = { explanation, Advanced } }, IsHitTestVisible = false };
        var expanded = false;
        AdvancedButton.Click += (_, _) =>
        {
            expanded = !expanded; AdvancedReveal.IsHitTestVisible = expanded;
            Motion.Set(AdvancedReveal, AnimatedReveal.ProgressProperty, expanded ? 1d : 0d);
            Motion.Set(rotation, RotateTransform.AngleProperty, expanded ? 180d : 0d);
            Avalonia.Automation.AutomationProperties.SetHelpText(AdvancedButton, UiText.Get(expanded ? "Expanded" : "Collapsed"));
        };
        var advancedCard = new Border { Background = Brush.Parse("#353535"), CornerRadius = new(18), ClipToBounds = true,
            Child = new StackPanel { Children = { AdvancedButton, AdvancedReveal } } };
        var body = new StackPanel { Spacing = 16, Children = { MacControls.Row("Protocol", Protocol), MacControls.Row("Name", NameField), MacControls.Row("Server", ServerField), MacControls.Row("Port", PortField), fields, advancedCard } };
        var error = Ui.Value(""); error.Tag = "node-editor-error"; error.TextWrapping = TextWrapping.Wrap;
        error.Foreground = new SolidColorBrush(Color.Parse("#FF9B8F")); error.IsVisible = false;
        var progress = Ui.Text("Applying changes…", true, 13); progress.Tag = "node-editor-progress"; progress.IsVisible = false;
        var save = Ui.Button(original is null ? "Add" : "Save", () => { }, true);
        var cancel = Ui.Button("Cancel", Close); save.Click += (_, _) => _ = Save(); var actions = Ui.Horizontal(cancel, save); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var root = new Grid { RowDefinitions = new("*,Auto"), Children = { Ui.Scroll(body, new(26, 24, 26, 0)) } };
        var footer = new Border { Padding = new(26, 16, 26, 24), Background = Skin.ChromeBrush,
            Child = new StackPanel { Spacing = 10, Children = { error, progress, actions } } }; Grid.SetRow(footer, 1); root.Children.Add(footer);
        Notification.MaxWidth = 560; Content = new Grid { Children = { root, Notification } };
        SizeChanged += (_, _) => Notification.Width = Math.Min(560, Math.Max(180, ClientSize.Width - Notification.Margin.Left - Notification.Margin.Right));
        Protocol.SelectionChanged += (_, _) => { ClearError(); Rebuild(); }; Rebuild();
        foreach (var input in new[] { NameField, ServerField, Advanced }) input.TextChanged += (_, _) => ClearError();
        PortField.PropertyChanged += (_, change) => { if (change.Property == NumericUpDown.ValueProperty) ClearError(); };
        Session.Changed += UpdateState;
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            if (!Session.TryCancel()) { e.Cancel = true; return; }
            _allowClose = true;
        };
        Closed += (_, _) => { _closed = true; Session.Changed -= UpdateState; Session.Dispose(); };
        Opened += (_, _) => NameField.Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _ = Save(); e.Handled = true; } };
        _submit = Save;

        void ClearError()
        {
            if (_submitting) return;
            ErrorText = null; error.IsVisible = false; error.Text = "";
        }
        void UpdateState()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(UpdateState); return; }
            if (_closed) return;
            save.IsEnabled = !_submitting && !Session.IsCancellationRequested;
            cancel.IsEnabled = !Session.IsCommitting;
            body.IsEnabled = !_submitting;
            progress.IsVisible = Session.IsCommitting;
        }
        void RememberProtocol()
        {
            if (_displayedProtocol is not { } displayed) return;
            _protocolDrafts[displayed] = new(PortField.Value, Advanced.Text ?? "", _inputs.ToDictionary(pair => pair.Key,
                pair => pair.Value switch { TextBox text => (object?)(text.Text ?? ""), CheckBox check => check.IsChecked, _ => null }));
        }

        void Rebuild()
        {
            if (Protocol.SelectedItem is not ProtocolEntry entry) return;
            if (entry == _displayedProtocol) return;
            RememberProtocol();
            _protocolDrafts.TryGetValue(entry, out var draft);
            fields.Children.Clear(); _inputs.Clear(); var parameters = ProtocolCatalog.Parameters(entry);
            if (entry.CoreType is "ss" or "ssr" or "socks5" or "http" or "trojan" or "hysteria2" or "anytls" or "tuic" or "ssh" or "mieru" or "trusttunnel" or "gost-relay") parameters["password"] = "";
            if (entry.CoreType is "socks5" or "http" or "ssh" or "mieru" or "trusttunnel" or "gost-relay") parameters["username"] = "";
            if (entry.CoreType is "socks5" or "http" or "gost-relay") parameters["tls"] = entry.Tls;
            var existing = original is not null && original.Type == entry.CoreType &&
                (entry.CoreType is not ("http" or "socks5") || entry.Tls == (original.Options["tls"]?.GetValue<bool>() == true)) ? original.Options : null;
            foreach (var (key, template) in parameters)
            {
                var value = existing?.ContainsKey(key) == true ? existing[key] : template;
                object? savedValue = null; var hasSavedValue = draft?.Values.TryGetValue(key, out savedValue) == true;
                Control input;
                if (template is JsonValue jv && jv.TryGetValue<bool>(out _))
                {
                    var check = new CheckBox { IsChecked = hasSavedValue ? savedValue as bool? : value?.GetValue<bool>() == true };
                    check.IsCheckedChanged += (_, _) => ClearError(); input = check;
                }
                else
                {
                    var text = Text(); text.Text = hasSavedValue ? savedValue as string ?? "" : value is JsonArray or JsonObject ? value.ToJsonString() : value?.ToString() ?? "";
                    text.PasswordChar = key is "password" or "private-key" or "psk" or "auth-str" or "key" ? '●' : '\0';
                    text.TextChanged += (_, _) => ClearError(); input = text;
                }
                _inputs[key] = input; fields.Children.Add(MacControls.Row(Label(key), input));
            }
            PortField.Value = draft is not null ? draft.Port : existing is not null ? original!.Port : entry.CoreType == "ssh" ? 22 : 443;
            var extra = existing?.DeepClone() as JsonObject ?? new JsonObject();
            foreach (var key in new[] { "name", "type", "server", "port" }.Concat(parameters.Select(p => p.Key))) extra.Remove(key);
            // Keep the exact editor text, including unfinished/invalid JSON, so
            // switching protocols is never an implicit validation or reset.
            Advanced.Text = draft?.Advanced ?? extra.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            _displayedProtocol = entry;
        }
        async Task Save()
        {
            if (_submitting || _closed || Session.IsCancellationRequested || Protocol.SelectedItem is not ProtocolEntry entry) return;
            _submitting = true;
            try
            {
                Session.BeginAttempt(); UpdateState(); var template = ProtocolCatalog.Parameters(entry);
                var options = new JsonObject { ["name"] = NameField.Text, ["type"] = entry.CoreType, ["server"] = ServerField.Text?.Trim(), ["port"] = (int)(PortField.Value ?? 0) };
                foreach (var (key, control) in _inputs)
                {
                    if (control is CheckBox check) options[key] = check.IsChecked == true;
                    else if (control is TextBox text)
                    {
                        var value = text.Text?.Trim() ?? "";
                        options[key] = template[key] is JsonArray ? JsonNode.Parse(value) : template[key] is JsonValue jv && jv.TryGetValue<int>(out _) ? JsonValue.Create(int.Parse(value)) : JsonValue.Create(value);
                    }
                }
                var extra = JsonNode.Parse(Advanced.Text ?? "{}", documentOptions: new JsonDocumentOptions { MaxDepth = 12 }) as JsonObject ?? throw new FormatException("Expected JSON object.");
                foreach (var pair in extra) if (pair.Key is not ("name" or "type" or "server" or "port")) options[pair.Key] = pair.Value?.DeepClone();
                var node = NodeImport.FromOptions(options);
                if (original is not null) node = node with { Id = original.Id, SubscriptionId = original.SubscriptionId, CountryCode = original.CountryCode };
                // Validation and constructing the node have no side effects. From
                // here until apply returns, do not offer a cancellation that could
                // leave a saved node behind a dismissed editor.
                Session.BeginCommit(); await apply(node);
                ErrorText = null; error.IsVisible = false; Applied = true; _allowClose = true; Close();
            }
            catch (OperationCanceledException) when (Session.IsCancellationRequested) { }
            catch (Exception e)
            {
                ErrorText = e is JsonException ? "Invalid JSON object." : e.Message;
                error.Text = UiText.Get(ErrorText); error.IsVisible = true; Notification.Show(error.Text ?? "");
            }
            finally { Session.FinishAttempt(); _submitting = false; UpdateState(); }
        }
    }
    private static string Label(string key) => key switch
    {
        "password" => "Password", "username" => "Username", "uuid" => "UUID", "cipher" => "Cipher", "servername" => "TLS server name",
        "private-key" => "Private key", "public-key" => "Public key", "host-key" => "SSH host keys", "ip" => "Tunnel IPv4", "udp" => "UDP relay",
        "tls" => "Enable TLS", "psk" => "Pre-shared key", "version" => "Protocol version", "protocol" => "SSR protocol", "obfs" => "Obfuscation",
        "auth-str" => "Authentication", "up" => "Upload bandwidth (Mbps)", "down" => "Download bandwidth (Mbps)", "alterId" => "Alter ID",
        "congestion-controller" => "Congestion control", "transport" => "Transport", "multiplexing" => "Multiplexing", "aead-method" => "Encryption method", "key" => "Key", _ => key
    };
}
