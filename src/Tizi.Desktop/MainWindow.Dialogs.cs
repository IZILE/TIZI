using Avalonia.Controls;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private readonly List<CenteredDialog> _dialogs=[];
    internal IReadOnlyList<CenteredDialog> OpenDialogs=>_dialogs;
    internal void AttachDialog(CenteredDialog dialog)
    {
        ActiveMoreSheet?.DismissImmediately();
        _dialogs.Add(dialog);_clientSurface.Children.Add(dialog);UpdateDialogInput();
    }
    internal void ActivateDialog(CenteredDialog dialog)
    {
        if(!_dialogs.Remove(dialog))return;
        _dialogs.Add(dialog);_clientSurface.Children.Remove(dialog);_clientSurface.Children.Add(dialog);UpdateDialogInput();
    }
    internal void DetachDialog(CenteredDialog dialog)
    { _clientSurface.Children.Remove(dialog);_dialogs.Remove(dialog);UpdateDialogInput(); }
    private void UpdateDialogInput()
    {
        // Block background input without applying every control's disabled visual state.
        _clientSurface.Children[0].IsHitTestVisible=!_dialogs.Any(dialog=>dialog.Modal);
        for(var i=0;i<_dialogs.Count;i++)_dialogs[i].IsHitTestVisible=i==_dialogs.Count-1;
        if(_dialogs.LastOrDefault() is { } active && !active.IsKeyboardFocusWithin)
            Avalonia.Threading.Dispatcher.UIThread.Post(()=>
            { if(_dialogs.LastOrDefault()==active&&!active.IsKeyboardFocusWithin)active.Focus(); });
    }
    private bool CloseDialogsForOwner()
    {
        foreach(var dialog in _dialogs.ToArray().Reverse())if(!dialog.CloseForOwner())return false;
        return true;
    }
}
