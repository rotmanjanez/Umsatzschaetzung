using System.Text.RegularExpressions;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Extract;

// The item table of one page, read from the model's structure heads and nothing else: cells
// from the cell starts, columns from the cells' x-overlap, one meaning per column, one item per
// line-item row. The model types nothing inside the table. Its column index only breaks ties:
// it is off by one on wide tables while the cell boundaries are not (v13/REPORT.md §5).
public sealed class Table
{
    sealed class Cell
    {
        public required List<TaggedWord> Words;
        public required Box Box;
        public required Role Role;
        public int Col;
        public Column? Column;
        public string Text => string.Join(" ", Words.Select(w => w.Word.Text));
    }

    sealed class Column
    {
        public int X0, X1;
        public Field? Meaning;
        public bool Decided;
        public readonly List<Cell> Cells = [];
        public int Width => X1 - X0;
        public IEnumerable<Cell> Head => Cells.Where(c => c.Role == Role.ColumnHeader);
        public IEnumerable<Cell> Body => Cells.Where(c => c.Role == Role.LineItem);
        public int Col => Cells.Count == 0 ? 0
            : Cells.GroupBy(c => c.Col).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        public string HeadText => string.Join(" ", Head.Select(c => c.Text));
    }

    readonly List<Column> columns = [];

    public List<Dictionary<Field, OcrWord>> Items { get; } = [];
    public bool HasColumns => columns.Count > 0;

    // A page without a column-header row is a continuation and keeps the previous page's
    // columns, ranges and meanings; the model also recognises a repeated header itself.
    public static Table Read(List<TaggedWord> page, Table? previous)
    {
        var rows = page.GroupBy(w => w.Row).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(w => w.Word.Box.X).ToList()).ToList();
        var table = new Table();
        if (previous is not null && !rows.Any(r => r[0].Role == Role.ColumnHeader))
            foreach (var c in previous.columns)
                table.columns.Add(new Column { X0 = c.X0, X1 = c.X1, Meaning = c.Meaning, Decided = c.Decided });

        var cells = rows.Select(r => (Role: r[0].Role, Cells: Cells(r))).ToList();
        foreach (var (role, list) in cells)
            if (role is Role.ColumnHeader or Role.LineItem) table.Place(list, extend: true);
        table.MergeHeaders();
        foreach (var (role, list) in cells)
            if (role is Role.LineWrap or Role.Continuation) table.Place(list, extend: false);
        table.columns.Sort((a, b) => a.X0.CompareTo(b.X0));
        table.Decide();
        table.Build(cells);
        return table;
    }

    static List<Cell> Cells(List<TaggedWord> row)
    {
        var cells = new List<Cell>();
        foreach (var w in row)
        {
            if (cells.Count == 0 || w.CellStart)
                cells.Add(new Cell { Words = [], Box = w.Word.Box, Role = row[0].Role });
            cells[^1].Words.Add(w);
            cells[^1].Box = Rows.Union(cells[^1].Box, w.Word.Box);
        }
        foreach (var c in cells)
            c.Col = c.Words.GroupBy(w => w.Col).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        return cells;
    }

    // Header and line-item cells found no column: a wrap row's cell that fits nowhere hangs
    // off the item as name text instead of opening a column of its own.
    void Place(List<Cell> row, bool extend)
    {
        foreach (var cell in row)
        {
            var column = Best(cell);
            if (column is null)
            {
                if (!extend) continue;
                column = new Column { X0 = cell.Box.X, X1 = cell.Box.X + cell.Box.W };
                columns.Add(column);
            }
            else if (extend)
            {
                column.X0 = Math.Min(column.X0, cell.Box.X);
                column.X1 = Math.Max(column.X1, cell.Box.X + cell.Box.W);
            }
            column.Cells.Add(cell);
            cell.Column = column;
        }
    }

    // The column overlapping the cell most; among near-equal overlaps the one the model's
    // column index agrees with, then the narrower one.
    Column? Best(Cell cell)
    {
        var scored = columns.Select(c => (Column: c, Overlap: Overlap(c, cell))).Where(x => x.Overlap > 0).ToList();
        if (scored.Count == 0) return null;
        var most = scored.Max(x => x.Overlap);
        return scored.Where(x => x.Overlap >= 0.75 * most)
            .OrderByDescending(x => x.Column.Col == cell.Col)
            .ThenBy(x => x.Column.Width)
            .ThenByDescending(x => x.Overlap)
            .First().Column;
    }

    static int Overlap(Column c, Cell cell) =>
        Math.Min(c.X1, cell.Box.X + cell.Box.W) - Math.Max(c.X0, cell.Box.X);

    // A left-aligned header over right-aligned amounts overlaps none of them, so header and
    // body come out as two columns. A header-only column joins the body-only neighbour the
    // model gives the same index, else its only body-only neighbour.
    void MergeHeaders()
    {
        for (var again = true; again;)
        {
            again = false;
            columns.Sort((a, b) => a.X0.CompareTo(b.X0));
            for (var i = 0; i < columns.Count && !again; i++)
            {
                var h = columns[i];
                if (!h.Head.Any() || h.Body.Any()) continue;
                var near = new[] { i - 1, i + 1 }
                    .Where(j => j >= 0 && j < columns.Count && columns[j].Body.Any() && !columns[j].Head.Any())
                    .Select(j => columns[j]).ToList();
                var into = near.FirstOrDefault(c => c.Col == h.Col) ?? (near.Count == 1 ? near[0] : null);
                if (into is null) continue;
                into.X0 = Math.Min(into.X0, h.X0);
                into.X1 = Math.Max(into.X1, h.X1);
                foreach (var c in h.Cells) c.Column = into;
                into.Cells.AddRange(h.Cells);
                columns.RemoveAt(i);
                again = true;
            }
        }
    }

    static readonly Regex Punct = new(@"[\s.\-/:_]");

    static string Norm(string s) => Punct.Replace(s.ToLowerInvariant(), "");

    // Longest key first, so "Artikelnummer" is the article id and "Artikel" the name.
    static readonly (string Key, Field? Meaning)[] Headings = Keys(
        (Field.Quantity, "Menge Anz Anzahl Stk Stück Mge Liefermenge Qty"),
        (Field.ArticleId, "Art.-Nr ArtNr Artikelnummer Art.Nr Artikel-Nr Nr"),
        (Field.Name, "Bezeichnung Artikelbezeichnung Artikel Beschreibung Text Produkt Warenbezeichnung"),
        (Field.Unit, "Einheit ME Einh EH Gebinde"),
        (Field.UnitPrice, "Einzelpreis E-Preis EP Preis Stückpreis Einzel à"),
        (Field.LineNet, "Betrag Summe Gesamt Gesamtpreis Netto Wert Nettobetrag GP"),
        (Field.Vat, "MwSt USt Steuer Satz St. % Tax"),
        (null, "Pos Position # Zeile Datum Lieferdatum MHD"));

    static (string, Field?)[] Keys(params (Field? Meaning, string Words)[] groups) =>
        [.. groups.SelectMany(g => g.Words.Split(' ').Select(w => (Norm(w), g.Meaning)))
            .OrderByDescending(k => k.Item1.Length)];

    // Exact first, then a key that opens the text ("Menge/Stk"), then a key the scan
    // misread by one character ("Eirheit", "Surme"). A short key such as ME or EP must
    // match exactly: as a prefix it claims "Merge" for the unit and "Epos" for the price.
    static (bool Hit, Field? Meaning) Heading(string text)
    {
        var key = Norm(text);
        if (key == "") return (false, null);
        foreach (var (k, meaning) in Headings)
            if (key == k) return (true, meaning);
        foreach (var (k, meaning) in Headings)
            if (k.Length >= 4 && key.StartsWith(k, StringComparison.Ordinal)) return (true, meaning);
        foreach (var (k, meaning) in Headings)
            if (k.Length >= 5 && OneEdit(key, k)) return (true, meaning);
        return (false, null);
    }

    static bool OneEdit(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1) return false;
        int i = 0, j = 0, edits = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) { i++; j++; continue; }
            if (++edits > 1) return false;
            if (a.Length > b.Length) i++;
            else if (a.Length < b.Length) j++;
            else { i++; j++; }
        }
        return edits + (a.Length - i) + (b.Length - j) <= 1;
    }

    static readonly Regex Amount = new(@"^[-€]?\s*\d{1,3}(?:[.\s]\d{3})*[,.]\d{2}\s*(?:€|EUR)?-?$");
    static readonly Regex Count = new(@"^\d{1,4}(?:[,.]\d{1,3})?(?:\s+\p{L}+\.?)?$");
    static readonly Regex Code = new(@"^(?=.*\d)[A-Za-z0-9][A-Za-z0-9.\-/]+$");
    static readonly Regex Integer = new(@"^\d{1,4}$");

    void Decide()
    {
        foreach (var c in columns)
        {
            if (c.Decided) continue;
            var (hit, meaning) = Heading(c.HeadText);
            if (!hit) continue;
            c.Meaning = meaning;
            c.Decided = true;
        }

        Unheader();

        var amounts = new List<Column>();
        var counts = new List<Column>();
        var codes = new List<Column>();
        var names = new List<Column>();
        foreach (var c in columns.Where(c => !c.Decided))
        {
            var texts = Texts(c);
            if (texts.Count == 0) continue;
            if (Mostly(texts, t => t.Contains('%'))) Fix(c, Field.Vat);
            else if (Sequential(texts)) Fix(c, null);
            else if (Alphabetic(texts) && Mostly(texts, t => Model.Units.Lookup(t) is not null)) Fix(c, Field.Unit);
            else if (Mostly(texts, Amount.IsMatch)) amounts.Add(c);
            else if (Mostly(texts, Count.IsMatch)) counts.Add(c);
            else if (Mostly(texts, Code.IsMatch)) codes.Add(c);
            else if (Alphabetic(texts)) names.Add(c);
        }
        if (!columns.Any(c => c.Meaning == Field.Name) && names.Count > 0)
            Fix(names.MaxBy(c => c.Width)!, Field.Name);
        var name = columns.FirstOrDefault(c => c.Meaning == Field.Name);
        if (!columns.Any(c => c.Meaning == Field.ArticleId) && name is not null)
            foreach (var c in codes.Where(c => c.X1 <= name.X0).Take(1)) Fix(c, Field.ArticleId);

        Arithmetic(counts, amounts);
    }

    // A header is a claim the body can contradict: "Artikel" over numeric codes is the
    // article id, and of two columns claiming one meaning only the one whose body fits keeps it.
    void Unheader()
    {
        foreach (var c in columns.Where(c => c.Meaning == Field.Name && c.Body.Any()))
            if (Mostly(Texts(c), Code.IsMatch) && !Alphabetic(Texts(c))) c.Meaning = Field.ArticleId;
        foreach (var meaning in new[] { Field.Name, Field.ArticleId, Field.Unit, Field.Vat })
        {
            var claims = columns.Where(c => c.Meaning == meaning).ToList();
            if (claims.Count < 2) continue;
            var fit = claims.Where(c => Fits(c, meaning)).ToList();
            var keep = fit.Count > 0 ? (meaning == Field.Name ? fit.MaxBy(c => c.Width) : fit[0]) : claims[0];
            foreach (var c in claims.Where(c => c != keep)) c.Meaning = null;
        }
    }

    bool Fits(Column c, Field meaning)
    {
        var texts = Texts(c);
        if (texts.Count == 0) return false;
        return meaning switch
        {
            Field.Name => Alphabetic(texts),
            Field.ArticleId => Mostly(texts, Code.IsMatch),
            Field.Unit => Alphabetic(texts) && Mostly(texts, t => Model.Units.Lookup(t) is not null),
            _ => Mostly(texts, t => t.Contains('%')),
        };
    }

    static List<string> Texts(Column c) => c.Body.Select(x => x.Text.Trim()).Where(t => t != "").ToList();

    static void Fix(Column c, Field? meaning)
    {
        c.Meaning = meaning;
        c.Decided = true;
    }

    static bool Mostly(List<string> texts, Func<string, bool> test) =>
        texts.Count(test) >= 0.8 * texts.Count;

    static bool Sequential(List<string> texts)
    {
        if (texts.Count < 2 || !texts.All(Integer.IsMatch)) return false;
        var v = texts.Select(int.Parse).ToList();
        return v.Zip(v.Skip(1)).All(p => p.Second == p.First + 1);
    }

    static bool Alphabetic(List<string> texts)
    {
        var all = string.Concat(texts);
        var letters = all.Count(char.IsLetter);
        return letters > 0 && letters >= 0.5 * all.Count(char.IsLetterOrDigit);
    }

    // quantity × unit price = line net decides among the numeric columns nothing else named:
    // the triple that holds on most rows wins, a column named by its header only competes for
    // its own meaning. With no row adding up, the amounts read left to right as price then net.
    void Arithmetic(List<Column> counts, List<Column> amounts)
    {
        var q = Candidates(Field.Quantity, [.. counts, .. amounts]);
        var p = Candidates(Field.UnitPrice, amounts);
        var n = Candidates(Field.LineNet, amounts);
        var best = (Hits: 0, Q: (Column?)null, P: (Column?)null, N: (Column?)null);
        foreach (var cq in q)
            foreach (var cp in p.Where(c => c != cq))
                foreach (var cn in n.Where(c => c != cq && c != cp))
                {
                    var hits = Hits(cq, cp, cn);
                    if (hits > best.Hits) best = (hits, cq, cp, cn);
                }
        if (best.Hits > 0)
        {
            Claim(Field.Quantity, best.Q!);
            Claim(Field.UnitPrice, best.P!);
            Claim(Field.LineNet, best.N!);
            return;
        }
        var free = amounts.Where(c => !c.Decided).ToList();
        if (!columns.Any(c => c.Meaning == Field.LineNet) && free.Count > 0) Fix(free[^1], Field.LineNet);
        free = amounts.Where(c => !c.Decided).ToList();
        if (!columns.Any(c => c.Meaning == Field.UnitPrice) && free.Count > 0) Fix(free[^1], Field.UnitPrice);
        if (!columns.Any(c => c.Meaning == Field.Quantity))
            foreach (var c in counts.Where(c => !c.Decided).Take(1)) Fix(c, Field.Quantity);
    }

    List<Column> Candidates(Field meaning, List<Column> open)
    {
        var named = columns.Where(c => c.Meaning == meaning).ToList();
        return named.Count == 1 ? named : [.. named, .. open.Where(c => !c.Decided)];
    }

    void Claim(Field meaning, Column winner)
    {
        foreach (var c in columns.Where(c => c.Meaning == meaning && c != winner)) Fix(c, null);
        Fix(winner, meaning);
    }

    int Hits(Column q, Column p, Column n)
    {
        var hits = 0;
        var rows = q.Body.Concat(p.Body).Concat(n.Body).Select(c => c.Words[0].Row).Distinct();
        foreach (var row in rows)
        {
            var quantity = Parse.Number(At(q, row), Parse.ScaleMilli);
            var price = Parse.Number(At(p, row), Parse.ScaleMicro);
            var net = Parse.Number(At(n, row), Parse.ScaleCents);
            if (quantity > 0 && price > 0 && net > 0
                && InvoiceMath.RoundDiv(quantity * price, 1000 * 10000) == net) hits++;
        }
        return hits;
    }

    static string At(Column c, int row) => c.Body.FirstOrDefault(x => x.Words[0].Row == row)?.Text ?? "";

    // Name text ends where a key opens: the GTIN, the article number or the lot printed
    // under or behind the name are not the name, and expected.json never carries them.
    static readonly Regex KeyWord = new(@"^(GTIN|EAN|Art(ikel)?[.\-]?(Nr|nummer|kennung)|Charge|Lot|MHD)\b", RegexOptions.IgnoreCase);
    static readonly char[] Bullets = ['•', '·', '-', '–', '*', '.', ','];

    static string NameText(Cell cell)
    {
        var words = new List<string>();
        foreach (var w in cell.Words)
        {
            if (KeyWord.IsMatch(w.Word.Text)) break;
            words.Add(w.Word.Text);
        }
        while (words.Count > 0 && words[^1].Trim(Bullets) == "") words.RemoveAt(words.Count - 1);
        while (words.Count > 0 && words[0].Trim(Bullets) == "") words.RemoveAt(0);
        return string.Join(" ", words);
    }

    // One item per line-item row. A wrap row adds its name-column text to the name and fills
    // a cell the item still lacks; anything else on it is a note the row does not need.
    void Build(List<(Role Role, List<Cell> Cells)> rows)
    {
        Dictionary<Field, OcrWord>? current = null;
        foreach (var (role, cells) in rows)
        {
            if (role is Role.LineWrap or Role.Continuation)
            {
                if (current is null) continue;
                foreach (var cell in cells)
                {
                    var meaning = cell.Column?.Meaning;
                    if (meaning is null && cell.Column is not null && cell.Column.Decided) continue;
                    var field = meaning ?? Field.Name;
                    // A wrap row that carries a key anywhere is a note ("Schema der
                    // Artikelkennung: 0160"), not the rest of the name.
                    if (field == Field.Name && cell.Words.Any(w => KeyWord.IsMatch(w.Word.Text))) continue;
                    var text = field == Field.Name ? NameText(cell) : cell.Text;
                    if (text == "") continue;
                    if (field == Field.Name && current.TryGetValue(field, out var name))
                        current[field] = new OcrWord { Text = name.Text + " " + text, Box = Rows.Union(name.Box, cell.Box) };
                    else current.TryAdd(field, new OcrWord { Text = text, Box = cell.Box });
                }
                continue;
            }
            current = null;
            if (role != Role.LineItem) continue;
            current = [];
            foreach (var cell in cells)
            {
                if (cell.Column?.Meaning is not { } field) continue;
                var text = field == Field.Name ? NameText(cell) : cell.Text;
                if (text == "") continue;
                // The article id set in the name column without a cell of its own.
                if (field == Field.Name && !current.ContainsKey(Field.Name) && !current.ContainsKey(Field.ArticleId)
                    && cells.Count(c => c.Column == cell.Column) > 1 && Code.IsMatch(text.TrimEnd(Bullets).Trim()))
                    field = Field.ArticleId;
                current[field] = current.TryGetValue(field, out var had)
                    ? new OcrWord { Text = had.Text + " " + text, Box = Rows.Union(had.Box, cell.Box) }
                    : new OcrWord { Text = text, Box = cell.Box };
            }
            Units(current);
            Items.Add(current);
        }
    }

    // "15 Stk" as one cell, in the quantity column when the row has no unit cell, or in the
    // unit column when it has no quantity cell: number and unit are both in there.
    static readonly Regex TrailingUnit = new(@"^\s*[-\d.,]+\s+(\S+)\s*$");

    static void Units(Dictionary<Field, OcrWord> cells)
    {
        var (have, lack) = cells.ContainsKey(Field.Unit) ? (Field.Unit, Field.Quantity) : (Field.Quantity, Field.Unit);
        if (cells.ContainsKey(lack) || !cells.TryGetValue(have, out var both)) return;
        var m = TrailingUnit.Match(both.Text);
        if (!m.Success || Model.Units.Lookup(Parse.UnitCode(m.Groups[1].Value)) is null) return;
        cells[Field.Unit] = new OcrWord { Text = m.Groups[1].Value, Box = both.Box };
        cells[Field.Quantity] = new OcrWord { Text = both.Text[..m.Groups[1].Index].Trim(), Box = both.Box };
    }

    public string DescribeRows(List<TaggedWord> page)
    {
        var rows = page.GroupBy(w => w.Row).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(w => w.Word.Box.X).ToList())
            .Where(r => r[0].Role is Role.ColumnHeader or Role.LineItem or Role.LineWrap or Role.Continuation);
        return string.Join("\n", rows.Select(r => $"  r{r[0].Row,-3} [{r[0].Role}] " + string.Join(" ", Cells(r).Select(c =>
            $"|{c.Text}:{(Best(c) is { } col ? columns.IndexOf(col) + ":" + (col.Meaning?.ToString() ?? "-") : "?")}"))));
    }

    public string Describe() => string.Join("\n", columns.Select(c =>
        $"  {c.X0,5}-{c.X1,-5} col={c.Col,-2} {c.Meaning?.ToString() ?? (c.Decided ? "-" : "?"),-13} head='{c.HeadText}' body={c.Body.Count()} e.g. {string.Join(" | ", c.Body.Take(2).Select(x => x.Text))}"));
}
