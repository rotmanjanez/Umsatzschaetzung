using Avalonia.Media.Imaging;

namespace Umsatzschätzung.App.Ui;

public static class Images
{
    public static Bitmap Decode(byte[] data) => new(new MemoryStream(data));
}
