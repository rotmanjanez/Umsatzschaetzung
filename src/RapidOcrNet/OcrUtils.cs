// Apache-2.0 license
// Modified for Umsatzschätzung; the changes against the vendored commit are in the git history.
// Adapted from RapidAI / RapidOCR
// https://github.com/RapidAI/RapidOCR/blob/92aec2c1234597fa9c3c270efd2600c83feecd8d/dotnet/RapidOcrOnnxCs/OcrLib/OcrUtils.cs

using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace RapidOcrNet;

/// <summary>
/// Geometry bookkeeping recorded while cropping a detection quad out of the source image.
/// Used to map word-box rectangles in recognized-image coords back to original source coords.
/// </summary>
internal readonly struct CropContext
{
    public readonly int Left;
    public readonly int Top;

    /// <summary>Width of the rectified strip (partImg) before any 90° pre-rotation.</summary>
    public readonly int PartImgWidth;

    /// <summary>Height of the rectified strip (partImg) before any 90° pre-rotation.</summary>
    public readonly int PartImgHeight;

    /// <summary>Forward perspective matrix mapping imgCrop coords → partImg coords.</summary>
    public readonly SKMatrix PerspectiveMatrix;

    public readonly bool HasPerspective;

    /// <summary>True if a 90° CW rotation was applied to partImg before recognition.</summary>
    public readonly bool Rotated90;

    public CropContext(int left, int top, int partImgWidth, int partImgHeight,
        SKMatrix perspectiveMatrix, bool hasPerspective, bool rotated90)
    {
        Left = left;
        Top = top;
        PartImgWidth = partImgWidth;
        PartImgHeight = partImgHeight;
        PerspectiveMatrix = perspectiveMatrix;
        HasPerspective = hasPerspective;
        Rotated90 = rotated90;
    }
}

internal static class OcrUtils
{
    /// <summary>
    /// Sampling used for detector / classifier / recognizer network inputs. Python
    /// uses cv2.INTER_LINEAR; bilinear sampling would be the natural analogue, but
    /// empirically the bundled PP-OCRv5 ONNX models behave better when fed with the
    /// same Mitchell cubic resampler the original C# port used. Switching to bilinear
    /// shifts pixel values enough to flip 180° on borderline classifier inputs.
    /// </summary>
    public static readonly SKSamplingOptions NetworkSampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

    /// <summary>
    /// Sampling used for the rotate-crop perspective warp. Python uses
    /// cv2.INTER_CUBIC; Mitchell is what the bundled ONNX models were tuned with.
    /// </summary>
    public static readonly SKSamplingOptions WarpSampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

    public static Tensor<float> SubtractMeanNormalize(SKBitmap src, float[] meanVals, float[] normVals)
    {
        int cols = src.Width;
        int rows = src.Height;
        int rowBytes = src.RowBytes; // Use actual row stride (may include padding)
        int plane = rows * cols;

        var inputTensor = new DenseTensor<float>([1, 3, rows, cols]);
        Memory<float> buffer = inputTensor.Buffer;

        if (src.Info.ColorType == SKColorType.Gray8)
        {
            Parallel.For(0, rows, r =>
            {
                Span<float> data = buffer.Span;
                ReadOnlySpan<byte> row = src.GetPixelSpan().Slice(r * rowBytes, cols);
                for (int ch = 0; ch < 3; ++ch)
                {
                    float mean = meanVals[ch], norm = normVals[ch];
                    Span<float> dst = data.Slice(ch * plane + r * cols, cols);
                    for (int c = 0; c < cols; ++c)
                    {
                        dst[c] = (row[c] - mean) * norm;
                    }
                }
            });
        }
        else if (src.Info.ColorType == SKColorType.Bgra8888)
        {
            Parallel.For(0, rows, r =>
            {
                Span<float> data = buffer.Span;
                ReadOnlySpan<byte> row = src.GetPixelSpan().Slice(r * rowBytes, cols * 4);
                for (int ch = 0; ch < 3; ++ch)
                {
                    float mean = meanVals[ch], norm = normVals[ch];
                    Span<float> dst = data.Slice(ch * plane + r * cols, cols);
                    for (int c = 0; c < cols; ++c)
                    {
                        dst[c] = (row[c * 4 + ch] - mean) * norm;
                    }
                }
            });
        }
        else
        {
            throw new ArgumentException($"This image needs to be '{SKColorType.Bgra8888}' or '{SKColorType.Gray8}', but got '{src.Info.ColorType}'.");
        }

        return inputTensor;
    }

    /// <summary>
    /// PP-OCR-style image bounding: first downscale so the longer side is ≤
    /// <paramref name="maxSideLen"/> (Python <c>reduce_max_side</c>), then upscale if
    /// the shorter side is below <paramref name="minSideLen"/> (Python
    /// <c>increase_min_side</c>). Dst dimensions are rounded to the nearest /32.
    /// Returns the source unchanged if both bounds are non-positive or already met: the
    /// detector's own resize rounds to the stride, so a page is not resampled only for it.
    /// </summary>
    public static SKBitmap ResizeImageWithinBounds(SKBitmap src, int minSideLen, int maxSideLen, out bool owned)
    {
        int srcW = src.Width;
        int srcH = src.Height;

        int dstW = srcW;
        int dstH = srcH;

        // Step 1: reduce_max_side
        int maxV = Math.Max(dstW, dstH);
        if (maxSideLen > 0 && maxV > maxSideLen)
        {
            float ratio = maxSideLen / (float)maxV;
            dstW = (int)(dstW * ratio);
            dstH = (int)(dstH * ratio);
        }

        // Step 2: increase_min_side
        int minV = Math.Min(dstW, dstH);
        if (minSideLen > 0 && minV < minSideLen)
        {
            float ratio = minSideLen / (float)minV;
            dstW = (int)(dstW * ratio);
            dstH = (int)(dstH * ratio);
        }

        if (dstW == srcW && dstH == srcH)
        {
            owned = false;
            return src;
        }

        // Round to nearest /32 to satisfy the detector's stride.
        dstW = RoundToMultiple32(dstW);
        dstH = RoundToMultiple32(dstH);

        var resized = Bands.Resize(src, src.Info.WithSize(dstW, dstH), NetworkSampling);
        owned = true;
        return resized;
    }

    private static int RoundToMultiple32(int value)
    {
        int rounded = (int)Math.Round(value / 32.0) * 32;
        return Math.Max(rounded, 32);
    }

    /// <summary>
    /// Apply Python-style conditional vertical letterbox: only adds top+bottom padding
    /// when the input is too wide (w/h &gt; widthHeightRatio) or too short (h &lt; minHeight).
    /// Returns the padded bitmap and the top-padding amount (left-padding is always 0).
    /// </summary>
    public static SKBitmap ApplyVerticalLetterbox(SKBitmap src, float widthHeightRatio, int minHeight, out int paddingTop)
    {
        paddingTop = 0;

        int w = src.Width;
        int h = src.Height;

        bool useLimitRatio = widthHeightRatio > 0 && (w / (float)h > widthHeightRatio);
        bool tooShort = h <= minHeight;
        if (!useLimitRatio && !tooShort)
        {
            return src;
        }

        // Python: new_h = max(int(w / wh_ratio), min_height) * 2; padding_h = abs(new_h - h)/2
        int referenceWidth = widthHeightRatio > 0 ? (int)(w / widthHeightRatio) : minHeight;
        int newH = Math.Max(referenceWidth, minHeight) * 2;
        int padH = Math.Abs(newH - h) / 2;
        if (padH <= 0)
        {
            return src;
        }

        var info = src.Info;
        info.Height = h + 2 * padH;

        var padded = new SKBitmap(info);
        using (var canvas = new SKCanvas(padded))
        using (var image = SKImage.FromBitmap(src))
        {
            // White matches the rest of the C# pipeline's "light background" assumption
            // (the bundled PP-OCRv5 ONNX is tuned for it). Python uses BLACK here; the
            // model bundled with rapidocr-python was trained accordingly.
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, 0, padH, WarpSampling);
        }

        paddingTop = padH;
        return padded;
    }

    public static SKBitmap MakePadding(SKBitmap src, int padding)
    {
        if (padding <= 0)
        {
            return src;
        }

        SKImageInfo info = src.Info;

        info.Width += 2 * padding;
        info.Height += 2 * padding;

        SKBitmap newBmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(newBmp))
        using (var image = SKImage.FromBitmap(src))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, padding, padding, WarpSampling);
        }

//#if DEBUG
//        using (var fs = new FileStream($"Padding_{Guid.NewGuid()}.png", FileMode.Create))
//        {
//            newBmp.Encode(fs, SKEncodedImageFormat.Png, 100);
//        }
//#endif

        return newBmp;
    }

    public static int GetThickness(SKBitmap boxImg)
    {
        int minSize = boxImg.Width > boxImg.Height ? boxImg.Height : boxImg.Width;
        return minSize / 1000 + 2;
    }

    /// <summary>
    /// Crop and rectify every detection quad in <paramref name="textBoxes"/>. Returns an
    /// eagerly-allocated array; if any single crop throws partway through, all already
    /// allocated bitmaps are disposed before the exception propagates.
    /// </summary>
    public static SKBitmap[] GetPartImages(SKBitmap src, IReadOnlyList<TextBox>? textBoxes,
        bool rotateTall = true, float padding = 0)
    {
        if (textBoxes is null || textBoxes.Count == 0)
        {
            return [];
        }

        var images = new SKBitmap[textBoxes.Count];
        int produced = 0;
        try
        {
            for (int i = 0; i < textBoxes.Count; ++i)
            {
                images[i] = GetRotateCropImage(src, textBoxes[i].BoxPoints, out _, rotateTall, padding);
                produced = i + 1;
            }

            return images;
        }
        catch
        {
            for (int i = 0; i < produced; i++)
            {
                images[i].Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// Like <see cref="GetPartImages"/> but also records per-crop <see cref="CropContext"/>
    /// bookkeeping for later word-box inverse mapping. Same exception-safety contract.
    /// </summary>
    public static (SKBitmap[] PartImages, CropContext[] Contexts) GetPartImagesWithContext(SKBitmap src,
        IReadOnlyList<TextBox>? textBoxes, bool rotateTall = true, float padding = 0)
    {
        if (textBoxes is null || textBoxes.Count == 0)
        {
            return ([], []);
        }

        var images = new SKBitmap[textBoxes.Count];
        var contexts = new CropContext[textBoxes.Count];
        int produced = 0;
        try
        {
            for (int i = 0; i < textBoxes.Count; ++i)
            {
                images[i] = GetRotateCropImage(src, textBoxes[i].BoxPoints, out contexts[i], rotateTall, padding);
                produced = i + 1;
            }

            return (images, contexts);
        }
        catch
        {
            for (int i = 0; i < produced; i++)
            {
                images[i].Dispose();
            }
            throw;
        }
    }

    public static SKMatrix GetPerspectiveTransform(in SKPointI topLeft, in SKPointI topRight, in SKPointI botRight, in SKPointI botLeft,
        float width, float height)
    {
        // https://stackoverflow.com/questions/48416118/perspective-transform-in-skia

        float x1 = topLeft.X;
        float y1 = topLeft.Y;
        float x2 = topRight.X;
        float y2 = topRight.Y;
        float x3 = botRight.X;
        float y3 = botRight.Y;
        float x4 = botLeft.X;
        float y4 = botLeft.Y;

        float w = width;
        float h = height;

        float scaleX = (y1 * x2 * x4 - x1 * y2 * x4 + x1 * y3 * x4 - x2 * y3 * x4 - y1 * x2 * x3 + x1 * y2 * x3 - x1 * y4 * x3 + x2 * y4 * x3) / (x2 * y3 * w + y2 * x4 * w - y3 * x4 * w - x2 * y4 * w - y2 * w * x3 + y4 * w * x3);
        float skewX = (-x1 * x2 * y3 - y1 * x2 * x4 + x2 * y3 * x4 + x1 * x2 * y4 + x1 * y2 * x3 + y1 * x4 * x3 - y2 * x4 * x3 - x1 * y4 * x3) / (x2 * y3 * h + y2 * x4 * h - y3 * x4 * h - x2 * y4 * h - y2 * h * x3 + y4 * h * x3);
        float transX = x1;
        float skewY = (-y1 * x2 * y3 + x1 * y2 * y3 + y1 * y3 * x4 - y2 * y3 * x4 + y1 * x2 * y4 - x1 * y2 * y4 - y1 * y4 * x3 + y2 * y4 * x3) / (x2 * y3 * w + y2 * x4 * w - y3 * x4 * w - x2 * y4 * w - y2 * w * x3 + y4 * w * x3);
        float scaleY = (-y1 * x2 * y3 - y1 * y2 * x4 + y1 * y3 * x4 + x1 * y2 * y4 - x1 * y3 * y4 + x2 * y3 * y4 + y1 * y2 * x3 - y2 * y4 * x3) / (x2 * y3 * h + y2 * x4 * h - y3 * x4 * h - x2 * y4 * h - y2 * h * x3 + y4 * h * x3);
        float transY = y1;
        float persp0 = (x1 * y3 - x2 * y3 + y1 * x4 - y2 * x4 - x1 * y4 + x2 * y4 - y1 * x3 + y2 * x3) / (x2 * y3 * w + y2 * x4 * w - y3 * x4 * w - x2 * y4 * w - y2 * w * x3 + y4 * w * x3);
        float persp1 = (-y1 * x2 + x1 * y2 - x1 * y3 - y2 * x4 + y3 * x4 + x2 * y4 + y1 * x3 - y4 * x3) / (x2 * y3 * h + y2 * x4 * h - y3 * x4 * h - x2 * y4 * h - y2 * h * x3 + y4 * h * x3);
        float persp2 = 1;

        var persp = new SKMatrix(scaleX, skewX, transX, skewY, scaleY, transY, persp0, persp1, persp2);

        return persp.TryInvert(out SKMatrix perspInv) ? perspInv : SKMatrix.Identity; // TODO - Check what's best to return when not inv
    }


    /// <summary>
    /// Cut a detection that ran several short lines together into one box per line.
    /// DBNet returns one connected component; a column of two-glyph tokens - a unit column
    /// reading "kg", "kg", "kg" down the page - comes back as a single tall box, and a line
    /// reader has no way to read it: turned it is sideways, upright it is squeezed to a
    /// sliver. Boxes that are not clearly taller than they are wide, that are skewed, or
    /// whose ink does not fall into separate bands are returned untouched.
    /// </summary>
    public static IReadOnlyList<TextBox> SplitStackedBoxes(SKBitmap src, IReadOnlyList<TextBox> boxes)
    {
        List<TextBox>? split = null;
        for (var i = 0; i < boxes.Count; i++)
        {
            var bands = Stacked(src, boxes[i]);
            if (bands is null)
            {
                split?.Add(boxes[i]);
                continue;
            }
            split ??= [.. boxes.Take(i)];
            split.AddRange(bands);
        }
        return split ?? boxes;
    }

    // The bands of a stacked box, or null when it is one line after all.
    static List<TextBox>? Stacked(SKBitmap src, TextBox box)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var p in box.BoxPoints)
        {
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
        }
        x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, src.Width - 1); y1 = Math.Min(y1, src.Height - 1);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 4 || h < 4 * MinBand || h < 1.5 * w) return null;

        // A quad that leans is a line at an angle, not a stack: cutting it horizontally
        // would slice through its own glyphs.
        var lean = Math.Max(
            Math.Abs(box.BoxPoints[0].Y - box.BoxPoints[1].Y),
            Math.Abs(box.BoxPoints[0].X - box.BoxPoints[3].X));
        if (lean > w / 4) return null;

        // The bitmap is read once: GetPixel costs about a microsecond and a page carries these
        // boxes by the dozen. Reading every other column instead is a third of the work again
        // and costs a cell on the fixtures, so the profile sees every pixel.
        var lum = new byte[w * h];
        int dark = 255, light = 0;
        for (var y = 0; y < h; y++)
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                var v = Luminance(src.GetPixel(x0 + x, y0 + y));
                lum[i] = (byte)v;
                if (v < dark) dark = v;
                if (v > light) light = v;
            }
        if (light - dark < 40) return null;
        var threshold = (dark + light) / 2;

        var ink = new int[h];
        for (var y = 0; y < h; y++)
            for (int x = 0, i = y * w; x < w; x++, i++)
                if (lum[i] < threshold) ink[y]++;

        var bands = new List<(int Top, int Height)>();
        int? start = null;
        for (var y = 0; y < h; y++)
        {
            if (ink[y] > 1 && start is null) start = y;
            else if (ink[y] <= 1 && start is { } s) { if (y - s >= MinBand) bands.Add((s, y - s)); start = null; }
        }
        if (start is { } last && h - last >= MinBand) bands.Add((last, h - last));
        if (bands.Count < 2) return null;

        // What the unclip pulled in from the row above or below is a fragment, not a line:
        // too short to be one, or cut off by the edge of the box.
        var tallest = bands.Max(b => b.Height);
        bands.RemoveAll(b => b.Height * 2 < tallest || (b.Height < tallest && (b.Top == 0 || b.Top + b.Height == h)));
        if (bands.Count < 2) return null;

        return [.. bands.Select(b => new TextBox
        {
            Score = box.Score,
            BoxPoints =
            [
                new SKPointI(x0, y0 + b.Top),
                new SKPointI(x1, y0 + b.Top),
                new SKPointI(x1, y0 + b.Top + b.Height),
                new SKPointI(x0, y0 + b.Top + b.Height),
            ],
        })];
    }

    const int MinBand = 6;

    static int Luminance(SKColor c) => (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;

    public static SKBitmap GetRotateCropImage(SKBitmap src, SKPointI[] box, bool rotateTall = true)
    {
        return GetRotateCropImage(src, box, out _, rotateTall);
    }

    /// <summary>
    /// <paramref name="padding"/> widens a line across its run, as a share of its height on
    /// either side: a box a few pixels off its line otherwise cuts the tail of a comma, which
    /// then reads as a point.
    /// </summary>
    public static SKBitmap GetRotateCropImage(SKBitmap src, SKPointI[] box, out CropContext context,
        bool rotateTall = true, float padding = 0)
    {
        System.Diagnostics.Debug.Assert(box.Length == 4);

        var (b0, b1, b2, b3) = padding > 0 && IsRun(box)
            ? Across(box[0], box[1], box[2], box[3], padding, src.Width, src.Height)
            : (box[0], box[1], box[2], box[3]);

        // NOTE: must be independent `if`s (not `if/else if`) — a monotonically-decreasing
        // sequence would otherwise update `left` every iteration and never touch `right`,
        // leaving it at int.MinValue and producing a degenerate SKRectI below.
        ReadOnlySpan<int> collectX = stackalloc int[] { b0.X, b1.X, b2.X, b3.X };
        int left = int.MaxValue;
        int right = int.MinValue;
        foreach (var v in collectX)
        {
            if (v < left)
            {
                left = v;
            }

            if (v > right)
            {
                right = v;
            }
        }

        ReadOnlySpan<int> collectY = stackalloc int[] { b0.Y, b1.Y, b2.Y, b3.Y };
        int top = int.MaxValue;
        int bottom = int.MinValue;
        foreach (var v in collectY)
        {
            if (v < top)
            {
                top = v;
            }

            if (v > bottom)
            {
                bottom = v;
            }
        }

        SKRectI rect = new SKRectI(left, top, right, bottom);

        var info = src.Info;
        info.Width = rect.Width;
        info.Height = rect.Height;

        SKBitmap imgCrop = new SKBitmap(info);
        if (!src.ExtractSubset(imgCrop, rect))
        {
            imgCrop.Dispose();
            throw new InvalidOperationException($"Could not extract image subset for rect {rect} from {src.Width}x{src.Height} source.");
        }

        ref SKPointI p0 = ref b0;
        p0.X -= left;
        p0.Y -= top;

        ref SKPointI p1 = ref b1;
        p1.X -= left;
        p1.Y -= top;

        ref SKPointI p2 = ref b2;
        p2.X -= left;
        p2.Y -= top;

        ref SKPointI p3 = ref b3;
        p3.X -= left;
        p3.Y -= top;

        int imgCropWidth = (int)Math.Sqrt((p0.X - p1.X) * (p0.X - p1.X) + (p0.Y - p1.Y) * (p0.Y - p1.Y));
        int imgCropHeight = (int)Math.Sqrt((p0.X - p3.X) * (p0.X - p3.X) + (p0.Y - p3.Y) * (p0.Y - p3.Y));

        var m = GetPerspectiveTransform(in p0, in p1, in p2, in p3, imgCropWidth, imgCropHeight);

        if (m.IsIdentity)
        {
            bool rotated = rotateTall && imgCrop.Height >= imgCrop.Width * 1.5;
            context = new CropContext(
                left, top,
                imgCrop.Width, imgCrop.Height,
                SKMatrix.Identity, hasPerspective: false,
                rotated90: rotated);

            if (rotated)
            {
                var rotated90 = BitmapRotateClockWise90(imgCrop);
                imgCrop.Dispose();
                return rotated90;
            }

            return imgCrop;
        }

        var info2 = imgCrop.Info;
        info2.Width = imgCropWidth;
        info2.Height = imgCropHeight;

        var partImg = new SKBitmap(info2);
        using (var canvas = new SKCanvas(partImg))
        using (var image = SKImage.FromBitmap(imgCrop))
        {
            canvas.SetMatrix(m);
            canvas.DrawImage(image, 0, 0, WarpSampling);
            canvas.Restore();
        }
        imgCrop.Dispose();

        bool rotated90Flag = rotateTall && partImg.Height >= partImg.Width * 1.5;
        context = new CropContext(
            left, top,
            partImg.Width, partImg.Height,
            m, hasPerspective: true,
            rotated90: rotated90Flag);

        if (rotated90Flag)
        {
            var rotated90 = BitmapRotateClockWise90(partImg);
            partImg.Dispose();
            return rotated90;
        }

        return partImg;
    }

    /// <summary>A box wider along its run than across it: a line, not a stack or a single glyph.</summary>
    public static bool IsRun(SKPointI[] box) =>
        Square(box[1].X - box[0].X, box[1].Y - box[0].Y) > Square(box[3].X - box[0].X, box[3].Y - box[0].Y);

    /// <summary>
    /// Takes back what <see cref="GetRotateCropImage(SKBitmap, SKPointI[], out CropContext, bool, float)"/>
    /// spared around a line from a box read in its crop, so words keep the height of their line.
    /// </summary>
    public static void Unpad(SKPointI[] box, float padding)
    {
        (box[0], box[1], box[2], box[3]) = Across(box[0], box[1], box[2], box[3], -padding / (1 + 2 * padding), int.MaxValue, int.MaxValue);
    }

    static int Square(int x, int y) => x * x + y * y;

    static (SKPointI, SKPointI, SKPointI, SKPointI) Across(SKPointI b0, SKPointI b1, SKPointI b2, SKPointI b3,
        float share, int width, int height)
    {
        float dx = (b3.X - b0.X + b2.X - b1.X) / 2f * share;
        float dy = (b3.Y - b0.Y + b2.Y - b1.Y) / 2f * share;

        SKPointI Move(SKPointI p, int side) => new(
            Math.Clamp((int)MathF.Round(p.X + side * dx), 0, width - 1),
            Math.Clamp((int)MathF.Round(p.Y + side * dy), 0, height - 1));
        return (Move(b0, -1), Move(b1, -1), Move(b2, 1), Move(b3, 1));
    }

    public static SKBitmap BitmapRotateClockWise180(SKBitmap src)
    {
        var rotated = new SKBitmap(src.Info);

        using (var canvas = new SKCanvas(rotated))
        using (var image = SKImage.FromBitmap(src))
        {
            canvas.Translate(rotated.Width, rotated.Height);
            canvas.RotateDegrees(180);
            canvas.DrawImage(image, 0, 0, WarpSampling);
            canvas.Restore();
        }

        return rotated;
    }

    public static SKBitmap BitmapRotateClockWise90(SKBitmap src)
    {
        var info = src.Info;
        (info.Width, info.Height) = (info.Height, info.Width);

        var rotated = new SKBitmap(info);

        using (var canvas = new SKCanvas(rotated))
        using (var image = SKImage.FromBitmap(src))
        {
            canvas.Translate(rotated.Width, 0);
            canvas.RotateDegrees(90);
            canvas.DrawImage(image, 0, 0, WarpSampling);
            canvas.Restore();
        }

        return rotated;
    }

    /// <summary>
    /// Counter-clockwise 90° rotation matching <c>numpy.rot90</c> (k=1, default axes).
    /// Used as the pre-recognition rotation for tall (vertical) crops, so that the
    /// recognizer reads top-to-bottom characters in the same order as Python.
    /// </summary>
    public static SKBitmap BitmapRotateCounterClockWise90(SKBitmap src)
    {
        var info = src.Info;
        (info.Width, info.Height) = (info.Height, info.Width);

        var rotated = new SKBitmap(info);

        using (var canvas = new SKCanvas(rotated))
        using (var image = SKImage.FromBitmap(src))
        {
            canvas.Translate(0, rotated.Height);
            canvas.RotateDegrees(-90);
            canvas.DrawImage(image, 0, 0, WarpSampling);
            canvas.Restore();
        }

        return rotated;
    }
}
