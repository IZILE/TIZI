using System.Runtime.InteropServices;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using FormatException = System.FormatException;

namespace Tizi.Core;

public static class QrImport
{
    public const int MaxBytes = 24 * 1024 * 1024;
    public const long MaxPixels = 16_000_000;
    public static List<string> Decode(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new FormatException("QR image is too large (24 MB maximum).");
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data) ?? throw new FormatException("Unsupported or damaged image.");
        if (codec.Info.Width > 8192 || codec.Info.Height > 8192 || (long)codec.Info.Width * codec.Info.Height > MaxPixels) throw new FormatException("QR image dimensions are too large.");
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new FormatException("The QR image could not be decoded.");
        var pixels = new byte[bitmap.ByteCount]; Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] } };
        var source = new RGBLuminanceSource(pixels, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(source)?.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList() ?? [];
        if (results.Count == 0) throw new FormatException("No readable QR code found.");
        if (results.Count > 32 || results.Any(r => r.Length > 32768)) throw new FormatException("QR contents are too large.");
        return results;
    }
}
