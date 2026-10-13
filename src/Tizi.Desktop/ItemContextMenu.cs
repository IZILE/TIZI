using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

/// <summary>Select the operation target first; dispatch after the menu has finished retracting.</summary>
internal static class ItemContextMenu
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContextMenu, Action> ImmediateClosers = new();
    internal static void CloseImmediately(ContextMenu menu) { if (ImmediateClosers.TryGetValue(menu, out var close)) close(); else menu.Close(); }
    internal static ContextMenu Create(Control owner, Action select, IEnumerable<(string Label, Action Action)> actions)
    {
        var menu = new ContextMenu();
        var closing = false; var allowClose = false; var generation = 0; Action? pending = null;
        ImmediateClosers.Add(menu, () =>
        {
            if (!closing && !menu.IsOpen) return;
            ++generation; pending = null; allowClose = true;
            try { if (menu.FindLogicalAncestorOfType<Popup>() is { } popup) popup.Close(); else menu.Close(); }
            finally { allowClose = false; }
        });
        PopupRevealSurface? Surface() => menu.GetVisualDescendants().OfType<PopupRevealSurface>().FirstOrDefault();
        foreach (var (label, action) in actions)
        {
            var item = new MenuItem { [!MenuItem.HeaderProperty] = UiText.Bind(label) };
            item.Click += (_, _) =>
            {
                if (!menu.IsOpen) { select(); action(); return; }
                pending = () => { select(); action(); }; menu.Close();
            };
            menu.Items.Add(item);
        }
        menu.Opening += (_, _) => { if (closing) return; ++generation; select(); if (Surface() is { } surface) surface.IsHitTestVisible = true; };
        menu.Closing += (_, e) =>
        {
            if (allowClose || TopLevel.GetTopLevel(owner) is not Window { IsVisible: true } || Surface() is not { } surface) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; var current = ++generation; surface.IsHitTestVisible = false; surface.Progress = 0;
            _ = FinishCloseAsync(current);
        };
        menu.Closed += (_, _) =>
        {
            ++generation; closing = false;
            var action = pending; pending = null;
            if (action is not null) Dispatcher.UIThread.Post(action);
        };
        async Task FinishCloseAsync(int current)
        {
            await Task.Delay(180);
            if (current != generation || !closing) return;
            // ContextMenu.Close sets Popup.IsOpen=false. A cancelled close keeps the
            // popup host alive but leaves that property false, so assigning it again
            // would do nothing. The public Popup.Close disposes the retained host.
            allowClose = true;
            try { if (menu.FindLogicalAncestorOfType<Popup>() is { } popup) popup.Close(); else menu.Close(); }
            finally { allowClose = false; }
        }
        return menu;
    }
}
