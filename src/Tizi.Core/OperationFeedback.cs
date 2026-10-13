namespace Tizi.Core;

// Older profiles stored temporary operation failures alongside the user's content.
// Read them compatibly, but do not replay or save them as the current content state.
public static class OperationFeedback
{
    public static AppState ClearSavedErrors(AppState state)
    {
        var subscriptions = state.Subscriptions.Any(s => s.LastError is not null);
        var providers = state.Providers.Any(p => p.Error is not null);
        if (!subscriptions && !providers && state.ProviderSearchError is null) return state;
        return state with
        {
            Subscriptions = subscriptions ? state.Subscriptions.Select(s => s.LastError is null ? s : s with { LastError = null }).ToList() : state.Subscriptions,
            Providers = providers ? state.Providers.Select(p => p.Error is null ? p : p with { Error = null }).ToList() : state.Providers,
            ProviderSearchError = null
        };
    }
}
