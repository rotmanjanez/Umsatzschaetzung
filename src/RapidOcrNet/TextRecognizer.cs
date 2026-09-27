// Apache-2.0 license
// Modified for Umsatzschätzung; the changes against the vendored commit are in the git history.
// Adapted from RapidAI / RapidOCR
// https://github.com/RapidAI/RapidOCR/blob/92aec2c1234597fa9c3c270efd2600c83feecd8d/dotnet/RapidOcrOnnxCs/OcrLib/CrnnNet.cs

using System.Text;
using SkiaSharp;
using Umsatzschaetzung.Nets;

namespace RapidOcrNet;

public sealed class TextRecognizer : IDisposable
{
    private static readonly float[] MeanValues = [127.5F, 127.5F, 127.5F];
    private static readonly float[] NormValues = [1.0F / 127.5F, 1.0F / 127.5F, 1.0F / 127.5F];
    private const int CrnnDstHeight = 48;
    //private const int CrnnDefaultWidth = 320; // matches PP-OCR rec_img_shape [3, 48, 320]
    //private const int RecBatchNum = 6;

    private INet _crnnNet = null!;
    private INet? _accelerated;
    private volatile bool _acceleratorFailed;
    private string[] _keys = null!;
    private string _inputName = null!;

    /// <summary>
    /// <paramref name="keys"/> is the dictionary file, a key per line. <paramref name="accelerated"/>
    /// reads beside the cores; a run that fails there is read on the cores.
    /// </summary>
    public void InitModel(INet net, byte[] keys, INet? accelerated = null)
    {
        _crnnNet = net;
        _accelerated = accelerated;
        _inputName = net.Inputs[0].Name;
        _keys = InitKeys(keys);
    }

    private static string[] InitKeys(byte[] dictionary)
    {
        using (var sr = new StreamReader(new MemoryStream(dictionary), Encoding.UTF8))
        {
            List<string> keys = ["#"];

            while (sr.ReadLine() is { } line)
            {
                keys.Add(line);
            }

            keys.Add(" ");
            System.Diagnostics.Debug.WriteLine($"keys Size = {keys.Count}");

            return keys.ToArray();
        }
    }

    public async Task<TextLine[]> GetTextLines(SKBitmap[] partImgs)
    {
        // NOTE: Python's pipeline batches crops by aspect ratio and zero-right-pads
        // each crop to 48 * max(w/h, 320/48) so the recognizer sees its training
        // distribution. Empirically the bundled PP-OCRv5 latin ONNX model in this
        // repo does NOT cope well with that right-side padding, it produces wrong
        // characters and 1-char substitutions on a few inputs. So we keep the legacy
        // per-image, tight-fit recognizer call (which the model evidently was
        // re-tuned for) while still recording CTC column indices.
        // A crop is too small a run to spread over cores; the crops are spread instead, and
        // the session is best given one intra-op thread. A line costs its width, so the cores
        // take the narrowest and the accelerator, whose fixed cost per run is highest, the
        // widest; without an accelerator the widest go first, so none is left for last on one
        // core while the others idle. A runtime that finishes a run on the thread that asks
        // for it keeps every core busy; a single-threaded one reads the crops one after another.
        var textLines = new TextLine[partImgs.Length];
        var widestFirst = Enumerable.Range(0, partImgs.Length).OrderByDescending(i => partImgs[i].Width / (float)partImgs[i].Height).ToArray();
        var cores = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        if (_accelerated is null || _acceleratorFailed)
        {
            await Parallel.ForEachAsync(widestFirst, cores, async (i, _) => textLines[i] = await GetTextLine(partImgs[i]));
            return textLines;
        }

        var gate = new Lock();
        int widest = 0, narrowest = widestFirst.Length - 1;
        int Next(bool wide)
        {
            lock (gate) return widest > narrowest ? -1 : wide ? widestFirst[widest++] : widestFirst[narrowest--];
        }
        var accelerator = Task.Run(async () =>
        {
            for (int i; (i = Next(wide: true)) >= 0;) textLines[i] = await GetAcceleratedTextLine(partImgs[i]);
        });
        await Parallel.ForEachAsync(Enumerable.Range(0, Environment.ProcessorCount), cores, async (_, _) =>
        {
            for (int i; (i = Next(wide: false)) >= 0;) textLines[i] = await GetTextLine(partImgs[i]);
        });
        await accelerator;
        return textLines;
    }

    private async Task<TextLine> GetAcceleratedTextLine(SKBitmap src)
    {
        if (!_acceleratorFailed)
        {
            try
            {
                return await GetTextLine(src, _accelerated!);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _acceleratorFailed = true;
            }
        }
        return await GetTextLine(src);
    }

    public async Task<TextLine> GetTextLine(SKBitmap src)
    {
        try
        {
            return await GetTextLine(src, _crnnNet);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message + ex.StackTrace);
        }

        return new TextLine();
    }

    private async Task<TextLine> GetTextLine(SKBitmap src, INet net)
    {
        var sw = ValueStopwatch.StartNew();
        float scale = CrnnDstHeight / (float)src.Height;
        // A sliver far taller than wide rounds to no width at all, and Skia hands back no
        // bitmap for that.
        int dstWidth = Math.Max((int)(src.Width * scale), 1);

        Tensor input;
        using (SKBitmap srcResize = src.Resize(new SKSizeI(dstWidth, CrnnDstHeight), OcrUtils.NetworkSampling))
        {
            input = OcrUtils.SubtractMeanNormalize(srcResize, MeanValues, NormValues, _inputName);
        }

        var outputs = await net.Run([input]);
        var tl = ScoreToTextLine(outputs[0]);
        tl.Time = (float)sw.ElapsedMilliseconds;
        return tl;
    }

    private TextLine ScoreToTextLine(Tensor srcData)
    {
        int h = srcData.Shape[1];
        int w = srcData.Shape[2];

        ReadOnlySpan<float> data = srcData.F;

        int lastIndex = 0;
        var scores = new List<float>();
        var chars = new List<string>();
        var cols = new List<int>();

        for (int i = 0; i < h; i++)
        {
            int maxIndex = 0;
            float maxValue = -1000F;
            ReadOnlySpan<float> row = data.Slice(i * w, w);

            for (int j = 0; j < w; j++)
            {
                float v = row[j];
                if (v > maxValue)
                {
                    maxIndex = j;
                    maxValue = v;
                }
            }

            if (maxIndex > 0 && maxIndex < _keys.Length && !(i > 0 && maxIndex == lastIndex))
            {
                scores.Add(maxValue);
                chars.Add(_keys[maxIndex]);
                cols.Add(i);
            }

            lastIndex = maxIndex;
        }

        return new TextLine
        {
            Chars = chars.ToArray(),
            CharScores = scores.ToArray(),
            CharCols = cols.ToArray(),
            ColCount = h,
            LineTxtLen = h
        };
    }

    public void Dispose()
    {
        _crnnNet?.Dispose();
        _accelerated?.Dispose();
    }
}
