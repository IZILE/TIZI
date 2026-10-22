using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class AppController
{
    private readonly DispatcherTimer _failoverTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly NodeFailureTracker _nodeFailures = new();
    private CancellationTokenSource? _failoverStop;
    private Task _failoverTask = Task.CompletedTask;
    private string? _failoverIdentity;
    private bool _automaticSwitchStopping;
    internal event Action<string>? NodeAutoSwitched;
    private bool CanAutoSwitch => State.AutoSwitchOnTimeout && !_qualityPreview && !DesktopChecks.Active && !_closing && !_automaticSwitchStopping
        && (!State.Tailscale.Enabled || State.Tailscale.ThroughProxy)
        && Connected && ConnectionState == ConnectionPhase.Connected && State.Mode != RouteMode.Direct;
    private void InitializeAutomaticSwitch() => _failoverTimer.Tick += (_, _) =>
    {
        if (CanAutoSwitch && !Busy && _failoverTask.IsCompleted) _failoverTask = CheckAutomaticSwitchAsync();
    };
    private void RefreshAutomaticSwitch()
    {
        var current = State.Nodes.FirstOrDefault(node => node.Id == State.SelectedNode);
        var identity = CanAutoSwitch && current is not null
            ? current.Id + ":" + current.Options.ToJsonString() + ":" + State.DelayUrl + ":" + State.Latency.TimeoutMs : null;
        if (identity != _failoverIdentity)
        {
            _failoverIdentity = identity; _failoverStop?.Cancel(); _nodeFailures.ResetObservation();
        }
        if (identity is null) { _failoverTimer.Stop(); _nodeFailures.Reset(); }
        else _failoverTimer.Start();
    }
    private async Task StopAutomaticSwitchAsync()
    {
        _automaticSwitchStopping = true; _failoverTimer.Stop(); _failoverStop?.Cancel(); await _failoverTask;
    }
    private async Task CheckAutomaticSwitchAsync()
    {
        var failed = State.Nodes.FirstOrDefault(node => node.Id == State.SelectedNode);
        if (!CanAutoSwitch || Busy || failed is null) return;
        var snapshot = State; var identity = _failoverIdentity;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _failoverStop = stop;
        try
        {
            var unavailable = false;
            try { unavailable = await Engine.DelayAsync(failed, snapshot.DelayUrl, stop.Token, snapshot.Latency.TimeoutMs) <= 0; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or TimeoutException or System.Net.Sockets.SocketException)
            { unavailable = NodeFailover.IsUnavailable(error, stop.Token); }
            if (stop.IsCancellationRequested || !CanAutoSwitch || _failoverIdentity != identity) return;
            if (!_nodeFailures.Observe(failed.Id, unavailable, DateTimeOffset.UtcNow)) return;
            var candidate = await NodeFailover.FindAvailableAsync(snapshot, failed,
                (node, token) => Engine.DelayAsync(node, snapshot.DelayUrl, token, snapshot.Latency.TimeoutMs), stop.Token);
            if (candidate is null || stop.IsCancellationRequested || !await _operation.WaitAsync(0, stop.Token)) return;
            try
            {
                if (!CanAutoSwitch || _failoverIdentity != identity || !NodeFailover.CanCommit(State, failed.Id, candidate, Connected)) return;
                Busy = true; NotifyChanged(); stop.Token.ThrowIfCancellationRequested();
                await Engine.SelectNodeAsync(candidate, _lifetime.Token);
                try { await ApplyAsync(State with { SelectedNode = candidate.Id }); }
                catch
                {
                    if (Connected && State.SelectedNode == failed.Id) await Engine.SelectNodeAsync(failed, _lifetime.Token);
                    throw;
                }
                var notice = UiText.Format("Switched automatically to {0}", candidate.Name);
                Logged?.Invoke(notice); NodeAutoSwitched?.Invoke(notice);
            }
            finally { Busy = false; _operation.Release(); if (!_closing) NotifyChanged(); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception) { if (!_closing) Logged?.Invoke(UiText.Get("Automatic node switching could not complete.")); }
        finally { if (ReferenceEquals(_failoverStop, stop)) _failoverStop = null; }
    }
}
