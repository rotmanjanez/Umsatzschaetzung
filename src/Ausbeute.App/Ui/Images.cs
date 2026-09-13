using System.IO;
using System.Windows.Media.Imaging;

namespace Ausbeute.App.Ui;

public static class Images
{
    public static BitmapImage Decode(byte[] data)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(data);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
