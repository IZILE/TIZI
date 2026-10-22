using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class AppController
{
    private readonly IpQualityService _qualityService=new();
    private bool _qualityPreview;
    private string? _qualityIdentity;
    private CancellationTokenSource? _qualityCancellation;
    private Task _qualityTask=Task.CompletedTask;
    private DateTimeOffset _qualityRefreshedAt;
    internal IpQualityResult? IpQualityResult { get; private set; }
    internal bool IpQualityChecking { get; private set; }
    private void NotifyChanged() { RefreshAutomaticSwitch();ScheduleIpQuality();Changed?.Invoke(); }
    internal void RefreshExitCountryIfNeeded() => ScheduleIpQuality(refresh: true);
    private void ScheduleIpQuality(bool refresh = false)
    {
        var node=State.Nodes.FirstOrDefault(n=>n.Id==State.SelectedNode);
        var identity=!_qualityPreview && !Program.Headless && !_closing && Connected && ConnectionState==ConnectionPhase.Connected && (node is not null || State.Tailscale.Enabled || State.Mode==RouteMode.Direct)
            ? State.Mode+":"+node?.Id+":"+node?.Options.ToJsonString()+":"+System.Text.Json.JsonSerializer.Serialize(State.IpQuality)+":"+System.Text.Json.JsonSerializer.Serialize(State.Tailscale) : null;
        var same = identity==_qualityIdentity;
        if(same && (!refresh || IpQualityChecking || DateTimeOffset.UtcNow-_qualityRefreshedAt<TimeSpan.FromMinutes(2)))return;
        _qualityIdentity=identity;_qualityCancellation?.Cancel();if(!same)IpQualityResult=null;IpQualityChecking=false;
        if(identity is null)return;
        _qualityRefreshedAt=DateTimeOffset.UtcNow;
        IpQualityChecking=true;var cancellation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);cancellation.CancelAfter(TimeSpan.FromSeconds(24));
        _qualityCancellation=cancellation;var previous=_qualityTask;
        _qualityTask=InspectIpAsync(State,node,identity,cancellation,previous);
    }
    private async Task InspectIpAsync(AppState state,ProxyNode? node,string identity,CancellationTokenSource cancellation,Task previous)
    {
        try
        {
            await previous;await Task.Delay(300,cancellation.Token);
            IpQualityResult result;
            if(state.Mode==RouteMode.Direct)
            {
                using var http=Engine.CreateLocalProxyHttpClient();
                result=await _qualityService.QueryCountryAsync(http,cancellation.Token);
            }
            else if(state.Tailscale.Enabled)
            {
                // Inspect the active phone route, not the transit node in a second
                // engine with a competing copy of the same Tailscale identity.
                using var http=Engine.CreateTailscaleHttpClient();
                result=state.IpQuality.Enabled ? await _qualityService.QueryAsync(http,state.IpQuality,cancellation.Token) : await _qualityService.QueryCountryAsync(http,cancellation.Token);
            }
            else
            {
                await using var probe=await NodeQualityProbe.StartAsync(state,node!,Path.Combine(AppContext.BaseDirectory,"engine/mihomo.exe"),Path.Combine(UserData.DirectoryPath,"quality-run"),cancellation.Token);
                StartProxyMonitor("--quality-watchdog",probe.ProcessId,probe.ProcessStartTicks,probe.RuntimeDirectory);
                result=state.IpQuality.Enabled ? await _qualityService.QueryAsync(probe.Http,state.IpQuality,cancellation.Token) : await _qualityService.QueryCountryAsync(probe.Http,cancellation.Token);
            }
            if(identity==_qualityIdentity && !cancellation.IsCancellationRequested && Connected)IpQualityResult=result;
        }
        catch(OperationCanceledException) { }
        catch(Exception)
        { if(identity==_qualityIdentity && !cancellation.IsCancellationRequested)IpQualityResult=new("",[new("IP detection",null,null,null,null,null,"Unavailable")],null,DateTimeOffset.UtcNow); }
        finally
        {
            var current=ReferenceEquals(_qualityCancellation,cancellation);
            if(current)_qualityCancellation=null;
            cancellation.Dispose();
            if(current && identity==_qualityIdentity) { IpQualityChecking=false;if(!_closing)Changed?.Invoke(); }
        }
    }
}
