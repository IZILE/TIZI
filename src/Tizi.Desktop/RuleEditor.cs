using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed class RuleEditor : CenteredDialog
{
    internal StableChoice TypeChoice { get; } = new() { ItemsSource = RoutingRule.Types };
    internal TextBox ValueEditor { get; } = new() { Classes = { "text-input" } };
    internal StableChoice ActionChoice { get; } = new() { ItemsSource = new[] { "TIZI", "DIRECT", "REJECT" } };
    internal CheckBox NoResolve { get; } = new() { Content = Ui.Text("Skip DNS resolution (no-resolve)") };
    private readonly Button _save;
    private readonly Func<RoutingRule, Task> _apply;
    private readonly TextBlock _error = Ui.Value("");
    internal bool Applied { get; private set; }
    internal string? ErrorText { get; private set; }
    internal RuleEditor(RoutingRule rule, Func<RoutingRule, Task> apply, bool adding = false)
    {
        _apply = apply; Title = UiText.Get(adding ? "Add rule" : "Edit rule");
        Width = 600; MinWidth = 460; MaxHeight = 760; SizeToContent = SizeToContent.Height;
        Background = Skin.ChromeBrush; Padding = new(24); 
        TypeChoice.SelectedIndex = Array.IndexOf(RoutingRule.Types, rule.Type);
        ValueEditor.Text = rule.Value; ActionChoice.SelectedItem = rule.Action; NoResolve.IsChecked = rule.NoResolve;
        var body = new StackPanel { Spacing = 18 };
        void Field(string label, Control input) => body.Children.Add(new StackPanel { Spacing = 7, Children = { Ui.Text(label, true), input } });
        Field("Rule type", TypeChoice); Field("Domain, network or process", ValueEditor); Field("Action", ActionChoice); body.Children.Add(NoResolve);
        _error.Foreground = Skin.ErrorStrong; _error.TextWrapping = TextWrapping.Wrap; _error.IsVisible = false; body.Children.Add(_error);
        _save = Ui.Button(adding ? "Add" : "Save", () => _ = SubmitAsync(), true);
        var actions = Ui.Horizontal(Ui.Button("Cancel", Close), _save); actions.HorizontalAlignment = HorizontalAlignment.Right; body.Children.Add(actions);
        Content = Ui.Scroll(body); UiText.WireChoices(this);
        TypeChoice.SelectionChanged += (_, _) => UpdateFields(); UpdateFields();
        Opened += (_, _) => { if (ValueEditor.IsEnabled) ValueEditor.Focus(); else ActionChoice.Focus(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) { _ = SubmitAsync(); e.Handled = true; } };
    }
    private void UpdateFields()
    {
        var type = TypeChoice.SelectedItem?.ToString(); ValueEditor.IsEnabled = type != "MATCH";
        NoResolve.IsEnabled = type is "IP-CIDR" or "IP-CIDR6" or "GEOIP";
        if (!NoResolve.IsEnabled) NoResolve.IsChecked = false;
    }
    internal async Task SubmitAsync()
    {
        if (!_save.IsEnabled) return; _save.IsEnabled = false;
        try
        {
            ErrorText = null; _error.IsVisible = false;
            var type = TypeChoice.SelectedItem?.ToString() ?? "";
            var rule = new RoutingRule(type, type == "MATCH" ? "" : ValueEditor.Text?.Trim() ?? "", ActionChoice.SelectedItem?.ToString() ?? "") { NoResolve = NoResolve.IsEnabled && NoResolve.IsChecked == true };
            ModelValidation.Rule(rule); await _apply(rule); Applied = true; await CloseSheetAsync();
        }
        catch (Exception e) { ErrorText = e.Message; _error.Text = UiText.Get(e.Message); _error.IsVisible = true; }
        finally { _save.IsEnabled = true; }
    }
}
