using System.Security.Cryptography;

namespace Tizi.Desktop;

internal static class VerificationStamp
{
    internal static object Current()
    {
        var path=Environment.ProcessPath!;
        using var file=File.OpenRead(path);
        return new { executable=Path.GetFileName(path),sha256=Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(),version=typeof(Program).Assembly.GetName().Version?.ToString(),utc=DateTimeOffset.UtcNow };
    }
}
