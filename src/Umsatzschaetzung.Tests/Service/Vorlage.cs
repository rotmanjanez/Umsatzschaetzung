using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Service;

// The bar case of data/case.sql: three lines from one supplier, five products, stock at both ends.
static class Vorlage
{
    public const string Id = "case.bar.2024";
    public const string InvoiceId = "inv.bar.1";

    public static Case Load()
    {
        using var dir = new TempDir();
        var store = new CaseStore(dir.Path);
        store.Save(new Case
        {
            Id = Id,
            Label = Id,
            PeriodFrom = new DateOnly(2024, 1, 1),
            PeriodTo = new DateOnly(2024, 12, 31),
            Taxpayer = new Taxpayer { Name = "-", TaxNumber = "-", PabNumber = "-" },
            CreatedAt = Clock.Now(),
            UpdatedAt = Clock.Now(),
        });
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dir.Path, Id + ".db"),
            Pooling = false,
        }.ToString()))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = File.ReadAllText(TestData.File("case.sql"));
            cmd.ExecuteNonQuery();
        }
        return store.Load(Id);
    }

    // Eine Bezeichnung gibt es nur einmal: ohne eigene bekommt jede Prüfung eine neue.
    public static Case Blank(string? label = null, string gewerbe = "") => new()
    {
        Label = label ?? "Prüfung " + Guid.NewGuid(),
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Taxpayer = new Taxpayer { Name = "Muster", TaxNumber = "123/4567", PabNumber = "89", Gewerbe = gewerbe },
    };
}
