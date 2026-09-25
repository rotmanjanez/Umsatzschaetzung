using System.Text.RegularExpressions;

namespace Umsatzschaetzung.Richtsatz;

public static partial class Richtsätze
{
    public static Sammlung Read(List<Sheet> sheets)
    {
        var klassen = new List<Klasse>();
        var synonyme = new List<Synonym>();
        var pauschbeträge = new List<Pauschbetrag>();

        foreach (var half in sheets.SelectMany(Grid.Halves))
        {
            if (Klassen(half, klassen)) continue;
            if (Synonyme(half, synonyme)) continue;
            Pauschbeträge(half, pauschbeträge);
        }

        if (klassen.Count == 0) throw new InvalidDataException("richtsatzsammlung: keine Gewerbeklassen gefunden");
        return new Sammlung(Jahr(sheets), klassen, synonyme, pauschbeträge);
    }

    static int Jahr(List<Sheet> sheets)
    {
        foreach (var sheet in sheets)
            if (Kalenderjahr().Match(Grid.Block(sheet.Words)) is { Success: true } match)
                return int.Parse(match.Groups[1].Value);
        throw new InvalidDataException("richtsatzsammlung: kein Kalenderjahr gefunden");
    }

    // Richtsätze: the columns are numbered 1 to 8 right below the header, and the border
    // between them is what says where a cell ends - a turnover band prints its amount
    // right-aligned far enough into column 1 to overhang the next one.
    static bool Klassen(Half half, List<Klasse> into)
    {
        var lines = Grid.Lines(half.Words);
        var head = lines.FirstOrDefault(l => l.Count == 8 && Grid.Text(l) == "1 2 3 4 5 6 7 8");
        if (head is null) return false;

        var body = head[0].Baseline;
        var centres = head.Select(w => w.Xc).ToList();
        // The header above the numbered row merges cells and, on the last sheet, does not
        // even share the column widths of the body, so only the body's borders count.
        var borders = Grid.Long(Grid.Verticals(half.Rules.Where(r => r.Bottom > body)), 0.1);
        var cols = Grid.Around(borders, centres,
                       half.Words.Min(w => w.X0) - 2, half.Words.Max(w => w.X1) + 2)
                   ?? throw new InvalidDataException($"richtsatz: Seite {half.Sheet.Number} ohne Spaltenraster");

        // The border breaks give the rows of the table; a Gewerbeklasse spans as many of
        // them as its turnover bands need, and begins where one of them names it.
        var edges = Grid.Edges(half.Rules, body);
        if (edges.Count < 2) return false;
        var fine = Cells(half.Words, cols, edges);

        // What narrows a Gewerbeklasse is printed below its name in a smaller type, and that
        // is what tells the two apart - even where they share a line.
        var gross = half.Words.Where(w => w.Baseline > body && Grid.Slot(cols, w.Xc) == 0)
            .GroupBy(w => Math.Round(w.Size, 1)).MaxBy(g => g.Count())?.Key ?? 0;

        var rows = new List<double>();
        for (var r = 0; r + 1 < edges.Count; r++)
            if (Spalte.Eins(fine[r, 0], gross).Namen.Count > 0) rows.Add(edges[r]);
        if (rows.Count == 0) return false;
        rows.Add(edges[^1]);

        var cells = Cells(half.Words, cols, rows);
        for (var r = 0; r + 1 < rows.Count; r++)
        {
            var klasse = Zeile(cells, r, gross, half.Sheet.Number);
            if (klasse is not null) into.Add(klasse);
        }
        return true;
    }

    static Klasse? Zeile(List<Word>[,] cells, int row, double gross, int seite)
    {
        var spalte = Spalte.Eins(cells[row, 0], gross);
        var kennzahlen = Grid.Lines(cells[row, 1]).Select(Grid.Text).Where(t => Kennzahl().IsMatch(t)).ToList();

        var staffeln = new List<Staffel>();
        if (spalte.Stufen.Count == 0)
            staffeln.Add(new Staffel(null, null, null, Spalten(cells, row, double.MinValue, double.MaxValue)));
        else
            for (var i = 0; i < spalte.Stufen.Count; i++)
            {
                var stufe = spalte.Stufen[i];
                var bis = i + 1 < spalte.Stufen.Count ? spalte.Stufen[i + 1].Ab : double.MaxValue;
                staffeln.Add(new Staffel(stufe.Name, stufe.Von, stufe.Bis, Spalten(cells, row, stufe.Ab, bis)));
            }

        // A row that only names a group of Gewerbeklassen carries neither a Gewerbekennzahl
        // nor a rate; the classes it groups follow it as rows of their own.
        if (spalte.Namen.Count == 0) return null;
        if (kennzahlen.Count == 0 && staffeln.All(s => Leer(s.Sätze))) return null;

        // Where a Gewerbeklasse has no Richtsätze of its own, the remark that says which one
        // to use instead is set across the columns they would occupy.
        var bemerkung = staffeln.All(s => Leer(s.Sätze))
            ? Bemerkung([.. Enumerable.Range(2, 6).SelectMany(c => cells[row, c])])
            : Bemerkung(cells[row, 7]);

        return new Klasse(
            Grid.Join(spalte.Namen),
            spalte.Zusätze.Count == 0 ? null : Grid.Join(spalte.Zusätze).Trim('(', ')'),
            kennzahlen,
            staffeln,
            bemerkung,
            seite);
    }

    static Sätze Spalten(List<Word>[,] cells, int row, double from, double to) =>
        new(Zelle(cells[row, 2], from, to), Zelle(cells[row, 3], from, to), Zelle(cells[row, 4], from, to),
            Zelle(cells[row, 5], from, to), Zelle(cells[row, 6], from, to));

    static bool Leer(Sätze sätze) =>
        sätze is { Aufschlag: null, RohgewinnI: null, RohgewinnII: null, Halbreingewinn: null, Reingewinn: null };

    // A cell prints its Rahmensatz over its Mittelsatz, and a Mittelsatz on its own where
    // the Sammlung gives no range.
    static Satz? Zelle(List<Word> cell, double from, double to)
    {
        var words = cell.Where(w => w.Baseline >= from && w.Baseline < to).ToList();
        var numbers = Zahl().Matches(Grid.Block(words)).Select(m => int.Parse(m.Value)).ToList();
        return numbers.Count switch
        {
            0 => null,
            1 => new Satz(null, null, numbers[0]),
            3 => new Satz(numbers[0], numbers[1], numbers[2]),
            _ => throw new InvalidDataException($"richtsatz: Zelle mit {numbers.Count} Sätzen: '{Grid.Block(words)}'"),
        };
    }

    static string? Bemerkung(List<Word> cell)
    {
        var text = Grid.Block(cell).Trim();
        return text.Length == 0 ? null : text;
    }

    static bool Synonyme(Half half, List<Synonym> into)
    {
        var lines = Grid.Lines(half.Words);
        var head = lines.FirstOrDefault(l => Grid.Text(l).StartsWith("Die Gewerbeklasse"));
        if (head is null) return false;

        // Some years draw no right hand border on the synonym table, and the page closes it.
        var cols = Grid.Long(Grid.Verticals(half.Rules), 0.5);
        if (cols.Count is not (2 or 3)) return false;
        if (cols.Count == 2) cols.Add(half.Words.Max(w => w.X1) + 1);

        string? begriff = null;
        var klasse = new List<string>();
        // The list names every Gewerbeklasse itself too, pointing nowhere; only an entry that
        // points somewhere is a synonym of one.
        void Flush()
        {
            if (begriff is not null && klasse.Count > 0) into.Add(new Synonym(begriff, Grid.Join(klasse)));
            klasse.Clear();
        }

        foreach (var line in lines.Where(l => l[0].Baseline > head[0].Baseline))
        {
            var left = line.Where(w => Grid.Slot(cols, w.Xc) == 0).ToList();
            var right = line.Where(w => Grid.Slot(cols, w.Xc) == 1).ToList();
            if (left.Count > 0)
            {
                Flush();
                begriff = Grid.Text(left);
            }
            if (right.Count > 0) klasse.Add(Grid.Text(right));
        }
        Flush();
        return true;
    }

    static void Pauschbeträge(Half half, List<Pauschbetrag> into)
    {
        var cols = Grid.Long(Grid.Verticals(half.Rules), 0.5);
        if (cols.Count != 5) return;

        (DateOnly Von, DateOnly Bis)? zeitraum = null;
        string? gruppe = null;

        foreach (var line in Grid.Lines(half.Words))
        {
            var text = Grid.Text(line);
            if (Zeitraum().Match(text) is { Success: true } gilt)
            {
                var jahr = int.Parse(gilt.Groups[5].Value);
                zeitraum = (Tag(gilt.Groups[1].Value, gilt.Groups[2].Value, jahr), Tag(gilt.Groups[3].Value, gilt.Groups[4].Value, jahr));
                gruppe = null;
                continue;
            }
            // Only where a change of the tax rate splits the year does the Sammlung print the
            // period over the table; otherwise its heading names the calendar year.
            if (Kalenderjahr().Match(text) is { Success: true } kalender)
            {
                var jahr = int.Parse(kalender.Groups[1].Value);
                zeitraum = (new DateOnly(jahr, 1, 1), new DateOnly(jahr, 12, 31));
                gruppe = null;
                continue;
            }
            if (zeitraum is null) continue;

            var name = Grid.Text(line.Where(w => Grid.Slot(cols, w.Xc) == 0));
            if (name.Length == 0) continue;

            var beträge = Enumerable.Range(1, 3).Select(c => Grid.Text(line.Where(w => Grid.Slot(cols, w.Xc) == c))).ToList();
            if (!beträge.All(b => Betrag().IsMatch(b)))
            {
                gruppe = name;
                continue;
            }

            // A Gewerbezweig the Sammlung splits keeps its heading for the parts below it.
            var teil = Teil().IsMatch(name);
            into.Add(new Pauschbetrag(zeitraum.Value.Von, zeitraum.Value.Bis,
                teil && gruppe is not null ? $"{gruppe} {name}" : name,
                Euro(beträge[0]), Euro(beträge[1]), Euro(beträge[2])));
            if (!teil) gruppe = null;
        }
    }

    static readonly string[] Monate =
        ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    static DateOnly Tag(string tag, string monat, int jahr) =>
        new(jahr, Array.IndexOf(Monate, monat) + 1, int.Parse(tag));

    static List<Word>[,] Cells(List<Word> words, List<double> cols, List<double> rows)
    {
        var cells = new List<Word>[rows.Count - 1, cols.Count - 1];
        for (var r = 0; r < rows.Count - 1; r++)
            for (var c = 0; c < cols.Count - 1; c++)
                cells[r, c] = [];

        foreach (var word in words)
        {
            var r = Grid.Slot(rows, word.Baseline);
            var c = Grid.Slot(cols, word.Xc);
            if (r >= 0 && c >= 0) cells[r, c].Add(word);
        }
        return cells;
    }

    static long Euro(string text)
    {
        var match = Betrag().Match(text);
        if (!match.Success) throw new InvalidDataException($"richtsatz: kein Betrag in '{text}'");
        var cent = match.Groups[2].Success ? int.Parse(match.Groups[2].Value.PadRight(2, '0')) : 0;
        return long.Parse(match.Groups[1].Value.Replace(".", "")) * 100 + cent;
    }

    sealed record Stufe(string Name, long? Von, long? Bis, double Ab);

    // Column 1 carries the name of the Gewerbeklasse, what narrows it in small type below,
    // and the turnover bands it is staffelt by, each band above the rates that belong to it.
    sealed record Spalte(List<string> Namen, List<string> Zusätze, List<Stufe> Stufen)
    {
        public static Spalte Eins(List<Word> cell, double gross)
        {
            var spalte = new Spalte([], [], []);
            foreach (var line in Grid.Lines(cell))
            {
                var text = Grid.Text(line);
                var band = Band().Match(text);
                if (band.Success)
                {
                    var über = band.Groups["rel"].Value == "über";
                    var a = Euro(band.Groups["a"].Value);
                    var b = band.Groups["b"].Success ? Euro(band.Groups["b"].Value) : (long?)null;
                    spalte.Stufen.Add(new Stufe(band.Groups["stufe"].Value, über ? a : null, über ? b : a, line[0].Baseline - 3));
                    continue;
                }
                if (Weiter().Match(text) is { Success: true } weiter && spalte.Stufen.Count > 0)
                {
                    spalte.Stufen[^1] = spalte.Stufen[^1] with { Bis = Euro(weiter.Groups[1].Value) };
                    continue;
                }
                if (Umsatz().IsMatch(text)) continue;

                var klein = line.Where(w => w.Size < gross - 0.3).ToList();
                var name = line.Where(w => w.Size >= gross - 0.3).ToList();
                if (klein.Count > 0) spalte.Zusätze.Add(Grid.Text(klein));
                if (name.Count > 0 && spalte.Stufen.Count == 0) spalte.Namen.Add(Grid.Text(name));
            }
            return spalte;
        }
    }

    [GeneratedRegex(@"Kalenderjahr\s+(\d{4})")] private static partial Regex Kalenderjahr();
    [GeneratedRegex(@"^\d{4,6}\.\d$")] private static partial Regex Kennzahl();
    [GeneratedRegex(@"^(?<stufe>[A-Z])\s+(?<rel>bis|über)\s+(?<a>[\d.]+)\s*€(?:\s*bis\s+(?<b>[\d.]+)\s*€)?$")] private static partial Regex Band();
    [GeneratedRegex(@"^bis\s+([\d.]+)\s*€$")] private static partial Regex Weiter();
    [GeneratedRegex(@"^Wirtsch\.\s*Umsatz")] private static partial Regex Umsatz();
    [GeneratedRegex(@"\d+")] private static partial Regex Zahl();
    [GeneratedRegex(@"^(\d{1,3}(?:\.\d{3})+|\d+)(?:,(\d{1,2}))?$")] private static partial Regex Betrag();
    [GeneratedRegex(@"(\d{1,2})\.\s*(\w+)\s+bis\s+(\d{1,2})\.\s*(\w+)\s+(\d{4})")] private static partial Regex Zeitraum();
    [GeneratedRegex(@"^[a-z]\)")] private static partial Regex Teil();
}
