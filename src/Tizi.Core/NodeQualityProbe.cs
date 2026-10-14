using System.Net;

namespace Tizi.Core;

/// <summary>A temporary single-node global route prevents user rules from silently testing a direct exit.</summary>
public sealed class NodeQualityProbe : IAsyncDisposable
{
    private readonly EngineClient _engine=new();
    public HttpClient Http { get; private set; }=null!;
    public int ProcessId=>_engine.ProcessId;
    public long ProcessStartTicks=>_engine.ProcessStartTicks;
    public string RuntimeDirectory=>_engine.RuntimeDirectory;
    private NodeQualityProbe() { }
    public static AppState Configuration(AppState state,ProxyNode node,int port)=>NetworkEnvironment.ConnectionSettings(state with
    {
        Nodes=[node],SelectedNode=node.Id,Capture=CaptureMode.LocalOnly,Mode=RouteMode.Global,MixedPort=port,
        Rules=[],LastConnected=false,AutoUpdate=false,AutoUpdateProviders=false,
        Dns=state.Dns with { RespectRules=false },Logs=state.Logs with { Level="silent",Dns=false }
    });
    public static async Task<NodeQualityProbe> StartAsync(AppState state,ProxyNode node,string binary,string runtime,CancellationToken cancellation)
    {
        var probe=new NodeQualityProbe();
        try
        {
            var configuration=Configuration(state,node,EngineClient.FreePort());
            await probe._engine.StartAsync(configuration,binary,runtime,cancellation).ConfigureAwait(false);
            probe.Http=new HttpClient(new SocketsHttpHandler { Proxy=new WebProxy("http://127.0.0.1:"+configuration.MixedPort),UseProxy=true,UseCookies=false,AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(4),MaxConnectionsPerServer=2 }) { Timeout=Timeout.InfiniteTimeSpan };
            return probe;
        }
        catch { await probe.DisposeAsync().ConfigureAwait(false);throw; }
    }
    public async ValueTask DisposeAsync() { Http?.Dispose();await _engine.DisposeAsync().ConfigureAwait(false); }
}
