using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private RoutingRule? _chosenRule;
    private Button? _moveUpButton, _moveDownButton, _closeSelectionButton;
    private readonly HashSet<string> _visibleTargets = [];
    private readonly Border _undoHost = new() { IsVisible = false, Margin = new(0, 12, 0, 0), Padding = new(12), Background = Skin.ChromeBrush, CornerRadius = new(12) };
    private Func<AppState, AppState>? _undoRemoval;
    private bool IsVisibleTarget(string? id) => _page == "Nodes" ? _subscriptionTree.ContainsVisible(id) : id is not null && _visibleTargets.Contains(id);
    private void ChooseItem(string id)
    {
        if (_chosen == id) return;
        _chosen = id;
        _chosenRule = _page == "Rules" && int.TryParse(id, out var index) && index >= 0 && index < _app.State.Rules.Count ? _app.State.Rules[index] : null;
        UpdateSelectionActions(); if (_page == "Connections") RefreshInspector();
    }
    private void ClearSelectionOnBlankPress(PointerPressedEventArgs e)
    {
        if (_chosen is null || _page is not ("Nodes" or "Providers" or "Rules" or "Connections")
            || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
            || e.Source is not Visual source) return;
        var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
        if (!ancestors.Contains(_contentSurface) || ancestors.Any(v => v is Button or TextBox or ComboBox
            or Avalonia.Controls.Primitives.ScrollBar or SubscriptionTree.GroupCard)) return;
        ClearSelection();
    }
    private void ClearSelection()
    {
        _chosen = null; _chosenRule = null; _rows.Select(null); _subscriptionTree.SelectItem(null);
        UpdateSelectionActions(); if (_page == "Connections") RefreshInspector();
    }
    private void ValidateSelection()
    {
        if (_chosen is null) return;
        if (!IsVisibleTarget(_chosen))
        { _chosen = null; _chosenRule = null; _rows.Select(null); _subscriptionTree.SelectItem(null); UpdateSelectionActions(); RefreshInspector(); }
    }
    private int? SelectedRuleIndex
    {
        get
        {
            if (_page != "Rules" || _chosenRule is null || !IsVisibleTarget(_chosen)) return null;
            var index = _app.State.Rules.FindIndex(r => ReferenceEquals(r, _chosenRule));
            return index >= 0 ? index : null;
        }
    }
    private IEnumerable<(string Label, Action Action)> ContextActionsFor(string id)
    {
        var page = _page;
        Action Guard(Action action) => () => { if (_page == page && IsVisibleTarget(id)) { ChooseItem(id); action(); } };
        if (page == "Providers")
            return [("Details", Guard(() => _ = OpenProviderDetailsAsync(id))), ("Open website", Guard(OpenProvider)), ("Remove", Guard(RemoveSelected))];
        if (page == "Connections")
            return [("Details", Guard(OpenConnectionDetails)), ("Close selected", Guard(CloseSelectedConnection))];
        if (page == "Rules" && int.TryParse(id, out var index) && index >= 0 && index < _app.State.Rules.Count)
        {
            var rule = _app.State.Rules[index];
            Action RuleAction(Action action) => Guard(() =>
            {
                if (index >= _app.State.Rules.Count || !ReferenceEquals(_app.State.Rules[index], rule)) { ShowError("This rule has changed. Reopen it to edit."); return; }
                action();
            });
            return [("Edit rule", RuleAction(() => EditRule(index))), ("Move up", RuleAction(() => MoveRule(-1))), ("Move down", RuleAction(() => MoveRule(1))), ("Remove", RuleAction(RemoveSelected))];
        }
        return [];
    }
    private void CloseSelectedConnection()
    {
        if (_page != "Connections" || !IsVisibleTarget(_chosen) || _chosen is not { } id) return;
        Run(() => _app.Engine.CloseConnectionsAsync(id));
    }
    private void OpenConnectionDetails()
    {
        if (_snapshot.Connections.FirstOrDefault(c => c.Id == _chosen) is not { } connection) return;
        var dialog = new Window { Title = UiText.Get("Connection details"), Width = 620, MaxHeight = 680, SizeToContent = SizeToContent.Height, Background = Skin.ChromeBrush, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, ShowInTaskbar = false };
        var details = new StackPanel { Spacing = 16, Margin = new(24) };
        void Detail(string label, string value)
        {
            var text = new SelectableTextBlock { Text = value.Length == 0 ? "—" : value, Foreground = Skin.LabelBrush, TextWrapping = TextWrapping.Wrap };
            details.Children.Add(new StackPanel { Spacing = 5, Children = { Ui.Text(label, true, 12), text } });
        }
        Detail("Host", connection.Host); Detail("Process", connection.Process); Detail("Network", connection.Network.ToUpperInvariant()); Detail("Rule", connection.Rule); Detail("Chain", connection.Chain);
        Detail("Uploaded", Bytes(connection.Upload)); Detail("Downloaded", Bytes(connection.Download));
        var close = Ui.Button("Close", () => dialog.Close()); close.HorizontalAlignment = HorizontalAlignment.Right; details.Children.Add(close);
        dialog.Content = Ui.Scroll(details); dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        _ = dialog.ShowDialog(this);
    }
    private void OfferUndo(Func<AppState, AppState> undo)
    {
        _undoRemoval = undo;
        var button = Ui.Button("Undo", () => Run(async () =>
        {
            if (_undoRemoval is not { } restore) return;
            var state = restore(_app.State); await _app.ApplyAsync(state, true);
            _undoRemoval = null; _undoHost.IsVisible = false; ShowNotice("Removal undone.");
        }));
        _undoHost.Child = Ui.Horizontal(Ui.Text("Item removed", true, 13), button); _undoHost.IsVisible = true;
    }
    private async Task<bool> ConfirmRemovalAsync(string name, int nodes)
    {
        var dialog = new Window { Title = UiText.Get("Remove subscription"), Width = 500, SizeToContent = SizeToContent.Height, Background = Skin.ChromeBrush, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, ShowInTaskbar = false };
        var prompt = Ui.Value(UiText.Format("Remove {0} and its {1} nodes? Routing rules will be kept.", name, nodes)); prompt.TextWrapping = TextWrapping.Wrap;
        var buttons = Ui.Horizontal(Ui.Button("Cancel", () => dialog.Close(false)), Ui.Button("Remove subscription", () => dialog.Close(true))); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        dialog.Content = new StackPanel { Spacing = 20, Margin = new(24), Children = { prompt, buttons } };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(false); e.Handled = true; } };
        return await dialog.ShowDialog<bool>(this);
    }
}
