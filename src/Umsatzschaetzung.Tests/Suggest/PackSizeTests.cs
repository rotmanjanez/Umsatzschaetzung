using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public class PackSizeTests
{
    [Theory]
    [InlineData("Kiste Pils 20 x 0,5 l", 20, 500, Unit.Ml)]
    [InlineData("Mehl Type 550 25 kg Sack", 1, 25000, Unit.G)]
    [InlineData("Weisswein trocken 0,75 l 12% vol", 1, 750, Unit.Ml)]
    [InlineData("Doppelkorn 38 % vol, Flasche 0,7 l", 1, 700, Unit.Ml)]
    [InlineData("Cola PET 24x0,33", 24, 330, null)]
    [InlineData("Servietten 3-lagig 250 Stk", 250, 1000, Unit.Piece)]
    [InlineData("Trg 6er Limo 0,33 l", 6, 330, Unit.Ml)]
    [InlineData("Fassbier Pils, Keg 50 l", 1, 50000, Unit.Ml)]
    [InlineData("Saft 12 × 1 L", 12, 1000, Unit.Ml)]
    [InlineData("Sahne 10*200 ml", 10, 200, Unit.Ml)]
    [InlineData("Espresso 6 x 1 kg", 6, 1000, Unit.G)]
    [InlineData("Gewürz 250 g", 1, 250, Unit.G)]
    [InlineData("Likör 4 cl", 1, 40, Unit.Ml)]
    public void TheSizeOfOneContainerIsRead(string text, long count, long size, Unit? unit) =>
        Assert.Equal(new Pack(count, size, unit), PackSize.Read(text));

    [Theory]
    [InlineData("Rinderhuefte, Abrechnung je kg")]
    [InlineData("Mehl Type 550")]
    [InlineData("Pfand Leergut Kiste")]
    [InlineData("Fass 0 l")]
    [InlineData("Wasser 2500 x 1 l")]
    [InlineData("Tank 250 m3")]
    [InlineData("")]
    public void NothingIsReadWhereThereIsNoPlausibleSize(string text) => Assert.Null(PackSize.Read(text));

    [Theory]
    [InlineData("Kiste Pils 20 x 0,5 l", "Pils")]
    [InlineData("Mehl Type 550 25 kg Sack", "Mehl Type 550")]
    [InlineData("Trg 6er Limo 0,33 l", "Trg Limo")]
    [InlineData("Servietten 3-lagig 250 Stk", "Servietten 3 lagig")]
    [InlineData("Cola PET 24x0,33", "Cola PET")]
    [InlineData("Weizen", "Weizen")]
    [InlineData("", "")]
    public void StrippingTakesThePackagingAndLeavesTheWare(string text, string ware) =>
        Assert.Equal(ware, PackSize.Strip(text));

    [Theory]
    [InlineData("Korn 0,71", "XBO", "Korn 0,7 l")]
    [InlineData("Korn 0,7I", "XBO", "Korn 0,7 l")]
    [InlineData("Korn 0,71", "LTR", "Korn 0,71")]
    [InlineData("Korn 0,7 l", "XBO", "Korn 0,7 l")]
    [InlineData("Kiste 20 x 0,5 l Nr 0,51", "XCS", "Kiste 20 x 0,5 l Nr 0,51")]
    [InlineData("Korn 38", "XBO", "Korn 38")]
    public void ALitreReadAsAOneIsTakenBackOnlyWhereAContainerHasNoSize(string text, string unitCode, string recovered) =>
        Assert.Equal(recovered, PackSize.Recover(text, unitCode));
}
