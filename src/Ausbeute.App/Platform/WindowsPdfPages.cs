using Ausbeute.Service;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Ausbeute.App.Platform;

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
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96.0)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * dpi / 96.0)),
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
            };
            using var png = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(png, options).AsTask(ct);
            pages.Add(await ReadAll(png, ct));
        }
        return pages;
    }

    static async Task<byte[]> ReadAll(IRandomAccessStream stream, CancellationToken ct)
    {
        stream.Seek(0);
        var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
        var read = await stream.ReadAsync(buffer, buffer.Capacity, InputStreamOptions.None).AsTask(ct);
        return read.ToArray();
    }
}
