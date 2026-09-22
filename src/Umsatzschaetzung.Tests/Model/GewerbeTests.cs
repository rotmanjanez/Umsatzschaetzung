using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class GewerbeTests
{
    [Theory]
    [InlineData("56101.0", true)]
    [InlineData("561", true)]
    [InlineData("5", true)]
    [InlineData("56101", true)]
    [InlineData("", false)]
    [InlineData("561010", false)]
    [InlineData("56101.", false)]
    [InlineData("56101,0", false)]
    [InlineData("abc", false)]
    public void AKennzahlIsUpToFiveDigitsWithAnOptionalDecimal(string s, bool valid) => Assert.Equal(valid, Gewerbe.Kennzahl(s));
}
