using Umsatzschätzung.Extract;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Umsatzschätzung.App.Platform;

public sealed class WindowsOcr : IOcr
{
    static readonly BitmapRotation[] Retries =
        [BitmapRotation.Clockwise180Degrees, BitmapRotation.Clockwise90Degrees, BitmapRotation.Clockwise270Degrees];

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

        var best = await Pass(decoder, scale, BitmapRotation.None, ct);
        if (Orientation.Score(best.Words) < Orientation.Confident)
        {
            foreach (var rotation in Retries)
            {
                var pass = await Pass(decoder, scale, rotation, ct);
                if (Orientation.Score(pass.Words) > Orientation.Score(best.Words)) best = pass;
            }
        }
        if (best.Rotation == BitmapRotation.None)
            return new OcrPageWords(width, height, best.Words);
        return new OcrPageWords(best.Width, best.Height, best.Words, await Rotate(decoder, best.Rotation, ct));
    }

    sealed record Pass(BitmapRotation Rotation, int Width, int Height, List<Model.OcrWord> Words);

    async Task<Pass> Pass(BitmapDecoder decoder, double scale, BitmapRotation rotation, CancellationToken ct)
    {
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
            Rotation = rotation,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct);

        var result = await engine!.RecognizeAsync(bitmap).AsTask(ct);
        var words = new List<Model.OcrWord>();
        foreach (var line in result.Lines)
            foreach (var w in line.Words)
                words.Add(new Model.OcrWord { Text = w.Text, Box = Scaled(w.BoundingRect, scale) });
        return new Pass(rotation, (int)Math.Round(bitmap.PixelWidth / scale), (int)Math.Round(bitmap.PixelHeight / scale), words);
    }

    static async Task<byte[]> Rotate(BitmapDecoder decoder, BitmapRotation rotation, CancellationToken ct)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateForTranscodingAsync(output, decoder).AsTask(ct);
        encoder.BitmapTransform.Rotation = rotation;
        await encoder.FlushAsync().AsTask(ct);
        output.Seek(0);
        var buffer = new Windows.Storage.Streams.Buffer((uint)output.Size);
        var read = await output.ReadAsync(buffer, buffer.Capacity, InputStreamOptions.None).AsTask(ct);
        return read.ToArray();
    }

    static Box Scaled(Windows.Foundation.Rect r, double scale) => new(
        (int)Math.Round(r.X / scale),
        (int)Math.Round(r.Y / scale),
        (int)Math.Round(r.Width / scale),
        (int)Math.Round(r.Height / scale));
}
