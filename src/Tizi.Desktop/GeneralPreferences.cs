using Tizi.Core;

namespace Tizi.Desktop;

internal sealed record GeneralPreferences(bool StartAtLogin, bool StartHidden, bool RestoreConnection, bool CloseToTray)
{
    internal static GeneralPreferences From(AppState state) => new(state.StartAtLogin, state.StartHidden, state.RestoreConnection, state.CloseToTray);
}
