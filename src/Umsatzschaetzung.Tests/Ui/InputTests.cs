using Umsatzschaetzung.App.Ui;

namespace Umsatzschaetzung.Tests.Ui;

public sealed class InputTests
{
    [Theory]
    [InlineData("1200", 1200L)]
    [InlineData("1.200", 1200L)]
    [InlineData("12.500.000", 12500000L)]
    [InlineData("-1.000", -1000L)]
    [InlineData(" 7 ", 7L)]
    [InlineData("0.5", null)]
    [InlineData("1.20", null)]
    [InlineData("1,5", null)]
    [InlineData("1.2345", null)]
    public void AWholeNumberTakesADotOnlyAsAThousandsGroup(string text, long? value) => Assert.Equal(value, Input.Int(text));

    [Theory]
    [InlineData("12,50", 1250L)]
    [InlineData("1.234,56", 123456L)]
    [InlineData("12.50", null)]
    [InlineData("-", null)]
    [InlineData(",", null)]
    [InlineData("1,239", null)]
    [InlineData(",5", 50L)]
    public void AnAmountTakesTheCommaAsItsDecimalMark(string text, long? cents) => Assert.Equal(cents, Input.Cents(text));
}
