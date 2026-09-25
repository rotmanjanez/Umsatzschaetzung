using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Extract;

public class GapsTests
{
    const int Pitch = 50;
    const int Height = 30;

    static readonly Dictionary<Field, (int X, int W)> Columns = new()
    {
        [Field.Name] = (100, 400),
        [Field.Quantity] = (600, 60),
        [Field.UnitPrice] = (800, 100),
        [Field.LineNet] = (1000, 100),
    };

    static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    static Box Printed(int row, Field field) => new(Columns[field].X, 100 + row * Pitch, Columns[field].W, Height);

    // Three rows of 3 × 1,50 = 4,50, less the cells the detector missed.
    static List<OcrLine> Table(params (int Row, Field Field)[] missing)
    {
        var lines = new List<OcrLine>();
        for (var row = 0; row < 3; row++)
        {
            var line = new OcrLine { Parsed = new InvoiceLine { Quantity = 3000, UnitPrice = 1_500_000, LineNet = 450, PriceBaseQty = 1000 } };
            foreach (var (field, text) in new[] { (Field.Name, "Semmel"), (Field.Quantity, "3"), (Field.UnitPrice, "1,50"), (Field.LineNet, "4,50") })
                if (!missing.Contains((row, field))) line.Cells[field] = new OcrWord { Text = text, Box = Printed(row, field) };
            if (missing.Contains((row, Field.LineNet))) line.Parsed.LineNet = 0;
            lines.Add(line);
        }
        return lines;
    }

    static bool Covers(Box outer, Box inner) =>
        outer.X <= inner.X && outer.Y <= inner.Y && outer.X + outer.W >= inner.X + inner.W && outer.Y + outer.H >= inner.Y + inner.H;

    [Fact]
    public void TheColumnAboveAndBelowAndTheRowBesidePlaceTheMissingCell()
    {
        var box = Gaps.Estimate(Table((1, Field.UnitPrice)), 1, Field.UnitPrice)!;
        Assert.True(Covers(box, Printed(1, Field.UnitPrice)));
        Assert.True(box.X >= Columns[Field.Quantity].X + Columns[Field.Quantity].W);
        Assert.True(box.X + box.W <= Columns[Field.LineNet].X);
        Assert.True(box.Y >= Printed(0, Field.UnitPrice).Y + Height);
        Assert.True(box.Y + box.H <= Printed(2, Field.UnitPrice).Y);
    }

    [Fact]
    public void OneCellAboveAndBothBesideAreEnough() =>
        Assert.True(Covers(Gaps.Estimate(Table((2, Field.UnitPrice)), 2, Field.UnitPrice)!, Printed(2, Field.UnitPrice)));

    // The last net of the page has only the cell above it and the price beside it.
    [Fact]
    public void TwoSurroundingCellsAreNotEnough() =>
        Assert.Null(Gaps.Estimate(Table((2, Field.LineNet)), 2, Field.LineNet));

    [Fact]
    public void ACellMissingAboveAsWellLeavesTooFewNeighbours() =>
        Assert.Null(Gaps.Estimate(Table((0, Field.LineNet), (1, Field.LineNet)), 1, Field.LineNet));

    [Fact]
    public void ARowThatDoesNotSitBetweenItsNeighboursIsNoGap()
    {
        var lines = Table((1, Field.UnitPrice));
        foreach (var cell in lines[1].Cells.Values) cell.Box = cell.Box with { Y = cell.Box.Y + 2 * Pitch };
        Assert.Null(Gaps.Estimate(lines, 1, Field.UnitPrice));
    }

    [Fact]
    public void AWordWhereTheCellShouldBeIsNoGap()
    {
        var lines = Table((1, Field.UnitPrice));
        lines[1].Cells[Field.Vat] = new OcrWord { Text = "10%", Box = Printed(1, Field.UnitPrice) };
        Assert.Null(Gaps.Estimate(lines, 1, Field.UnitPrice));
    }

    static async Task<(OcrPage Page, List<Box> Asked)> Filled(List<OcrLine> lines, params string[] reads)
    {
        var page = new OcrPage { Lines = lines };
        var asked = new List<Box>();
        await Gaps.Fill([page], (_, regions, _) =>
        {
            asked.AddRange(regions);
            return Task.FromResult(regions.Select((r, i) => i < reads.Length && reads[i] != ""
                ? new List<OcrWord> { new() { Text = reads[i], Box = r with { X = r.X + 10, W = r.W - 20 } } }
                : []).ToList());
        }, Ct);
        return (page, asked);
    }

    [Fact]
    public async Task ALostNetReadAgainIsKeptWhereTheRowAddsUp()
    {
        var (page, asked) = await Filled(Table((1, Field.LineNet)), "4,50");
        Assert.Single(asked);
        Assert.Equal(450, page.Lines[1].Parsed.LineNet);
        Assert.Equal("4,50", page.Lines[1].Cells[Field.LineNet].Text);
        Assert.True(Covers(asked[0], page.Lines[1].Cells[Field.LineNet].Box));
    }

    [Fact]
    public async Task ANetReadAgainThatDoesNotAddUpIsDropped()
    {
        var (page, _) = await Filled(Table((1, Field.LineNet)), "4,60");
        Assert.Equal(0, page.Lines[1].Parsed.LineNet);
        Assert.False(page.Lines[1].Cells.ContainsKey(Field.LineNet));
    }

    // Restore already put the quantity back from price and net; the read confirms it.
    [Fact]
    public async Task AQuantityReadAgainConfirmsTheRestoredOne()
    {
        var (page, _) = await Filled(Table((1, Field.Quantity)), "3");
        Assert.Equal(3000, page.Lines[1].Parsed.Quantity);
        Assert.Equal("3", page.Lines[1].Cells[Field.Quantity].Text);
    }

    [Fact]
    public async Task AQuantityReadAgainThatContradictsTheRowIsDropped()
    {
        var (page, _) = await Filled(Table((1, Field.Quantity)), "8");
        Assert.Equal(3000, page.Lines[1].Parsed.Quantity);
        Assert.False(page.Lines[1].Cells.ContainsKey(Field.Quantity));
    }

    [Fact]
    public async Task NothingReadAgainLeavesTheRowAsItWas()
    {
        var (page, _) = await Filled(Table((1, Field.LineNet)), "");
        Assert.Equal(0, page.Lines[1].Parsed.LineNet);
        Assert.False(page.Lines[1].Cells.ContainsKey(Field.LineNet));
    }

    [Fact]
    public async Task APageWithoutAPlaceableGapIsNotReadAgain()
    {
        var (_, asked) = await Filled(Table((2, Field.LineNet)), "4,50");
        Assert.Empty(asked);
    }
}
