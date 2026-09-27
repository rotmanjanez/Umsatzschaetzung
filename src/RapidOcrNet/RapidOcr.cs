// Apache-2.0 license
// Modified for Umsatzschätzung; the changes against the vendored commit are in the git history.
// Adapted from RapidAI / RapidOCR
// https://github.com/RapidAI/RapidOCR/blob/92aec2c1234597fa9c3c270efd2600c83feecd8d/dotnet/RapidOcrOnnxCs/OcrLib/OcrLite.cs

using SkiaSharp;
using System.Text;
using Umsatzschaetzung.Nets;

namespace RapidOcrNet;

public sealed class RapidOcr : IDisposable
{
    private readonly TextDetector _textDetector = new TextDetector();
    private readonly TextClassifier _textClassifier = new TextClassifier();
    private readonly TextRecognizer _textRecognizer = new TextRecognizer();

    /// <summary>
    /// Opens the model set's nets from <paramref name="weights"/>: the detector, whose single
    /// large input suits an accelerator, with <paramref name="detector"/>, the classifier and
    /// recognizer with <paramref name="reader"/>. <paramref name="acceleratedReader"/> opens a
    /// second recognizer that reads the widest lines beside the cores. The model set carries
    /// the detector's per-version normalization, so v6 detectors are wired up correctly.
    /// </summary>
    public async Task InitModels(IWeights weights, RapidOcrModelSet models, NetOptions detector, NetOptions reader, NetOptions? acceleratedReader = null)
    {
        ArgumentNullException.ThrowIfNull(models);

        _textDetector.InitModel(await weights.Open(models.DetModelPath, detector), models.DetMean, models.DetStd);
        _textClassifier.InitModel(await weights.Open(models.ClsModelPath, reader));
        var recognizer = await weights.Open(models.RecModelPath, reader);
        var keys = await weights.Read(models.KeysPath);
        _textRecognizer.InitModel(recognizer, keys, acceleratedReader is { } accelerated ? await weights.Open(models.RecModelPath, accelerated) : null);
    }

    public async Task<OcrResult> Detect(string path, RapidOcrOptions options)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find image to process: '{path}'.", path);
        }

        using (var originSrc = SKBitmap.Decode(path))
        {
            return await Detect(originSrc, options);
        }
    }

    /// <summary>
    /// <paramref name="read"/> is shown the boxes and their classified angles before any crop
    /// is read; returning false skips recognition and returns those blocks unread.
    /// </summary>
    public async Task<OcrResult> Detect(SKBitmap originSrc, RapidOcrOptions options, Predicate<TextBlock[]>? read = null)
    {
        using var input = PrepareDetectorInput(originSrc, options);
        var textBoxes = await _textDetector.GetTextBoxes(input.Bitmap, input.Scale, options.BoxScoreThresh, options.BoxThresh, options.UnClipRatio) ?? [];
        if (options.SplitStackedCrops) textBoxes = OcrUtils.SplitStackedBoxes(input.Bitmap, textBoxes);
        return await ReadBoxes(input, textBoxes, options, read);
    }

    /// <summary>
    /// Reads lines found before, in the page's own pixels, the way <see cref="Detect(SKBitmap, RapidOcrOptions, Predicate{TextBlock[]}?)"/>
    /// reads the lines it finds: a page turned once it was detected is read in its lines,
    /// turned with it, instead of being detected again.
    /// </summary>
    public async Task<OcrResult> Read(SKBitmap page, IEnumerable<TextBox> boxes, RapidOcrOptions options)
    {
        using var input = new DetectorInput(page, new ScaleParam(page.Width, page.Height, page.Width, page.Height),
            0, 0, 1, 1, page.Width, page.Height, null, null, null);
        return await ReadBoxes(input, TextDetector.SortBoxesInReadingOrder([.. boxes]), options, null);
    }

    /// <summary>
    /// Runs the detection stage only and returns the raw text boxes, skipping angle
    /// classification and recognition. Mirrors Python rapidocr's
    /// <c>ocr(image, use_det=True, use_cls=False, use_rec=False)</c> call. Useful when
    /// you need layout boxes before deciding how to crop and OCR the image (e.g. split
    /// a scan into columns or per-region passes).
    /// </summary>
    /// <param name="path">Path to the source image.</param>
    /// <param name="options">Detection options. Recognition-only fields (TextScore,
    /// ReturnWordBox, ClsThresh, etc.) are ignored on this path.</param>
    /// <returns>Boxes in source-image coordinates, sorted in reading order.</returns>
    public async Task<IReadOnlyList<TextBox>> DetectBoxes(string path, RapidOcrOptions options)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find image to process: '{path}'.", path);
        }

        using (var originSrc = SKBitmap.Decode(path))
        {
            return await DetectBoxes(originSrc, options);
        }
    }

    /// <summary>
    /// Runs the detection stage only and returns the raw text boxes, skipping angle
    /// classification and recognition. See <see cref="DetectBoxes(string, RapidOcrOptions)"/>.
    /// </summary>
    public async Task<IReadOnlyList<TextBox>> DetectBoxes(SKBitmap originSrc, RapidOcrOptions options)
    {
        using var input = PrepareDetectorInput(originSrc, options);
        var textBoxes = await _textDetector.GetTextBoxes(input.Bitmap, input.Scale,
            options.BoxScoreThresh, options.BoxThresh, options.UnClipRatio) ?? [];

        // Map from letterboxed-image space back into the original image's space, the
        // same transform Detect applies to TextBlock.BoxPoints. Boxes own fresh
        // point arrays, so in-place mutation is safe.
        foreach (var box in textBoxes)
        {
            input.MapToOriginal(box.BoxPoints);
        }

        return textBoxes;
    }

    private static DetectorInput PrepareDetectorInput(SKBitmap originSrc, RapidOcrOptions options)
    {
        int outerPadding = Math.Max(0, options.Padding);
        SKBitmap outerPadded = originSrc;
        SKBitmap? ownedOuter = null;
        if (outerPadding > 0)
        {
            ownedOuter = OcrUtils.MakePadding(originSrc, outerPadding);
            outerPadded = ownedOuter;
        }

        // PP-OCR resize_image_within_bounds: bring the input within [MinSideLen, MaxSideLen]
        // before any further processing. Skipped when caller forces legacy behavior with
        // ImgResize > 0 (so existing callers keep their pixel-for-pixel detector input).
        SKBitmap bounded = outerPadded;
        SKBitmap? ownedBounded = null;
        if (options.ImgResize <= 0)
        {
            bounded = OcrUtils.ResizeImageWithinBounds(outerPadded, options.MinSideLen, options.MaxSideLen, out bool boundOwned);
            if (boundOwned)
            {
                ownedBounded = bounded;
            }
        }

        SKBitmap letterboxed = OcrUtils.ApplyVerticalLetterbox(bounded, options.WidthHeightRatio, options.MinHeight, out int letterboxTop);
        SKBitmap? ownedLetterbox = !ReferenceEquals(letterboxed, bounded) ? letterboxed : null;

        ScaleParam scale;
        try
        {
            if (options.ImgResize > 0)
            {
                // Legacy path: explicit max-side cap. Caps at source size for tiny
                // images so 23x36 single-char crops aren't upscaled into giant inputs.
                int originMaxSide = Math.Max(originSrc.Width, originSrc.Height);
                int resize = options.ImgResize > originMaxSide ? originMaxSide : options.ImgResize;
                resize += 2 * outerPadding;
                scale = ScaleParam.GetScaleParam(letterboxed, resize);
            }
            else
            {
                // Python-style: scale short side up to LimitSideLen (default 736),
                // matching rapidocr-python's Det.limit_type="min" config.
                scale = ScaleParam.GetAdaptiveScaleParam(letterboxed, options.LimitSideLen, options.DetMaxPixels);
            }
        }
        catch
        {
            ownedLetterbox?.Dispose();
            ownedBounded?.Dispose();
            ownedOuter?.Dispose();
            throw;
        }

        // Bound ratio = pre-resize size / bounded size, per axis (the two sides are
        // rounded to /32 independently, so they can differ). This is Python rapidocr's
        // ratio_w / ratio_h from resize_image_within_bounds, used to map detector-space
        // coordinates back up into the original image. When ResizeImageWithinBounds was a
        // no-op (typical inputs, or the legacy ImgResize path), bounded == outerPadded so
        // both ratios are exactly 1.
        float boundRatioW = outerPadded.Width / (float)bounded.Width;
        float boundRatioH = outerPadded.Height / (float)bounded.Height;

        return new DetectorInput(letterboxed, scale, outerPadding, letterboxTop,
            boundRatioW, boundRatioH, originSrc.Width, originSrc.Height,
            ownedOuter, ownedBounded, ownedLetterbox);
    }

    private readonly struct DetectorInput : IDisposable
    {
        public readonly SKBitmap Bitmap;
        public readonly ScaleParam Scale;
        private readonly int _outerPadding;
        private readonly int _letterboxTop;
        private readonly float _boundRatioW;
        private readonly float _boundRatioH;
        private readonly int _originWidth;
        private readonly int _originHeight;
        private readonly SKBitmap? _ownedOuter;
        private readonly SKBitmap? _ownedBounded;
        private readonly SKBitmap? _ownedLetterbox;

        public DetectorInput(SKBitmap bitmap, ScaleParam scale, int outerPadding, int letterboxTop,
            float boundRatioW, float boundRatioH, int originWidth, int originHeight,
            SKBitmap? ownedOuter, SKBitmap? ownedBounded, SKBitmap? ownedLetterbox)
        {
            Bitmap = bitmap;
            Scale = scale;
            _outerPadding = outerPadding;
            _letterboxTop = letterboxTop;
            _boundRatioW = boundRatioW;
            _boundRatioH = boundRatioH;
            _originWidth = originWidth;
            _originHeight = originHeight;
            _ownedOuter = ownedOuter;
            _ownedBounded = ownedBounded;
            _ownedLetterbox = ownedLetterbox;
        }

        // Map detector (letterboxed) coordinates back into the original image space,
        // undoing the vertical letterbox, bound-ratio rescale and outer padding. Mirrors
        // Python rapidocr's map_boxes_to_original. Points are mutated in place.
        public void MapToOriginal(SKPointI[] points)
        {
            for (int p = 0; p < points.Length; p++)
            {
                MapPointToOriginal(ref points[p], _outerPadding, _letterboxTop,
                    _boundRatioW, _boundRatioH, _originWidth, _originHeight);
            }
        }

        public void Dispose()
        {
            _ownedLetterbox?.Dispose();
            _ownedBounded?.Dispose();
            _ownedOuter?.Dispose();
        }
    }

    private async Task<OcrResult> ReadBoxes(DetectorInput input, IReadOnlyList<TextBox> textBoxes, RapidOcrOptions options,
        Predicate<TextBlock[]>? read)
    {
        SKBitmap src = input.Bitmap;
        var (returnWordBox, returnSingleCharBox, rotateTall) = (options.ReturnWordBox, options.ReturnSingleCharBox, options.RotateTallCrops);
        var (textScore, clsThresh, clsRotate) = (options.TextScore, options.ClsThresh, options.ClsRotate);

        var sw = ValueStopwatch.StartNew();

        // getPartImages: capture crop bookkeeping when word boxes are requested.
        // Both overloads now dispose partial results internally if a crop throws midway.
        SKBitmap[] partImages;
        CropContext[] cropContexts;
        if (returnWordBox)
        {
            (partImages, cropContexts) = OcrUtils.GetPartImagesWithContext(src, textBoxes, rotateTall, options.CropPadding);
        }
        else
        {
            partImages = OcrUtils.GetPartImages(src, textBoxes, rotateTall, options.CropPadding);
            cropContexts = [];
        }

        // step: angleNet getAngles
        Angle[] angles = await _textClassifier.GetAngles(partImages, options.DoAngle, options.MostAngle, options.ClsPreserveAspectRatio, options.ClsMaxCrops);

        // Rotate partImgs only if the classifier is confident enough (Python <c>cls_thresh</c>).
        // Without this gate, low-confidence flips wrongly invert clean upright text and the
        // recognizer produces garbage like "1997" → "L66" or "This" → "s". With clsRotate
        // off the verdict is still reported but no crop is turned, which leaves the call
        // to the caller: a two-glyph crop carries too little evidence to be turned on its own.
        for (int i = 0; i < partImages.Length; ++i)
        {
            if (angles[i].Index != 1) continue;
            if (angles[i].Score < clsThresh)
            {
                // Below threshold, treat as no-flip for downstream consumers / word-box mapping.
                angles[i].Index = 0;
                continue;
            }
            if (!clsRotate) continue;
            var original = partImages[i];
            partImages[i] = OcrUtils.BitmapRotateClockWise180(original);
            original.Dispose();
        }

        foreach (var textBox in textBoxes)
        {
            input.MapToOriginal(textBox.BoxPoints);
        }

        if (read is not null)
        {
            var unread = Unread(textBoxes, angles);
            if (!read(unread))
            {
                foreach (var bmp in partImages)
                {
                    bmp.Dispose();
                }

                return new OcrResult { TextBlocks = unread, Boxes = textBoxes, StrRes = string.Empty };
            }
        }

        // step: crnnNet getTextLines
        TextLine[] textLines = await _textRecognizer.GetTextLines(partImages);

        foreach (var bmp in partImages)
        {
            bmp.Dispose();
        }

        var textBlocks = new TextBlock[textLines.Length];
        for (int i = 0; i < textLines.Length; ++i)
        {
            var textBox = textBoxes[i];
            var angle = angles[i];
            var textLine = textLines[i];

            WordBox[]? wordResults = null;
            if (returnWordBox)
            {
                wordResults = CalRecBoxes.Build(
                    textLine,
                    cropContexts[i],
                    cls180: clsRotate && angle.Index == 1,
                    returnSingleCharBox: returnSingleCharBox);

                if (wordResults is not null)
                {
                    // Map word polygons back to original space, as the boxes were.
                    var padded = options.CropPadding > 0 && OcrUtils.IsRun(textBox.BoxPoints);
                    for (int w = 0; w < wordResults.Length; w++)
                    {
                        if (padded) OcrUtils.Unpad(wordResults[w].BoxPoints, options.CropPadding);
                        input.MapToOriginal(wordResults[w].BoxPoints);
                    }
                }
            }

            textBlocks[i] = new TextBlock
            {
                BoxPoints = textBox.BoxPoints,
                BoxScore = textBox.Score,
                AngleIndex = angle.Index,
                AngleScore = angle.Score,
                AngleTime = angle.Time,
                Chars = textLine.Chars,
                CharScores = textLine.CharScores,
                WordResults = wordResults,
                CrnnTime = textLine.Time,
                BlockTime = angle.Time + textLine.Time,
                Text = GetText(textLine.Chars)
            };
        }

        // PP-OCR-style filtering: drop blocks with empty recognized text or
        // average char score below `textScore`.
        var filteredBlocks = new List<TextBlock>(textBlocks.Length);
        foreach (var block in textBlocks)
        {
            // A crop left upside down for the caller to turn reads as garbage or as nothing, but
            // its verdict is what the caller turns the page by, so it is kept whatever it reads.
            if (!clsRotate && block.AngleIndex == 1)
            {
                filteredBlocks.Add(block);
                continue;
            }

            if (block.Chars is null || block.Chars.Length == 0)
            {
                continue;
            }

            string text = block.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (textScore > 0 && block.CharScores is { Length: > 0 })
            {
                float sum = 0;
                for (int s = 0; s < block.CharScores.Length; s++)
                {
                    sum += block.CharScores[s];
                }

                float avg = sum / block.CharScores.Length;
                if (avg < textScore)
                {
                    continue;
                }
            }

            filteredBlocks.Add(block);
        }

        textBlocks = filteredBlocks.ToArray();

        var fullDetectTime = sw.ElapsedMilliseconds;

        var strRes = new StringBuilder();
        foreach (var x in textBlocks)
        {
            strRes.AppendLine(x.Text);
        }

        return new OcrResult
        {
            TextBlocks = textBlocks,
            Boxes = textBoxes,
            DetectTime = (float)fullDetectTime,
            StrRes = strRes.ToString()
        };
    }

    private static TextBlock[] Unread(IReadOnlyList<TextBox> textBoxes, Angle[] angles)
    {
        var blocks = new TextBlock[textBoxes.Count];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new TextBlock
            {
                BoxPoints = textBoxes[i].BoxPoints,
                BoxScore = textBoxes[i].Score,
                AngleIndex = angles[i].Index,
                AngleScore = angles[i].Score,
                Text = string.Empty,
                Chars = null,
                CharScores = null,
            };
        }

        return blocks;
    }

    /// <summary>
    /// Map a single detector-space point back into the original image's pixel space,
    /// undoing the preprocessing transforms in reverse order. Mirrors Python rapidocr's
    /// <c>map_boxes_to_original</c> (utils/process_img.py): remove the vertical letterbox,
    /// rescale by the <see cref="OcrUtils.ResizeImageWithinBounds"/> bound ratio, then
    /// remove the outer padding. The detector returns coordinates in letterboxed (bounded)
    /// space, so without the rescale step boxes for images outside [MinSideLen, MaxSideLen]
    /// come back in the wrong scale.
    /// </summary>
    /// <remarks>
    /// Left-side letterbox padding is always 0 (only vertical letterboxing is applied), so
    /// only Y is offset by <paramref name="letterboxTop"/>. The mapped point is finally clamped
    /// to <c>[0, originWidth] x [0, originHeight]</c>, matching the Python reference.
    /// </remarks>
    private static void MapPointToOriginal(ref SKPointI point, int outerPadding, int letterboxTop,
        float boundRatioW, float boundRatioH, int originWidth, int originHeight)
    {
        // letterboxed space -> bounded space: remove the vertical letterbox (left pad is 0).
        float x = point.X;
        float y = point.Y - letterboxTop;

        // bounded space -> outer-padded space: scale back up by the bound ratio.
        x *= boundRatioW;
        y *= boundRatioH;

        // outer-padded space -> original space: remove the outer padding.
        x -= outerPadding;
        y -= outerPadding;

        // Clamp to the original image bounds (Python map_boxes_to_original).
        point.X = Math.Clamp((int)MathF.Round(x), 0, originWidth);
        point.Y = Math.Clamp((int)MathF.Round(y), 0, originHeight);
    }

    private static string GetText(string[]? chars)
    {
        if (chars is null || chars.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(chars);
    }

    public void Dispose()
    {
        _textClassifier.Dispose();
        _textRecognizer.Dispose();
        _textDetector.Dispose();
    }
}

