using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class FormatTests
{
    [Fact]
    public void CentsAreWrittenGerman() => Assert.Equal("1.690,00 €", Format.Cents(169000));
}
