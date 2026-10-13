using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>Current-user activation/import messages; credentials are never written to a pipe log.</summary>
internal sealed class SingleInstance : IDisposable
{
    private static string PipeName => "TIZI-activate-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..20];
    private readonly CancellationTokenSource _stop = new();
    internal SingleInstance(Action activate, string? testName = null, Action<string>? import = null) => _ = ListenAsync(activate, testName ?? PipeName, import);
    internal static bool ActivateExisting(string? testName = null, string? importLink = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", testName ?? PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(2500);
            if (importLink is null) client.WriteByte(1);
            else
            {
                var bytes = Encoding.UTF8.GetBytes(importLink); if (bytes.Length > 16384) return false;
                client.WriteByte(2); client.Write(BitConverter.GetBytes(bytes.Length)); client.Write(bytes);
            }
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException) { return false; }
    }
    private async Task ListenAsync(Action activate, string pipe, Action<string>? import)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var message = new byte[1];
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); readTimeout.CancelAfter(2000);
                if (await server.ReadAsync(message, readTimeout.Token).ConfigureAwait(false) != 1) continue;
                if (message[0] == 1) Dispatcher.UIThread.Post(activate);
                else if (message[0] == 2 && import is not null)
                {
                    var length = new byte[4]; await server.ReadExactlyAsync(length, readTimeout.Token).ConfigureAwait(false);
                    var count = BitConverter.ToInt32(length); if (count is < 1 or > 16384) continue;
                    var bytes = new byte[count]; await server.ReadExactlyAsync(bytes, readTimeout.Token).ConfigureAwait(false);
                    var link = Encoding.UTF8.GetString(bytes); Dispatcher.UIThread.Post(() => { activate(); import(link); });
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }
    public void Dispose() => _stop.Cancel();
}
