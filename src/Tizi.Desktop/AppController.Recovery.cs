using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class AppController
{
    internal async Task RecoverSavedDataAsync(AppState backup)
    {
        if (_closing || Connected) throw new InvalidOperationException(UiText.Get("Disconnect before recovering saved data."));
        if (!await _operation.WaitAsync(0)) throw new InvalidOperationException(UiText.Get("Another operation is in progress."));
        Busy = true; Changed?.Invoke();
        try
        {
            var restored = OperationFeedback.ClearSavedErrors(Ready ? ProfileRecovery.MergeContents(State, backup) : backup with { LastConnected = false });
            ModelValidation.Rules(restored.Rules); AdvancedValidation.Check(restored);
            await SaveStateAsync(restored);
            State = restored; Ready = true; ProfileExists = true; LoadFailure = null;
            Logged?.Invoke(UiText.Get("Saved data recovered. Current preferences have been kept."));
        }
        finally { Busy = false; Changed?.Invoke(); _operation.Release(); }
    }
}
