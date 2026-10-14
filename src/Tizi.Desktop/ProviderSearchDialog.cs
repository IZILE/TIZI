using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed class ProviderSearchDialog : CenteredDialog
{
    internal StableChoice Mode { get; } = new() { ItemsSource = ProviderSearchModes.All.Select(m => m.Name).ToArray() };
    internal NumericUpDown Budget { get; } = new() { Minimum = 0, Maximum = 100000, Increment = 10, FormatString = "0.##", Width = 170 };
    internal StableChoice Currency { get; } = new() { ItemsSource = ProviderSearchModes.Currencies, Width = 110 };
    internal TextBox Regions { get; } = new() { MaxLength = 160, Classes = { "text-input" } };
    internal CheckBox Trial { get; } = new() { Content = Ui.Text("Require a documented trial") };
    internal TextBox Extra { get; } = new() { MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 76, Classes = { "text-input" } };
    private readonly Dictionary<string, CheckBox> _protocols = [];
    private readonly CancellationTokenSource _windowCancellation = new();
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly TextBlock _status = Ui.Text("", true);
    private readonly TextBlock _error = Ui.Value("");
    private readonly ClientNotification _notification = new();
    private readonly ProgressBar _activity = new() { IsIndeterminate = true, Height = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _cancel, _remember, _go;
    private readonly Action? _cancelSearch;
    private bool _closed, _submitting, _submittingSearch, _searching, _cancelling, _searchCancelled;
    private string? _searchError, _localError;
    internal CancellationToken Cancellation { get; }
    internal Button SearchButton => _go;
    internal Button CancelButton => _cancel;
    internal Button SaveButton => _remember;
    internal string? StatusText => _status.IsVisible ? _status.Text : null;
    internal string? ErrorText => _error.IsVisible ? _error.Text : null;
    internal ClientNotification Notification => _notification;
    internal ProviderSearchOptions Criteria => ProviderSearchModes.Validate(new()
    {
        ModeId = ProviderSearchModes.All[Math.Max(0, Mode.SelectedIndex)].Id, MonthlyBudget = Budget.Value ?? 0,
        Currency = Currency.SelectedItem?.ToString() ?? "CNY", Regions = Regions.Text ?? "",
        Protocols = _protocols.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(),
        RequireTrial = Trial.IsChecked == true, ExtraRequirements = Extra.Text ?? ""
    });
    internal ProviderSearchDialog(ProviderSearchOptions saved, Func<ProviderSearchOptions, Task> save, Func<ProviderSearchOptions, Task> search, Action? cancelSearch = null)
    {
        Cancellation = _windowCancellation.Token; _cancelSearch = cancelSearch;
        saved = ProviderSearchModes.Validate(saved);
        Title = UiText.Get("Search latest providers"); Width = 720; Height = 760; MinWidth = 500; MinHeight = 520;
        Background = Skin.ChromeBrush; UiText.WireChoices(this);
        Mode.SelectedIndex = Array.FindIndex(ProviderSearchModes.All, m => m.Id == saved.ModeId); Budget.Value = saved.MonthlyBudget;
        Currency.SelectedItem = saved.Currency; Regions.Text = saved.Regions; Trial.IsChecked = saved.RequireTrial; Extra.Text = saved.ExtraRequirements;
        var description = Ui.Text(ProviderSearchModes.All[Mode.SelectedIndex].Description, true); description.TextWrapping = TextWrapping.Wrap;
        Mode.SelectionChanged += (_, _) => UiText.Set(description, TextBlock.TextProperty, ProviderSearchModes.All[Math.Max(0, Mode.SelectedIndex)].Description);
        var protocols = new WrapPanel { ItemSpacing = 18, LineSpacing = 8 };
        foreach (var name in ProviderSearchModes.Protocols) { var check = new CheckBox { Content = name, IsChecked = saved.Protocols.Contains(name), MinHeight = 32 }; _protocols[name] = check; protocols.Children.Add(check); }
        void Field(string label, Control input) => _fields.Children.Add(new StackPanel { Spacing = 8, Children = { Ui.Text(label), input } });
        Field("Search mode", Mode); _fields.Children.Add(description);
        var budget = Ui.Horizontal(Budget, Currency, Ui.Text("0 = no limit", true)); Field("Monthly budget", budget);
        Field("Preferred regions (optional)", Regions); Field("Protocols / client formats (any selected)", protocols);
        _fields.Children.Add(Trial); Field("Additional preferences (optional)", Extra);
        _status.TextWrapping = TextWrapping.Wrap; _status.TextTrimming = TextTrimming.None;
        _error.TextWrapping = TextWrapping.Wrap; _error.TextTrimming = TextTrimming.None; _error.Foreground = new SolidColorBrush(Color.Parse("#FFB5A5"));
        _cancel = Ui.Button("Cancel", () => { if ((_searching || _submittingSearch) && _cancelSearch is not null) _cancelSearch(); else Close(); });
        _remember = Ui.Button("Save criteria", () => { }); _go = Ui.Button("Search · uses API credits", () => { }, true);
        _remember.Click += (_, _) => _ = Submit(save, false); _go.Click += (_, _) => _ = Submit(search, true);
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _cancel, _remember, _go } };
        var feedback = new StackPanel { Spacing = 9, Children = { _activity, _status, _error } };
        var root = new Grid { RowDefinitions = new("*,Auto,Auto"), Margin = new(26), RowSpacing = 16 };
        root.Children.Add(Ui.Scroll(_fields));
        Grid.SetRow(feedback, 1); root.Children.Add(feedback); Grid.SetRow(actions, 2); root.Children.Add(actions);
        Content = new Grid { Children = { root, _notification } };
        SizeChanged += (_, _) => _notification.Width = Math.Max(220, Math.Min(520, ClientSize.Width - 48));
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Opened += (_, _) => Mode.Focus();
        Closed += (_, _) =>
        {
            _closed = true; _windowCancellation.Cancel();
            if (_searching || _submittingSearch) _cancelSearch?.Invoke();
            _windowCancellation.Dispose();
        };
        UpdateFeedback();
        async Task Submit(Func<ProviderSearchOptions, Task> action, bool isSearch)
        {
            if (_closed || _submitting || _searching) return;
            _localError = null; _searchCancelled = false;
            ProviderSearchOptions options;
            try { options = Criteria; }
            catch (Exception error) { _localError = error.Message; UpdateFeedback(); return; }
            _submitting = true; _submittingSearch = isSearch; UpdateFeedback();
            try { await action(options); if (!_closed) Close(); }
            catch (OperationCanceledException) when (_searchCancelled || Cancellation.IsCancellationRequested) { _localError = null; }
            catch (Exception error)
            {
                _localError = null;
                if (!isSearch || _searchError is null) _notification.Show(UiText.Get(error.Message));
                if (isSearch) _searchError ??= error.Message;
            }
            finally { _submitting = _submittingSearch = false; if (!_closed) UpdateFeedback(); }
        }
    }
    internal void SetSearchState(bool busy, string? error, bool cancelling = false, bool cancelled = false)
    {
        if (_closed) return;
        if (busy && !_searching) _localError = null;
        _searching = busy; _searchError = error; _cancelling = cancelling; _searchCancelled = cancelled;
        UpdateFeedback();
    }
    private void UpdateFeedback()
    {
        var searching = _searching || (_submitting && _submittingSearch);
        _fields.IsEnabled = _remember.IsEnabled = _go.IsEnabled = !searching && !_submitting;
        _cancel.IsEnabled = !_cancelling;
        UiText.Set((TextBlock)_cancel.Content!, TextBlock.TextProperty, searching ? "Cancel search" : "Cancel");
        var error = _localError ?? _searchError;
        UiText.Set((TextBlock)_go.Content!, TextBlock.TextProperty, error is null ? "Search · uses API credits" : "Retry search · uses API credits");
        _activity.IsVisible = searching && !_cancelling;
        _status.IsVisible = searching || _submitting || _searchCancelled;
        UiText.Set(_status, TextBlock.TextProperty, _cancelling ? "Cancelling search…" : searching ? "Searching providers… Previous results remain available." : _submitting ? "Saving criteria…" : "Search cancelled; previous results kept.");
        _error.IsVisible = !string.IsNullOrWhiteSpace(_localError) && !_searchCancelled;
        if (_error.IsVisible) UiText.Set(_error, TextBlock.TextProperty, _localError!);
    }
}
