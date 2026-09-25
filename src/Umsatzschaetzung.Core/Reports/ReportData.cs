using System.ComponentModel;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Reports;

public sealed record EstimateGroup(List<EstimateRow> Rows, long Cost, long RevenueNet);

public sealed record EstimateGroups(EstimateGroup Products, EstimateGroup Leftover, EstimateGroup Lines);

public sealed record CalculationGroup(Sparte? Sparte, List<ProductRow> Rows, long Cost, long Markup);

public sealed record SupplierSum(string Name, int Invoices, long Net, long Gross);

public sealed record TemplateInfo(string Id, string Name);

public sealed record Richtsatzbasis(int Jahr, string Quelle, DateTimeOffset ImportedAt, Klasse? Klasse, List<Pauschbetrag> Pauschbeträge);

// Alles, was eine Vorlage sieht. Die Seite zum Vorlagenmodell entsteht aus diesem Typ.
public sealed class ReportData
{
    [Description("Die Prüfung, wie sie gespeichert ist, mit allen Rechnungen und Zeilen.")]
    public required Case Case { get; init; }
    [Description("Die Regeln, mit denen gerechnet wurde; Rezepturen so, wie diese Prüfung sie anpasst.")]
    public required RuleSet Rules { get; init; }
    [Description("Das Ergebnis der Kalkulation.")]
    public required Model.Report Report { get; init; }
    [Description("Rechnungen mit den Zeilen, die in den Wareneinsatz eingehen, nach Datum und Nummer.")]
    public required List<Included> Included { get; init; }
    [Description("Umsatz vor und nach Prüfung je Steuersatz; die letzte Zeile (total) ist die Summe.")]
    public required List<VatRow> Revenue { get; init; }
    [Description("Anzahl der Rechnungen in included.")]
    public int InvoiceCount { get; init; }
    [Description("Wareneinsatz samt Bestandsveränderung, in Cent.")]
    public long IncludedNet { get; init; }
    [Description("Einkauf ohne Zuordnung oder ohne Umsatz, in Cent.")]
    public long Excluded { get; init; }
    [Description("Über den Aufschlagsatz geschätzter Umsatz, getrennt nach Herkunft.")]
    public required EstimateGroups Estimated { get; init; }
    [Description("Kalkulation je Produkt: eine Gruppe je Sparte, außerhalb der Gastronomie eine Gruppe ohne Sparte.")]
    public required List<CalculationGroup> Calculation { get; init; }
    [Description("Rahmensatz der Richtsatzsammlung für die Gewerbekennzahl, falls eindeutig.")]
    public Rahmen? Rahmen { get; init; }
    [Description("Lage des kalkulierten Aufschlagsatzes zum Rahmen.")]
    public Rahmenlage? Lage { get; init; }
    [Description("Ob irgendeine Zutat eine Ertragsregel trägt.")]
    public bool AnyYields { get; init; }
    [Description("Die Gewerbekennzahl der Prüfung aus den Regeln.")]
    public Gewerbezweig? Gewerbe { get; init; }
    [Description("Die verwendete Richtsatzsammlung mit der ganzen Gewerbeklasse der Prüfung und den Pauschbeträgen.")]
    public Richtsatzbasis? Richtsatz { get; init; }
    [Description("Je Lieferant Anzahl und Summe aller Rechnungen der Prüfung, in Cent.")]
    public required List<SupplierSum> Suppliers { get; init; }
    [Description("Kopf- und Fußzeilen, die ins PDF gestempelt werden.")]
    public required PageMarks Marks { get; init; }
    [Description("Die Vorlage, aus der dieser Bericht entsteht.")]
    public required TemplateInfo Template { get; init; }
    [Description("Tage im Prüfungszeitraum, beide Enden eingeschlossen.")]
    public int PeriodDays { get; init; }
    [Description("Zeitpunkt, zu dem der Bericht erzeugt wurde.")]
    public DateTimeOffset GeneratedAt { get; init; }
    [Description("Version des Programms.")]
    public string AppVersion { get; init; } = "";
}
