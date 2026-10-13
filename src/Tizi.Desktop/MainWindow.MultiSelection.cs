using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private readonly HashSet<string> _selectedMany = [];
    private readonly List<RoutingRule> _selectedRules = [];
    private bool _multiSelect, _selectionManagement;
    private string? _suppressSelectionClick, _selectionAnchor;
    private KeyModifiers _selectionModifiers;
    private bool _contextSelection;
    private RemovalBubble? _removalBubble;
    internal IReadOnlySet<string> SelectedItems => _selectedMany;
    private void AttachSelectionGestures()
    {
        DispatcherTimer? timer = null; Point origin = default;
        void Stop() { timer?.Stop(); timer = null; }
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            Stop(); _selectionModifiers = e.KeyModifiers; _contextSelection = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed;
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed || e.Source is not Visual source) return;
            var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
            if (!ancestors.Contains(_contentSurface)) return;
            var button = ancestors.OfType<Button>().FirstOrDefault(); if (button is null) return;
            var id = button.Tag as string;
            if (!IsVisibleTarget(id)) id = _subscriptionTree.Groups.FirstOrDefault(g => ReferenceEquals(g.Header, button) && g.Key != "manual") is { } group ? "subscription:" + group.Key : null;
            if (!IsVisibleTarget(id)) return; origin = e.GetPosition(this); var itemId = id!;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            timer.Tick += (_, _) =>
            {
                Stop(); if (!IsVisibleTarget(itemId)) return; _suppressSelectionClick = itemId;
                _selectionModifiers = KeyModifiers.None;
                if (!_multiSelect) BeginMultipleSelection(itemId, true);
                else
                {
                    _selectionManagement = true;
                    if (!_selectedMany.Contains(itemId)) SelectMultipleItem(itemId);
                    else { _chosen = itemId; UpdateSelectionActions(); ApplyMultipleSelectionVisuals(); }
                }
            }; timer.Start();
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) =>
        {
            Stop(); var suppressed = _suppressSelectionClick;
            // Let the button consume this release first; also clear suppression after a release outside.
            Dispatcher.UIThread.Post(() => { if (_suppressSelectionClick == suppressed) _suppressSelectionClick = null; });
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerMoved += (_, e) => { var delta=e.GetPosition(this)-origin; if (timer is not null && delta.X*delta.X+delta.Y*delta.Y > 64) Stop(); };
        KeyDown += (_, e) =>
        {
            _contextSelection = false;
            if (e.Key == Key.Escape && _multiSelect) { ClearSelection(); e.Handled = true; }
        };
        Closed += (_, _) => Stop();
        Deactivated += (_, _) => { Stop(); _suppressSelectionClick = null; _selectionModifiers = KeyModifiers.None; _contextSelection = false; };
    }
    private List<string> VisibleSelectionItems() => _page == "Nodes"
        ? _subscriptionTree.Groups.SelectMany(g => g.NodesShown ? g.NodeRows.ItemIds : g.Key == "manual" ? [] : new[] { "subscription:" + g.Key }).Where(IsVisibleTarget).ToList()
        : _visibleTargets.ToList();
    private void BeginMultipleSelection(string? id, bool management)
    {
        _multiSelect = true; _selectionManagement = management; _selectedMany.Clear();
        if (id is not null && IsVisibleTarget(id)) _selectedMany.Add(id);
        _chosen = _selectedMany.FirstOrDefault(); _selectionAnchor = _chosen; RememberSelectedRules(); _chosenRule = _selectedRules.FirstOrDefault(); UpdateSelectionActions(); ApplyMultipleSelectionVisuals();
    }
    private void RememberSelectedRules()
    {
        _selectedRules.Clear(); if (_page != "Rules") return;
        foreach (var id in _selectedMany) if (int.TryParse(id, out var i) && i >= 0 && i < _app.State.Rules.Count) _selectedRules.Add(_app.State.Rules[i]);
    }
    private void SelectMultipleItem(string id)
    {
        if (_selectionModifiers.HasFlag(KeyModifiers.Shift) && _selectionAnchor is { } anchor)
        {
            var visible = VisibleSelectionItems(); var first = visible.IndexOf(anchor); var last = visible.IndexOf(id);
            if (first >= 0 && last >= 0) _selectedMany.UnionWith(visible.Skip(Math.Min(first,last)).Take(Math.Abs(last-first)+1));
        }
        else if (!_selectedMany.Add(id)) _selectedMany.Remove(id);
        _selectionModifiers = KeyModifiers.None;
        if (_page == "Nodes" && _selectedMany.Contains(id))
        {
            if (id.StartsWith("subscription:") && Guid.TryParse(id[13..], out var sub)) _selectedMany.ExceptWith(_app.State.Nodes.Where(n=>n.SubscriptionId==sub).Select(n=>n.Id.ToString()));
            else if (Guid.TryParse(id, out var node) && _app.State.Nodes.FirstOrDefault(n=>n.Id==node)?.SubscriptionId is { } owner) _selectedMany.Remove("subscription:"+owner);
        }
        _chosen = _selectedMany.Contains(id) ? id : _selectedMany.FirstOrDefault(); _selectionAnchor ??= id;
        RememberSelectedRules(); _chosenRule = _selectedRules.FirstOrDefault(); UpdateSelectionActions(); ApplyMultipleSelectionVisuals();
    }
    private void ApplyMultipleSelectionVisuals()
    {
        if (!_multiSelect) return;
        _rows.SelectMany(_selectedMany); _subscriptionTree.SelectItems(_selectedMany);
    }
    private void ValidateMultipleSelection()
    {
        if (_page == "Rules")
        {
            _selectedMany.Clear();
            foreach (var rule in _selectedRules) { var index=_app.State.Rules.FindIndex(r=>ReferenceEquals(r,rule)); if(index>=0) _selectedMany.Add(index.ToString()); }
        }
        _selectedMany.RemoveWhere(id => !IsVisibleTarget(id)); _chosen = _selectedMany.Contains(_chosen ?? "") ? _chosen : _selectedMany.FirstOrDefault();
        RememberSelectedRules(); if (_selectedMany.Count == 0) _selectionManagement = false;
    }
    private async void RemoveMultipleItems()
    {
        if (_preview || _selectedMany.Count == 0) return;
        var ids = _selectedMany.Where(IsVisibleTarget).ToHashSet(); var page=_page; var before=_app.State;
        var subIds=ids.Where(id=>id.StartsWith("subscription:")).Select(id=>Guid.Parse(id[13..])).ToHashSet();
        var nodeIds=before.Nodes.Where(n=>ids.Contains(n.Id.ToString()) || n.SubscriptionId is { } owner && subIds.Contains(owner)).Select(n=>n.Id).ToHashSet();
        var removedRules=_selectedRules.ToArray();
        if(page=="Nodes" && subIds.Count>0 && !await ConfirmRemovalAsync(UiText.Format("{0} subscriptions", subIds.Count),nodeIds.Count))return;
        if (_page != page || !ids.SetEquals(_selectedMany.Where(IsVisibleTarget))) return;
        Run(async ()=>
        {
            var current=_app.State; AppState next; Func<AppState,AppState> undo;
            if(page=="Nodes")
            {
                if(_app.Connected && current.SelectedNode is { } active && nodeIds.Contains(active))throw new InvalidOperationException(UiText.Get("Disconnect or switch nodes before removing the current node."));
                var nodes=current.Nodes.Where(n=>nodeIds.Contains(n.Id)).ToArray(); var subs=current.Subscriptions.Where(s=>subIds.Contains(s.Id)).ToArray();
                next=current with {Nodes=current.Nodes.Where(n=>!nodeIds.Contains(n.Id)).ToList(),Subscriptions=current.Subscriptions.Where(s=>!subIds.Contains(s.Id)).ToList(),SelectedNode=nodeIds.Contains(current.SelectedNode??Guid.Empty)?null:current.SelectedNode};
                undo=state=>
                {
                    if(state.Nodes.Any(n=>nodeIds.Contains(n.Id))||state.Subscriptions.Any(s=>subIds.Contains(s.Id)||subs.Any(r=>SourceUpdates.Normalize(r.Url)==SourceUpdates.Normalize(s.Url))))throw new InvalidOperationException(UiText.Get("Items changed after removal. Recover an encrypted backup instead."));
                    return state with { Nodes=RestoreAt(state.Nodes,current.Nodes,nodes),Subscriptions=RestoreAt(state.Subscriptions,current.Subscriptions,subs),SelectedNode=state.SelectedNode??current.SelectedNode };
                };
            }
            else if(page=="Providers")
            {
                var removed=current.Providers.Where(p=>ids.Contains(p.Id.ToString())).ToArray();
                next=current with {Providers=current.Providers.Where(p=>!ids.Contains(p.Id.ToString())).ToList()};
                undo=state=>
                {
                    if(state.Providers.Any(p=>removed.Any(r=>r.Id==p.Id||SourceUpdates.Normalize(r.Url)==SourceUpdates.Normalize(p.Url))))throw new InvalidOperationException(UiText.Get("Items changed after removal. Recover an encrypted backup instead."));
                    return state with {Providers=RestoreAt(state.Providers,current.Providers,removed)};
                };
            }
            else if(page=="Rules")
            {
                var remaining=current.Rules.Where(r=>!removedRules.Any(x=>ReferenceEquals(x,r))).ToList(); next=current with {Rules=remaining};
                undo=state=>state.Rules.SequenceEqual(remaining) ? state with {Rules=RestoreAt(state.Rules,current.Rules,removedRules)} : throw new InvalidOperationException(UiText.Get("Items changed after removal. Recover an encrypted backup instead."));
            }
            else return;
            await _app.ApplyAsync(next,true); ClearSelection(); OfferUndo(undo);
        });
    }
    private static List<T> RestoreAt<T>(List<T> current,List<T> previous,IEnumerable<T> removed)
    { var restored=current.ToList();foreach(var item in removed.OrderBy(previous.IndexOf))restored.Insert(Math.Min(previous.IndexOf(item),restored.Count),item);return restored; }
}
