using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

// A stored invoice taken into the parts must read as if the case were taken apart anew, or the next save records a change nobody made.
public class CasePartsTests
{
    [Fact]
    public void TakingAStoredInvoiceMatchesTheCaseTakenApart()
    {
        var c = Vorlage.Load();
        var parts = CaseParts.Of(c);
        var inv = Json.Copy(c.Invoices[0]);
        inv.Number = "Grüße-1";
        c.Invoices[0] = inv;
        c.Mappings["neu"] = new ArticleMapping { Id = "neu", Observed = "Weißbier" };
        c.MappedStore = null;

        CaseParts.Take(parts, c, inv, true);

        Assert.Equal(CaseParts.Of(c), parts);
    }

    [Fact]
    public void TakingANewInvoiceAddsItsPart()
    {
        var c = Vorlage.Load();
        var parts = CaseParts.Of(c);
        var inv = new Invoice { Id = "neu", Number = "1" };
        c.Invoices.Add(inv);
        c.MappedStore = "store";

        CaseParts.Take(parts, c, inv, false);

        Assert.Equal(CaseParts.Of(c), parts);
    }
}
