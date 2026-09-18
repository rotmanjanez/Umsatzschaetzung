namespace Umsatzschätzung.Richtsatz;

public sealed record Sammlung(int Year, List<Klasse> Klassen, List<Synonym> Synonyme, List<Pauschbetrag> Pauschbeträge);

public sealed record Klasse(string Name, string? Zusatz, List<string> Kennzahlen, List<Staffel> Staffeln, string? Bemerkung, int Seite);

// Von and Bis are wirtschaftlicher Umsatz in Cent, open at a null end. A Klasse that
// the Sammlung does not staffel carries one Staffel with no Stufe and no bounds.
public sealed record Staffel(string? Stufe, long? Von, long? Bis, Sätze Sätze);

public sealed record Sätze(Satz? Aufschlag, Satz? RohgewinnI, Satz? RohgewinnII, Satz? Halbreingewinn, Satz? Reingewinn);

// Whole v.H. of the wirtschaftlicher Umsatz. Handwerk rows print Rohgewinn I as a bare
// Mittelsatz, so the Rahmensatz is the optional part.
public sealed record Satz(int? Von, int? Bis, int Mittel);

public sealed record Synonym(string Begriff, string Klasse);

// The Sammlung sets the Pauschbeträge per period, which is the calendar year except where
// a change of the tax rate splits it into half years.
public sealed record Pauschbetrag(DateOnly Von, DateOnly Bis, string Gewerbezweig, long Ermäßigt, long Voll, long Gesamt);

// Was der Speicher über eine Sammlung weiß, ohne sie zu lesen. Quelle ist der Dateiname
// der importierten PDF, bei den mitgelieferten Sammlungen leer.
public sealed record SammlungInfo(int Year, int Klassen, string Quelle, DateTimeOffset ImportedAt)
{
    public bool Mitgeliefert => Quelle == "";
}
