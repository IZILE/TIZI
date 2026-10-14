using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class AppController
{
    private readonly IpQualityService _qualityService=new();
    private bool _qualityPreview;
    private string? _qualityIdentity;
    private CancellationTokenSource? _qualityCancellation;
    private Task _qualityTask=Task.CompletedTask;
    internal IpQualityResult? IpQualityResult { get; private set; }
    internal bool IpQualityChecking { get; private set; }
    private void NotifyChanged() { ScheduleIpQuality();Changed?.Invoke(); }
    private void ScheduleIpQuality()
    {
        var node=State.Nodes.FirstOrDefault(n=>n.Id==State.SelectedNode);
        var identity=!_qualityPreview && !_closing && Connected && ConnectionState==ConnectionPhase.Connected && State.Mode!=RouteMode.Direct && State.IpQuality.Enabled && node is not null
            ? node.Id+":"+node.Options.ToJsonString()+":"+System.Text.Json.JsonSerializer.Serialize(State.IpQuality) : null;
        if(identity==_qualityIdentity)return;
        _qualityIdentity=identity;_qualityCancellation?.Cancel();IpQualityResult=null;IpQualityChecking=false;
        if(identity is null || node is null)return;
        IpQualityChecking=true;var cancellation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);cancellation.CancelAfter(TimeSpan.FromSeconds(24));
        _qualityCancellation=cancellation;var previous=_qualityTask;
        _qualityTask=InspectIpAsync(State,node,identity,cancellation,previous);
    }
    private async Task InspectIpAsync(AppState state,ProxyNode node,string identity,CancellationTokenSource cancellation,Task previous)
    {
        try
        {
            await previous;await Task.Delay(300,cancellation.Token);
            await using var probe=await NodeQualityProbe.StartAsync(state,node,Path.Combine(AppContext.BaseDirectory,"engine/mihomo.exe"),Path.Combine(UserData.DirectoryPath,"quality-run"),cancellation.Token);
            StartProxyMonitor("--quality-watchdog",probe.ProcessId,probe.ProcessStartTicks,probe.RuntimeDirectory);
            var result=await _qualityService.QueryAsync(probe.Http,state.IpQuality,cancellation.Token);
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
