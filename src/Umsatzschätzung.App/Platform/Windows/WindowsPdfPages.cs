using Umsatzschätzung.Service;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Umsatzschätzung.App.Platform;

public sealed class WindowsPdfPages : IPdfPages
{
    public async Task<List<byte[]>> Render(byte[] pdf, int dpi, CancellationToken ct)
    {
        using var source = new InMemoryRandomAccessStream();
        await source.WriteAsync(pdf.AsBuffer()).AsTask(ct);
        source.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(source).AsTask(ct);

        var pages = new List<byte[]>((int)document.PageCount);
        for (uint i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = document.GetPage(i);
            var (width, height) = Target(page.Size.Width, page.Size.Height, dpi);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = width,
                DestinationHeight = height,
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
            };
            using var png = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(png, options).AsTask(ct);
            pages.Add(await ReadAll(png, ct));
        }
        return pages;
    }

    // A scan wrapped one point per pixel reports an A4 page as 3307 x 4677 device
    // units instead of 794 x 1123, so scaling it to 300 dpi renders it at 10333 px
    // wide: a 600 MB bitmap holding no more detail than the 2480 px scan inside it.
    // The recogniser reads nothing above MaxImageDimension anyway, so stop there.
    static (uint Width, uint Height) Target(double w, double h, int dpi)
    {
        var scale = dpi / 96.0;
        var longest = Math.Max(w, h) * scale;
        if (longest > RapidOcr.MaxImageDimension) scale *= RapidOcr.MaxImageDimension / longest;
        return ((uint)Math.Max(1, Math.Round(w * scale)), (uint)Math.Max(1, Math.Round(h * scale)));
    }

    static async Task<byte[]> ReadAll(IRandomAccessStream stream, CancellationToken ct)
    {
        stream.Seek(0);
        var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
        var read = await stream.ReadAsync(buffer, buffer.Capacity, InputStreamOptions.None).AsTask(ct);
        return read.ToArray();
    }
}
