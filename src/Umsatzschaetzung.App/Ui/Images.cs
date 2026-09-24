using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public static class Images
{
    public static Bitmap From(Raster raster)
    {
        var pinned = GCHandle.Alloc(raster.Pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pinned.AddrOfPinnedObject(),
                new PixelSize(raster.Width, raster.Height), new Vector(96, 96), raster.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }
}
