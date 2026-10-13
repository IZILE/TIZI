using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Tizi.Desktop;

/// <summary>Refresh the caret after the final centered layout, and normalize redundant numeric zeros.</summary>
public sealed class StableTextInput : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<StableTextInput, TextBox, bool>("Enabled");
    private static readonly ConditionalWeakTable<TextBox, State> States = new();
    static StableTextInput() => EnabledProperty.Changed.AddClassHandler<TextBox>((box, change) =>
    { if (change.NewValue is true) States.GetValue(box, b => new State(b)); else if (States.TryGetValue(box, out var state)) { state.Detach(); States.Remove(box); } });
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);
    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);
    private sealed class State
    {
        private readonly TextBox _box;
        private TextPresenter? _presenter;
        private object? _layout;
        private bool _normalizing;
        internal State(TextBox box)
        {
            _box = box; box.LayoutUpdated += Layout; box.TextChanged += Changed;
            box.TemplateApplied += Templated;
        }
        private void Templated(object? sender, TemplateAppliedEventArgs args) { _presenter = null; _layout = null; }
        private void Layout(object? sender, EventArgs args)
        {
            if (!_box.IsKeyboardFocusWithin || TopLevel.GetTopLevel(_box) is null) return;
            _presenter ??= _box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            if (_presenter is not { Bounds.Width: > 0 } presenter || !string.IsNullOrEmpty(presenter.PreeditText)) return;
            var layout = presenter.TextLayout;
            if (ReferenceEquals(layout, _layout)) return;
            _layout = layout;
            // Recompute from the layout used for painting, without changing the text or selection.
            presenter.MoveCaretToTextPosition(_box.CaretIndex);
        }
        private void Changed(object? sender, TextChangedEventArgs args)
        {
            _layout = null;
            if (_normalizing || !_box.IsKeyboardFocusWithin || !_box.GetVisualAncestors().OfType<NumericUpDown>().Any()) return;
            var text = _box.Text ?? "";
            var match = Regex.Match(text, @"^([+-]?)0+(?=\d)", RegexOptions.CultureInvariant);
            if (!match.Success) return;
            var removed = match.Length - match.Groups[1].Length;
            var normalized = match.Groups[1].Value + text[match.Length..];
            var caret = _box.CaretIndex; var start = _box.SelectionStart; var end = _box.SelectionEnd;
            _normalizing = true;
            try
            {
                _box.SetCurrentValue(TextBox.TextProperty, normalized);
                _box.CaretIndex = Math.Max(0, caret - removed); _box.SelectionStart = Math.Max(0, start - removed); _box.SelectionEnd = Math.Max(0, end - removed);
            }
            finally { _normalizing = false; }
        }
        internal void Detach() { _box.LayoutUpdated -= Layout; _box.TextChanged -= Changed; _box.TemplateApplied -= Templated; }
    }
}
