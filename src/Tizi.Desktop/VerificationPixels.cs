using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Tizi.Desktop;

// Headless frames and native bitmaps can expose different channel orders.
internal static class VerificationPixels
{
    internal static byte[] Bgra(Bitmap image)
    {
        using var converted=new WriteableBitmap(image.PixelSize,image.Dpi,PixelFormat.Bgra8888,AlphaFormat.Premul);
        using var buffer=converted.Lock();image.CopyPixels(buffer);
        var stride=image.PixelSize.Width*4;var pixels=new byte[stride*image.PixelSize.Height];
        for(var y=0;y<image.PixelSize.Height;y++)Marshal.Copy(buffer.Address+y*buffer.RowBytes,pixels,y*stride,stride);
        return pixels;
    }
}
