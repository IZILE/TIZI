using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

internal sealed record EditorField(string Key, string Label, string Value = "", bool Multiline = false, bool Secret = false);
internal sealed class ImportReviewCancelledException : Exception;
internal sealed class EditorDialog : CenteredDialog
{
    private readonly Dictionary<string, TextBox> _fields = [];
    private Func<Task> _submit = null!;
    private bool _submitting, _allowClose, _closingRequested, _closed;
    internal EditorSession Session { get; }
    internal CancellationToken Token => Session.Token;
    internal ClientNotification Notification { get; } = new();
    internal TextBox Field(string key) => _fields[key];
    internal Task SubmitAsync() => _submit();
    internal bool Applied { get; private set; }
    internal string? ErrorText { get; private set; }
    internal EditorDialog(string title, IReadOnlyList<EditorField> fields, Func<Dictionary<string, string>, Task> apply, string action = "Save")
        : this(title, fields, (values, session) => session.CommitAsync(() => apply(values)), action) { }

    internal EditorDialog(string title, IReadOnlyList<EditorField> fields, Func<Dictionary<string, string>, EditorSession, Task> apply,
        string action = "Save", CancellationToken cancellationToken = default)
    {
        Session = new EditorSession(cancellationToken);
        Title = UiText.Get(title); Width = 620; MaxHeight = 760; MinWidth = 460; SizeToContent = SizeToContent.Height; 
        Background = Skin.ChromeBrush; Padding = new Thickness(22); 
        var content = new StackPanel { Spacing = 14 };
        foreach (var field in fields)
        {
            var input = new TextBox { Text = field.Value, AcceptsReturn = field.Multiline, Height = field.Multiline ? 220 : double.NaN, TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalContentAlignment = field.Multiline ? VerticalAlignment.Top : VerticalAlignment.Center, TextAlignment = TextAlignment.Left, PasswordChar = field.Secret ? '●' : '\0', Classes = { "text-input" } };
            _fields.Add(field.Key, input);
            var caption = Ui.Text(field.Label, true);
            Avalonia.Automation.AutomationProperties.SetLabeledBy(input, caption);
            content.Children.Add(new StackPanel { Spacing = 6, Children = { caption, input } });
        }
        var error = Ui.Value(""); error.TextWrapping = TextWrapping.Wrap; error.Foreground = Skin.ErrorStrong; error.IsVisible = false;
        error.Tag = "editor-error";
        var progress = Ui.Text("Applying changes…", true, 13); progress.TextWrapping = TextWrapping.Wrap; progress.IsVisible = false;
        progress.Tag = "editor-progress";
        var save = new Button { Content = Ui.Text(action), Classes = { "accent" } }; var cancel = Ui.Button("Cancel", Close); save.Click += (_, _) => _ = Save();
        var actions = Ui.Horizontal(cancel, save); actions.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(error); content.Children.Add(progress); content.Children.Add(actions);
        var scroll = Ui.Scroll(content); scroll.MaxHeight = 650; Notification.MaxWidth = 520;
        SizeChanged += (_, _) => Notification.Width = Math.Min(520, Math.Max(180,
            ClientSize.Width - Padding.Left - Padding.Right - Notification.Margin.Left - Notification.Margin.Right));
        Content = new Grid { Children = { scroll, Notification } };
        UiText.WireChoices(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || !fields.Any(f => f.Multiline))) { _ = Save(); e.Handled = true; }
        };
        Opened += (_, _) => _fields.Values.FirstOrDefault()?.Focus();
        foreach (var input in _fields.Values) input.TextChanged += (_, _) =>
        {
            if (_submitting) return;
            ErrorText = null; error.IsVisible = false; error.Text = "";
        };
        Session.Changed += UpdateState;
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            if (!Session.TryCancel()) { e.Cancel = true; UpdateState(); return; }
            _closingRequested = true;
            // Keep the session and owning import reference alive until preparation
            // has observed cancellation; a late BeginCommit will then be rejected.
            if (_submitting) { e.Cancel = true; UpdateState(); }
            else _allowClose = true;
        };
        Closed += (_, _) => { _closed = true; Session.Changed -= UpdateState; Session.Dispose(); };
        void UpdateState()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(UpdateState); return; }
            if (_closed) return;
            save.IsEnabled = !_submitting && !Session.IsCancellationRequested;
            cancel.IsEnabled = !Session.IsCommitting && !_closingRequested;
            foreach (var input in _fields.Values) input.IsEnabled = !_submitting;
            progress.IsVisible = _submitting;
            if (_submitting) UiText.Set(progress, TextBlock.TextProperty,
                Session.IsCommitting ? "Applying changes…" : Session.IsCancellationRequested ? "Cancelling…" : "Working…");
        }
        async Task Save()
        {
            if (_submitting || _closed || Session.IsCancellationRequested) return;
            _submitting = true;
            try
            {
                Session.BeginAttempt(); UpdateState();
                await apply(_fields.ToDictionary(p => p.Key, p => p.Value.Text?.Trim() ?? ""), Session);
                // Preparations that return after cancellation must not be marked as
                // applied. A completed atomic commit is authoritative even if the
                // application lifetime was cancelled while it was finishing.
                if (!Session.IsCommitting) Session.ThrowIfCancellationRequested();
                ErrorText = null; error.IsVisible = false; Applied = true; _allowClose = true; await CloseSheetAsync();
            }
            catch (ImportReviewCancelledException) { error.IsVisible = false; }
            catch (OperationCanceledException) when (Session.IsCancellationRequested) { _closingRequested = true; }
            catch (Exception) when (Session.IsCancellationRequested && !Session.IsCommitting) { _closingRequested = true; }
            catch (Exception e) { ErrorText = e.Message; error.Text = UiText.Get(e.Message); error.IsVisible = true; Notification.Show(error.Text ?? ""); }
            finally
            {
                Session.FinishAttempt(); _submitting = false; UpdateState();
                if (!_closed && _closingRequested) { _allowClose = true; await CloseSheetAsync(); }
            }
        }
        _submit = Save;
    }
}
