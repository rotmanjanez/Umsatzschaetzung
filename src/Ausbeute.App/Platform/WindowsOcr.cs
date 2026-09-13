using Ausbeute.Model;
using Ausbeute.Service;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Ausbeute.App.Platform;

public sealed class WindowsOcr : IOcr
{
    OcrEngine? engine;

    public async Task<OcrPageWords> Recognize(byte[] image, CancellationToken ct)
    {
        engine ??= OcrEngine.TryCreateFromLanguage(new Language("de-DE"))
            ?? throw new InvalidOperationException("Die deutsche Texterkennung (OCR) ist auf diesem Rechner nicht installiert. Bitte das Sprachpaket Deutsch mit der Funktion „Optische Zeichenerkennung“ installieren.");

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(image.AsBuffer()).AsTask(ct);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        var max = (int)OcrEngine.MaxImageDimension;
        var scale = Math.Max(width, height) > max ? (double)max / Math.Max(width, height) : 1.0;

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct);

        var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
        var words = new List<Model.OcrWord>();
        foreach (var line in result.Lines)
            foreach (var w in line.Words)
                words.Add(new Model.OcrWord { Text = w.Text, Box = Scaled(w.BoundingRect, scale) });
        return new OcrPageWords(width, height, words);
    }

    static Box Scaled(Windows.Foundation.Rect r, double scale) => new(
        (int)Math.Round(r.X / scale),
        (int)Math.Round(r.Y / scale),
        (int)Math.Round(r.Width / scale),
        (int)Math.Round(r.Height / scale));
}
