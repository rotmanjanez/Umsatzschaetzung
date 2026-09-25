using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Richtsatz;

public enum Rahmenlage
{
    [JsonStringEnumMemberName("unter")] Unter,
    [JsonStringEnumMemberName("im")] Im,
    [JsonStringEnumMemberName("über")] Über,
}

// Der Rahmensatz, an dem sich ein kalkulierter Rohgewinnaufschlagsatz messen lässt:
// der Aufschlag der Gewerbeklasse aus der Sammlung des Prüfungsjahres, in der Staffel,
// in die der kalkulierte Umsatz fällt.
public sealed record Rahmen(int Jahr, string Klasse, string? Stufe, Satz Aufschlag)
{
    // Ein Satz ohne Rahmen nennt nur den Mittelsatz; dann ist er selbst die Grenze.
    public int Von => Aufschlag.Von ?? Aufschlag.Mittel;
    public int Bis => Aufschlag.Bis ?? Aufschlag.Mittel;

    // Der Satz kommt in Basispunkten, die Sammlung druckt volle v.H.
    public Rahmenlage Lage(long markup) =>
        markup < Von * 100L ? Rahmenlage.Unter : markup > Bis * 100L ? Rahmenlage.Über : Rahmenlage.Im;
}

public static class Vergleich
{
    // Die Kennzahl der Prüfung darf ein Präfix sein ("561"); trifft sie mehr als eine
    // Gewerbeklasse, gibt es keinen eindeutigen Rahmen und damit keinen Vergleich.
    public static Rahmen? Aufschlag(Sammlung? s, string? kennzahl, long umsatz)
    {
        if (s is null || Klasse(s, kennzahl) is not { } hit) return null;
        foreach (var st in hit.Staffeln)
        {
            if (st.Von is { } von && umsatz <= von) continue;
            if (st.Bis is { } bis && umsatz > bis) continue;
            if (st.Sätze.Aufschlag is { } satz) return new Rahmen(s.Year, hit.Name, st.Stufe, satz);
        }
        return null;
    }

    public static Klasse? Klasse(Sammlung? s, string? kennzahl)
    {
        if (s is null || string.IsNullOrEmpty(kennzahl)) return null;
        Klasse? hit = null;
        foreach (var k in s.Klassen)
        {
            if (!k.Kennzahlen.Exists(z => z == kennzahl || z.StartsWith(kennzahl, StringComparison.Ordinal))) continue;
            if (hit is not null && hit != k) return null;
            hit = k;
        }
        return hit;
    }
}
