using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Tizi.Core;

namespace Tizi.Desktop;

internal static partial class ConnectionChecks
{
    private static async Task CheckCancellationAsync(Action<bool,string> check)
    {
        static async Task Until(Func<bool> condition)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(!condition()) await Task.Delay(10,deadline.Token);
        }
        static ProxyNode Node(ProxyFixture fixture)=>NodeImport.FromOptions(new JsonObject{["name"]="Cancellable local fixture",["type"]="http",["server"]="127.0.0.1",["port"]=fixture.Port});
        static AppState State(ProxyNode node)=>new(){Nodes=[node],SelectedNode=node.Id,Capture=CaptureMode.LocalOnly,Mode=RouteMode.Global,MixedPort=EngineClient.FreePort(),DelayUrl="http://example.test/cancellable",AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false};
        await using(var fixture=new ProxyFixture())
        await using(var app=new AppController(preview:true))
        {
            await app.ApplyAsync(State(Node(fixture)) with{LastConnected=true});
            var cancelled=false;
            app.Changed+=()=>{if(!cancelled&&app.ConnectionState==ConnectionPhase.Connecting){cancelled=true;app.CancelConnection();}};
            await app.ExecuteAsync(app.ToggleAsync);
            check(cancelled&&!app.Busy&&!app.Connected&&!app.Engine.IsRunning&&app.ConnectionState==ConnectionPhase.Disconnected&&app.ConnectionFailure is null,"Cancellation before engine startup returns to idle without an error");
            check(!app.State.LastConnected&&!new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.LastConnected&&fixture.RequestCount==0,"Early cancellation clears reconnect intent without issuing a proxy request");
        }
        await using(var fixture=new ProxyFixture())
        {
            new Vault(UserData.DirectoryPath).Write("settings.dat",State(Node(fixture)));
            var window=new MainWindow(); var errors=new List<string>();window.Controller.Error+=errors.Add;window.Show();
            try
            {
                await window.Controller.InitializeAsync();
                window.ConnectAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await fixture.Observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
                check(window.Controller.Busy&&window.ConnectAction.Phase==ConnectionPhase.Connecting&&window.ConnectAction.IsEnabled,"The real connection button stays clickable while the HTTP preflight is pending");
                var runtime=window.Controller.Engine.RuntimeDirectory;var watch=Stopwatch.StartNew();
                window.ConnectAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(window.Controller.ConnectionState==ConnectionPhase.Cancelling&&!window.ConnectAction.IsEnabled,"A second button click enters cancellation and blocks repeated cleanup actions");
                window.ConnectAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(()=>!window.Controller.Busy);
                check(watch.Elapsed<TimeSpan.FromSeconds(5)&&!window.Controller.Connected&&!window.Controller.Engine.IsRunning&&!Directory.Exists(runtime)&&window.Controller.ConnectionState==ConnectionPhase.Disconnected,"Cancellation stops the pending request and engine before the normal connection timeout");
                check(errors.Count==0&&window.Controller.ConnectionFailure is null&&!window.Controller.State.LastConnected&&window.ConnectAction.IsEnabled,"User cancellation leaves no red failure, timeout notification, or reconnect intent");
                fixture.Reply.TrySetResult(true);await Task.Delay(150);
                check(!window.Controller.Connected&&!window.Controller.Engine.IsRunning&&fixture.RequestCount==1,"A late successful response cannot activate a cancelled connection or start another request");
                await using var retry=new ProxyFixture();retry.Reply.TrySetResult(true);var retryNode=Node(retry);
                await window.Controller.ApplyAsync(State(retryNode));
                window.ConnectAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>!window.Controller.Busy&&window.Controller.Connected);
                check(window.Controller.ConnectionState==ConnectionPhase.Connected&&window.ConnectAction.IsEnabled,"The same button can connect successfully after cancellation cleanup");
                window.ConnectAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>!window.Controller.Busy&&!window.Controller.Connected);
                check(!window.Controller.Engine.IsRunning,"Disconnect after a cancelled attempt and retry stops the replacement test engine");
            }
            finally{window.Close();await Until(()=>!window.IsVisible);}
        }
        await using(var fixture=new ProxyFixture())
        {
            var app=new AppController(preview:true);await app.ApplyAsync(State(Node(fixture)));
            var connect=app.ExecuteAsync(app.ToggleAsync);await fixture.Observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var runtime=app.Engine.RuntimeDirectory;
            await app.DisposeAsync();await connect;
            check(!app.Engine.IsRunning&&!Directory.Exists(runtime)&&!app.Connected,"Closing during a pending connection cancels its request and waits for engine cleanup");
        }
    }
}
