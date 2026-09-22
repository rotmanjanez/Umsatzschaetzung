using RapidOcrNet;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class RapidOcrOptionsTests
{
    [Fact]
    public void TheDefaultPresetIsTunedForTheBundledV5Detector()
    {
        var o = RapidOcrOptions.Default;
        Assert.Equal((50, 1024, 736, 2000, 30), (o.Padding, o.ImgResize, o.LimitSideLen, o.MaxSideLen, o.MinSideLen));
        Assert.Equal((-1f, 30, 0.5f, 0.9f), (o.WidthHeightRatio, o.MinHeight, o.TextScore, o.ClsThresh));
        Assert.Equal((0.5f, 0.3f, 1.6f), (o.BoxScoreThresh, o.BoxThresh, o.UnClipRatio));
        Assert.Equal((true, false, false, false, true), (o.DoAngle, o.MostAngle, o.ReturnWordBox, o.ReturnSingleCharBox, o.ClsPreserveAspectRatio));
    }

    [Fact]
    public void ThePythonPresetDropsTheBorderAndScalesAdaptively()
    {
        var o = RapidOcrOptions.PythonCompat;
        Assert.Equal((0, 0, 736, 2000, 30), (o.Padding, o.ImgResize, o.LimitSideLen, o.MaxSideLen, o.MinSideLen));
        Assert.Equal((8f, 30, 0.5f, 0.9f), (o.WidthHeightRatio, o.MinHeight, o.TextScore, o.ClsThresh));
        Assert.Same(RapidOcrOptions.PythonCompat, RapidOcrOptions.PPOCRv6);
        Assert.Equal(RapidOcrOptions.Default with { Padding = 0, ImgResize = 0, WidthHeightRatio = 8f }, o);
    }

    [Fact]
    public void BothPresetsKeepUpstreamOrientationHandlingAndLeaveStacksWhole()
    {
        foreach (var o in new[] { RapidOcrOptions.Default, RapidOcrOptions.PythonCompat })
        {
            Assert.True(o.ClsRotate);
            Assert.True(o.RotateTallCrops);
            Assert.False(o.SplitStackedCrops);
        }
    }

    [Fact]
    public void WithChangesOnlyTheCopy()
    {
        var app = RapidOcrOptions.PPOCRv6 with { ClsRotate = false, RotateTallCrops = false, SplitStackedCrops = true };

        Assert.Equal((false, false, true), (app.ClsRotate, app.RotateTallCrops, app.SplitStackedCrops));
        Assert.Equal((true, true, false), (RapidOcrOptions.PPOCRv6.ClsRotate, RapidOcrOptions.PPOCRv6.RotateTallCrops, RapidOcrOptions.PPOCRv6.SplitStackedCrops));
        Assert.NotEqual(RapidOcrOptions.PPOCRv6, app);
        Assert.Equal(RapidOcrOptions.PPOCRv6, app with { ClsRotate = true, RotateTallCrops = true, SplitStackedCrops = false });
    }
}
