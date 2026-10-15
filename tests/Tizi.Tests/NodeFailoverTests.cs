using System.Net;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class NodeFailoverTests
{
    private static ProxyNode Node(string name,string? region=null) => NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#"+Uri.EscapeDataString(name)).Single() with {CountryCode=region};
    private static AppState State(params ProxyNode[] nodes) => new() {Nodes=nodes.ToList(),SelectedNode=nodes[0].Id,AutoSwitchOnTimeout=true};
    [Fact] public async Task ExistingProfilesRemainOptedOutAndMakeNoRequests()
    {
        var current=Node("日本 1");var other=Node("日本 2");var calls=0;
        Assert.False(new AppState().AutoSwitchOnTimeout);
        Assert.Null(await NodeFailover.FindAvailableAsync(State(current,other) with {AutoSwitchOnTimeout=false},current,(_,_)=>{calls++;return Task.FromResult(20);}));
        Assert.Equal(0,calls);
    }
    [Fact] public void OneFailureDoesNotSwitchAndARecoveryBreaksTheStreak()
    {
        var tracker=new NodeFailureTracker();var node=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        Assert.False(tracker.Observe(node,true,now));Assert.False(tracker.Observe(node,false,now.AddSeconds(5)));
        Assert.False(tracker.Observe(node,true,now.AddSeconds(10)));Assert.True(tracker.Observe(node,true,now.AddSeconds(15)));
    }
    [Fact] public void CooldownSurvivesNodeChangesAndPendingFailuresDoNot()
    {
        var tracker=new NodeFailureTracker();var first=Guid.NewGuid();var second=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        Assert.False(tracker.Observe(first,true,now));Assert.True(tracker.Observe(first,true,now.AddSeconds(5)));
        tracker.ResetObservation();Assert.False(tracker.Observe(second,true,now.AddSeconds(10)));Assert.False(tracker.Observe(second,true,now.AddSeconds(15)));
        Assert.True(tracker.Observe(second,true,now.AddSeconds(36)));tracker.Reset();Assert.False(tracker.Observe(first,true,now.AddSeconds(37)));
    }
    [Theory]
    [InlineData("香港 1","香港 2",true)]
    [InlineData("香港 1","台湾 2",false)]
    [InlineData("澳门 1","中国 2",false)]
    [InlineData("🇹🇼 A","Taipei B",true)]
    [InlineData("London A","UK B",true)]
    [InlineData("Unknown A","Unknown B",false)]
    public void CountriesAndRegionsRemainDistinct(string first,string second,bool same) => Assert.Equal(same,NodeFailover.SameRegion(Node(first),Node(second)));
    [Fact] public async Task OnlySameRegionCandidatesAreProbedAndNothingCrossesTheBorder()
    {
        var current=Node("HK 1");var foreign=Node("JP 1");var same=Node("Hong Kong 2");var probed=new List<Guid>();
        var result=await NodeFailover.FindAvailableAsync(State(current,foreign,same),current,(node,_)=>{probed.Add(node.Id);return Task.FromResult(22);});
        Assert.Equal(same,result);Assert.Equal(new[]{same.Id},probed);
    }
    [Fact] public async Task NoSameRegionOrUnknownLocationDoesNotProbeOrSelectAnything()
    {
        foreach(var current in new[]{Node("日本 1"),Node("No location")})
        {
            var calls=0;var state=State(current,Node("美国 1"));
            Assert.Null(await NodeFailover.FindAvailableAsync(state,current,(_,_)=>{calls++;return Task.FromResult(20);}));
            Assert.Equal(0,calls);Assert.Equal(current.Id,state.SelectedNode);
        }
    }
    [Fact] public async Task AllSameRegionFailuresLeaveSelectionUnchanged()
    {
        var current=Node("日本 1");var state=State(current,Node("日本 2"),Node("日本 3"),Node("美国 1"));
        var result=await NodeFailover.FindAvailableAsync(state,current,(_,_)=>Task.FromException<int>(new TimeoutException()));
        Assert.Null(result);Assert.Equal(current.Id,state.SelectedNode);
    }
    [Fact] public async Task ZeroAndNegativeMeasurementsAreNeverHealthy()
    {
        var current=Node("日本 1");var zero=Node("日本 2");var negative=Node("日本 3");
        Assert.Null(await NodeFailover.FindAvailableAsync(State(current,zero,negative),current,(node,_)=>Task.FromResult(node.Id==zero.Id?0:-1)));
    }
    [Fact] public async Task HealthyWinnerCancelsAndDrainsOtherProbesWithinTheConcurrencyLimit()
    {
        var current=Node("日本 0");var nodes=Enumerable.Range(1,10).Select(i=>Node("日本 "+i)).ToArray();
        var state=State(new[]{current}.Concat(nodes).ToArray()) with {Latency=new() {Concurrency=2}};
        var active=0;var maximum=0;var cancelled=0;
        async Task<int> Measure(ProxyNode node,CancellationToken token)
        {
            var count=Interlocked.Increment(ref active);InterlockedExtensions.Max(ref maximum,count);
            try {await Task.Delay(node.Id==nodes[0].Id?40:1000,token);return 20;}
            catch(OperationCanceledException){Interlocked.Increment(ref cancelled);throw;}
            finally {Interlocked.Decrement(ref active);}
        }
        var result=await NodeFailover.FindAvailableAsync(state,current,Measure);
        Assert.Equal(nodes[0],result);Assert.InRange(maximum,1,2);Assert.True(cancelled>=1);Assert.Equal(0,active);
    }
    [Fact] public async Task CancellingAnInspectionPublishesNoCandidate()
    {
        var current=Node("日本 1");using var stop=new CancellationTokenSource();
        var work=NodeFailover.FindAvailableAsync(State(current,Node("日本 2")),current,async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return 10;},stop.Token);
        stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>work);
    }
    [Fact] public void ManualSelectionDisconnectDisableAndEditsRejectStaleCandidates()
    {
        var current=Node("日本 1");var other=Node("日本 2");var foreign=Node("美国 1");var state=State(current,other,foreign);
        Assert.True(NodeFailover.CanCommit(state,current.Id,other,true));
        Assert.False(NodeFailover.CanCommit(state,current.Id,other,false));
        Assert.False(NodeFailover.CanCommit(state with {SelectedNode=foreign.Id},current.Id,other,true));
        Assert.False(NodeFailover.CanCommit(state with {AutoSwitchOnTimeout=false},current.Id,other,true));
        Assert.False(NodeFailover.CanCommit(state with {Mode=RouteMode.Direct},current.Id,other,true));
        Assert.False(NodeFailover.CanCommit(state with {Nodes=[current,other with {Name="edited"}]},current.Id,other,true));
        Assert.False(NodeFailover.CanCommit(state with {Nodes=[current]},current.Id,other,true));
        Assert.False(NodeFailover.CanCommit(state,current.Id,foreign,true));
    }
    [Fact] public void OnlyTransportFailuresCountAndUserCancellationNeverDoes()
    {
        Assert.True(NodeFailover.IsUnavailable(new HttpRequestException("timeout",null,HttpStatusCode.GatewayTimeout),default));
        Assert.True(NodeFailover.IsUnavailable(new HttpRequestException("unavailable",null,HttpStatusCode.ServiceUnavailable),default));
        Assert.False(NodeFailover.IsUnavailable(new HttpRequestException("auth",null,HttpStatusCode.Unauthorized),default));
        Assert.False(NodeFailover.IsUnavailable(new HttpRequestException("missing",null,HttpStatusCode.NotFound),default));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.False(NodeFailover.IsUnavailable(new OperationCanceledException(),cancelled.Token));
    }
    private static class InterlockedExtensions
    {
        internal static void Max(ref int location,int value)
        {int old;do {old=Volatile.Read(ref location);if(old>=value)return;}while(Interlocked.CompareExchange(ref location,value,old)!=old);}
    }
}
