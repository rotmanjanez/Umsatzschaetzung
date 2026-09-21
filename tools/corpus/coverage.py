"""Wörter je Seite, je Klasse und je Seitenformat.

`validate.py` sieht eine gestrichene Spalte oder eine Klasse, die auf einem
Format nie vorkommt, *nicht*: die Seiten sind in sich stimmig. Erst diese
Tabelle zeigt die Null — und eine Null heißt, dass das Modell für dieses Format
"gibt es hier nicht" lernt. Nur die drei Kollisionsspalten dürfen auf A5/B5
fehlen; sie sind dort nachrangig.
"""
import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path[:0] = [str(Path(__file__).resolve().parent.parent / "train")]
import schema  # noqa: E402

import vocab  # noqa: E402

# Die Klassenliste kommt aus tools/train/schema.py — eine zweite Kopie hier wäre die
# Stelle, an der eine neue Klasse still aus dem Bericht fällt.
CLASSES = schema.FIELDS + ["O"]

# Die zwölf Wertklassen und die sechs Beschriftungsklassen sind alt; diese neunzehn
# sind v11 und müssen sich erst beweisen.
NEW_CLASSES = schema.FIELDS[18:]

# Anteil der *Seiten*, auf denen eine Klasse mindestens einmal vorkommen muss. Eine
# Klasse, die es nur auf 2 % der Seiten gibt, bekommt über 20 000 Seiten zwar tausende
# Wörter, aber das Modell sieht sie fast nie im Zusammenhang mit ihren Ablenkern.
TARGETS = {c: 0.15 for c in NEW_CLASSES}
TARGETS.update({"gtin": 0.08, "priceBasis": 0.08, "amountDue": 0.08, "lineGross": 0.08})


# Jede Achse, die `layout.template()` zieht und die `coverage.py` berichten soll.
# Eine Achse, die hier fehlt, fällt in keiner Auswertung auf — genau daran ist in
# v9 `decor_stamp` durchgerutscht (es stand nicht einmal in truth.json).
AXES = [# v11, Beschriftung (labels-Agent)
        "key_region", "net_alias", "show_amount_due", "name_gtin", "basis_inline", "name_unit_code",
        "vat_pct_form", "discount_form", "sender_keyed", "thanks_note", "footer_prose",
        # v11, Werbesatz im Briefkopf (tagline-Agent)
        "tagline_axis", "tagline_place", "tagline_style", "show_owner", "show_tagline",
        # v11, Familien (families-Agent)
        "doctype", "einvoice", "item_form", "sidebar", "period_block", "prepaid", "sections",
        "buyer_first", "qr_bill", "dotmatrix", "hand", "giant_logo", "form_fields",
        "host_fields", "head_rule", "title_text", "currency", "lang",
        "family", "page_format", "meta_style", "meta_place", "meta_table", "meta_colon",
        "meta_empty_key", "meta_bare_date", "sender_place", "head_contact", "title_style",
        "title_number", "title_spaced_key", "table_style", "header_style", "header_two_line",
        "header_unit_hint", "no_header_row", "caption", "totals_style", "totals_side",
        "totals_bottom", "totals_shade", "vat_row_form", "logo", "logo_side", "wordmark_caps",
        "footer", "footer_heads", "footer_code", "pos_format", "multi_row", "info_rows",
        "second_row_details", "detail_bold", "group_headings", "narrow_name", "glue_unit",
        "uppercase_headers", "bg_art", "bg_place", "head_band", "tint_panel", "decor_qr",
        "decor_stamp", "decor_barcode", "addr_customer_no", "delivery_sentence", "pay_box",
        "minus_style", "group_sep", "qty_style", "price_decimals", "date_format", "body_weight",
        "letter_tight", "cell_currency", "pageno_place", "receipt",
        # v12, Inhalt (content-Agent)
        "vat_rate_glued",
        # v12, Familien (families-Agent)
        "price_header_unit", "qty_unit_glue", "ei_bare", "ei_split", "vat_letter",
        "deposit_column", "day_columns"]

# ------------------------------------------------------------------ v12: Achsen mit Boden
#
# Eine Achse ohne Boden fällt nicht auf, wenn sie auf 0,3 % steht — und genau dann ist
# sie im Training wirkungslos. Jede Zeile hier ist ein Fehlerbild aus v12/PLAN.md.
# `coverage.py` endet mit 1, sobald eine davon unter ihrem Boden liegt.
FLOORS = {
    # Fehlerbild 1 — Wein: Jahrgang vor dem Namen, jahrgangscodierte Artikelnummer
    "vintage_first": 0.020,           # Anteil aller Positionen
    "vintage_first_wein": 0.30,       # Anteil der Positionen auf Weinrechnungen
    "artid_year": 0.030,              # Anteil der Positionen mit Artikelnummer
    "artid_year_wein": 0.30,
    # Namensvielfalt
    "opaque_name": 0.10,              # Name ist nur ein Code
    "caps_name": 0.04,                # VERSALIEN
    "long_name": 0.12,                # >= 6 Wörter, bricht über zwei bis drei Zeilen
    # Fehlerbild 3/4 — Einheit im Preiskopf, Satz in der Beschriftung
    "price_header_unit": 0.08,        # Anteil der Variationen (Achse der Familien)
    "vat_rate_glued": 0.35,           # Vorlagen mit Satz *in* der Beschriftung
    "mixed_rates": 0.35,              # Rechnungen mit zwei Sätzen in einer Tabelle (v12 voll: 39,6 %)
    "per_line_rate": 0.25,            # Variationen mit MwSt-Spalte in der Tabelle
    # Fehlerbild 6 — Mengen
    "qty_3dec": 0.040,                # gedruckte Menge mit drei Nachkommastellen
    "qty_thousands": 0.004,           # "1.200" — Punkt, der kein Komma ist
    "qty_bare_int": 0.10,             # nackte Ganzzahl ohne Einheitenwort in der Zeile (v12 voll: 12,8 %)
    "qty_glued_unit": 0.08,           # Variationen mit `qty_unit_glue`
    # Spaltenreihenfolge (Achse der Familien, hier aus truth.layout.columns gemessen)
    "name_before_artikel": 0.12,  # v12b: 14,1 % nach ORDER_WEIGHTS (gewollt)
}

YEAR_ID = re.compile(r"(?:^|[^0-9])(19|20)\d{2}(?:[^0-9]|$)")
YEAR_ID2 = re.compile(r"^\d{2}-[A-Z]{1,2}-\d")
WORD_ID = re.compile(r"^[A-Z]{3,6}-\d{1,3}$")
# Zwei Formen von `content.year_article_id`, die absichtlich wie eine gewöhnliche
# Artikelnummer aussehen: `2024578` (Jahr + drei Ziffern) und `S2417` (Kürzel +
# zweistelliges Jahr + zwei Ziffern). Beide sind für das Modell der interessante Fall
# und für die Messung ein Problem — deshalb hier zwei Muster, die eng genug sind, um
# `article_id`s eigene Formen (`A12345`, `123456`, `07-4711`) nicht mitzuzählen.
YEAR_ID3 = re.compile(r"^(19|20)\d{5}$")
YEAR_ID4 = re.compile(r"^[A-Z]{1,2}\d{4}$")
QTY_3DEC = re.compile(r"^-?\d+(?:[.\s]\d{3})*,\d{3}$")
QTY_THOUSANDS = re.compile(r"^-?\d{1,3}\.\d{3}$")
QTY_BARE_INT = re.compile(r"^\d{1,4}$")


def looks_opaque(name):
    """Ein Name, der nur ein Code ist. Kriterium: **kein** Token ist rein alphabetisch
    und mindestens vier Zeichen lang. `content.opaque_name` hält sich daran (jedes
    seiner Tokens trägt eine Ziffer), ein Versalname wie `RIESLING TROCKEN` dagegen
    nicht — der zählt als Name, nicht als Code."""
    return not any(tok.isalpha() and len(tok) >= 4 for tok in str(name).split())


def year_coded(article_id):
    a = str(article_id)
    return bool(YEAR_ID.search(a) or YEAR_ID2.match(a) or WORD_ID.match(a)
                or YEAR_ID3.match(a) or YEAR_ID4.match(a))

# Was aus den Daten kommt, nicht aus der Vorlage: Anteile je Rechnung.
SOURCE_AXES = ["kind", "bare_units", "free_lines", "deposit_lines", "code_units"]

# Einheitentext, der ein UN/ECE-Code ist und kein deutsches Wort. v10 druckte nur
# deutsche Wörter, und das Modell hat daraus gelernt, dass ein Großbuchstabencode keine
# Einheit ist (v10/REPORT.md, Lücke 6).
UNIT_CODES = set(vocab.UNIT_CODE_TEXT)
NUMERIC_ID = re.compile(r"^\d{3,4}$")

# Klasse × Seitenformat, wo die Null baulich ist und kein Fehler: der Kassenbon hat
# keine Rabatt-, Basis- und Bruttospalte, und schmale Blätter tragen keine
# Preisbasisspalte (`NARROW_COLUMNS`). Alles andere ist ein Befund.
KNOWN_GAPS = {("lineGross", "receipt"), ("priceBasis", "receipt"), ("lineDiscount", "receipt"),
              ("priceBasis", "a5"), ("priceBasis", "b5"),
              # Ein Kassenbon trägt kein Bestelldatum — er *ist* die Bestellung.
              ("orderDate", "receipt")}


def page_shares(pages, present):
    """Anteil der Seiten, auf denen eine Klasse mindestens einmal vorkommt."""
    total = max(1, sum(pages.values()))
    print("\nKlasse                 Wörter/Seite   Seiten mit >=1   Ziel")
    fails = []
    for c in CLASSES:
        share = present["pages"][c] / total
        target = TARGETS.get(c)
        mark = ""
        if target is not None:
            mark = f"   {target:.0%}" + ("  UNTER ZIEL" if share < target else "")
            if share < target:
                fails.append((c, share, target))
        print(f"{c:<22} {present['words'][c] / total:>10.2f}   {share:>12.1%}{mark}")
    return fails


def table(pages, counts, title):
    width = max(len(c) for c in CLASSES) + 1
    formats = sorted(pages)
    print(title.ljust(width) + "".join(f"{f:>10}" for f in formats))
    print("Seiten".ljust(width) + "".join(f"{pages[f]:>10}" for f in formats))
    zeros = []
    for c in CLASSES:
        print(c.ljust(width) + "".join(f"{counts[f][c] / pages[f]:>10.2f}" for f in formats))
        zeros += [(c, f) for f in formats
                  if not counts[f][c] and (c, f) not in KNOWN_GAPS]
    return zeros


def jsonl(root, path):
    """Dieselbe Tabelle, aber über die Wörter, die die OCR wirklich gelesen hat."""
    fmt_of = {}
    for p in Path(root).glob("*/*/truth.json"):
        truth = json.loads(p.read_text())
        fmt_of[truth["template"]] = truth["layout"]["page_format"]
    pages = Counter()
    counts = defaultdict(Counter)
    with open(path) as f:
        for line in f:
            if not line.strip():
                continue
            row = json.loads(line)
            fmt = fmt_of.get(row["template"], "?")
            pages[fmt] += 1
            for w in row["words"]:
                counts[fmt][w["field"]] += 1
    zeros = table(pages, counts, "OCR-Wörter je Seite")
    if zeros:
        print("\nNULLEN:")
        for c, f in zeros:
            print(f"  {c} auf {f}")
    return 1 if zeros else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--jsonl", default="", help="statt der Wahrheit die ausgerichteten "
                    "Zeilen aus align.py zählen; das Format kommt dann über die "
                    "Template-Id aus dem Korpus")
    args = ap.parse_args()

    if args.jsonl:
        return jsonl(args.root, args.jsonl)
    pages = Counter()
    counts = defaultdict(Counter)
    columns = defaultdict(Counter)
    variations = Counter()
    styles = Counter()
    marks = Counter()
    part2 = Counter()
    present = {"pages": Counter(), "words": Counter()}
    v12 = Counter()
    for path in sorted(Path(args.root).glob("*/*/truth.json")):
        truth = json.loads(path.read_text())
        fmt = truth["layout"]["page_format"]
        variations[fmt] += 1
        cols = truth["layout"].get("columns") or []
        if "name" in cols and "artikel" in cols and cols.index("name") < cols.index("artikel"):
            v12["name_before_artikel"] += 1
        if "mwst" in cols:
            v12["per_line_rate"] += 1
        if truth["layout"].get("price_header_unit"):
            v12["price_header_unit"] += 1
        if truth["layout"].get("qty_unit_glue"):
            v12["qty_glued_unit"] += 1
        if truth["layout"].get("vat_row_form") in ("gesamt", "gesamt_base", "zzgl",
                                                   "enthalten"):
            v12["vat_rate_glued"] += 1
        for page in truth["pages"]:
            unit_lines = {w["l"] for w in page["words"] if w["f"] == "unit"}
            for w in page["words"]:
                if w["f"] != "quantity":
                    continue
                v12["qty_words"] += 1
                if QTY_3DEC.match(w["t"]):
                    v12["qty_3dec"] += 1
                if QTY_THOUSANDS.match(w["t"]):
                    v12["qty_thousands"] += 1
                if QTY_BARE_INT.match(w["t"]) and w["l"] and w["l"] not in unit_lines:
                    v12["qty_bare_int"] += 1
        for c in truth["layout"]["columns"]:
            columns[fmt][c] += 1
        for axis in AXES:
            if axis in truth["layout"]:
                styles[f"{axis}={truth['layout'][axis]}"] += 1
        for axis in ("stamp", "scribble", "folds"):
            if axis in truth["degradation"]:
                styles[f"{axis}={truth['degradation'][axis]}"] += 1
        for axis in ("backdrop", "perspective"):
            if axis in truth["degradation"]:
                styles[f"{axis}={truth['degradation'][axis] > 0.0001}"] += 1
        wordmark_only = (truth["layout"]["logo"] == "wordmark"
                         and truth["layout"]["sender_place"] == "none")
        for page in truth["pages"]:
            pages[fmt] += 1
            seen = set()
            units_on_page = [w["t"] for w in page["words"] if w["f"] == "unit"]
            # Nackte Menge *auf derselben Position* wie eine rein numerische
            # Artikelnummer: gezählt wird je Positionsnummer (`l`), nicht je Seite —
            # eine Einheit irgendwo sonst auf dem Blatt (Preisbasis, Bonkopf) hat mit
            # dieser Zeile nichts zu tun.
            unit_lines = {w["l"] for w in page["words"] if w["f"] == "unit"}
            bare_numeric = any(NUMERIC_ID.match(w["t"]) and w["l"] not in unit_lines
                               for w in page["words"] if w["f"] == "articleId" and w["l"])
            for w in page["words"]:
                counts[fmt][w["f"]] += 1
                present["words"][w["f"]] += 1
                seen.add(w["f"])
            for f in seen:
                present["pages"][f] += 1
            # Die vier Anteile aus den v10-Diagnosen, gemessen an der Wahrheit statt an
            # einer Achse: eine Achse kann gezogen und trotzdem nicht gedruckt sein.
            if any(t in UNIT_CODES for t in units_on_page):
                part2["unit_code_text"] += 1
            if bare_numeric:
                part2["bare_qty_numeric_id"] += 1
            if truth["layout"].get("sender_keyed"):
                part2["keyed_supplier"] += 1
            if truth["layout"].get("thanks_note") or truth["layout"].get("footer_prose"):
                part2["footer_prose"] += 1
            if wordmark_only:
                marks["pages"] += 1
                marks["supplier"] += sum(1 for w in page["words"] if w["f"] == "supplier")

    zeros = table(pages, counts, "Wörter je Seite")
    fails = page_shares(pages, present)
    total_pages = max(1, sum(pages.values()))
    print("\nAnteile aus den v10-Diagnosen (Anteil der Seiten):")
    for key, title in (("unit_code_text", "Einheitentext ist ein UN/ECE-Code"),
                       ("bare_qty_numeric_id", "nackte Menge + numerische Artikelnummer"),
                       ("keyed_supplier", "beschrifteter Lieferant (Firmenname:)"),
                       ("footer_prose", "Fließtext mit Rolle footer")):
        print(f"  {title:<44} {part2[key]:>7} {part2[key] / total_pages:>7.1%}")
    width = max(len(c) for c in CLASSES) + 1
    formats = sorted(pages)
    print("\nSpalten je Variation".ljust(width) + "".join(f"{f:>10}" for f in formats))
    for c in ("pos", "artikel", "gtin", "groesse", "name", "menge", "einheit", "basis", "preis",
              "rabatt", "mwst", "betrag", "brutto", "waehrung", "steuercode", "wg"):
        print(c.ljust(width) + "".join(f"{columns[f][c] / variations[f]:>10.2f}" for f in formats))
    total = sum(variations.values()) or 1
    print("\nAchsen (Anteil der Variationen):")
    for k, v in sorted(styles.items()):
        print(f"  {k:<44} {v:>7} {v / total:>7.1%}")
    source = Counter()
    invoices = 0
    for path in sorted(Path(args.root).glob("*/source.json")):
        data = json.loads(path.read_text())
        invoices += 1
        for axis in SOURCE_AXES:
            source[f"{axis}={data.get(axis)}"] += 1
        source[f"charges={bool(data.get('charges'))}"] += 1
    if invoices:
        print(f"\nDaten (Anteil der {invoices} Rechnungen):")
        for k, v in sorted(source.items()):
            print(f"  {k:<44} {v:>7} {v / invoices:>7.1%}")
    print(f"\nWortmarken-Seiten ohne Absender: {marks['pages']}, "
          f"supplier-Wörter darauf: {marks['supplier']}")

    # ---------------------------------------------------- v12: Achsen mit Boden
    items = wein_items = ids = wein_ids = invoices2 = 0
    names = Counter()
    suppliers = set()
    for path in sorted(Path(args.root).glob("*/expected.json")):
        exp = json.loads(path.read_text())
        src = json.loads((path.parent / "source.json").read_text()) \
            if (path.parent / "source.json").exists() else {}
        invoices2 += 1
        suppliers.add(exp.get("supplierName", ""))
        wein = src.get("category") == "wein"
        if len(exp.get("vatBreakdown") or []) > 1:
            v12["mixed_rates"] += 1
        for line in exp["lines"]:
            items += 1
            names[line["name"]] += 1
            head = line["name"].split()[:1]
            first_is_year = bool(head and head[0].isdigit() and len(head[0]) == 4
                                 and head[0][:2] in ("19", "20"))
            if first_is_year:
                v12["vintage_first"] += 1
            if looks_opaque(line["name"]):
                v12["opaque_name"] += 1
            elif line["name"] == line["name"].upper():
                v12["caps_name"] += 1
            if len(line["name"].split()) >= 6:
                v12["long_name"] += 1
            if line.get("sellerArticleId"):
                ids += 1
                if year_coded(line["sellerArticleId"]):
                    v12["artid_year"] += 1
            if wein:
                wein_items += 1
                if first_is_year:
                    v12["vintage_first_wein"] += 1
                if line.get("sellerArticleId"):
                    wein_ids += 1
                    if year_coded(line["sellerArticleId"]):
                        v12["artid_year_wein"] += 1
    total_var = max(1, sum(variations.values()))
    base = {"vintage_first": items, "opaque_name": items, "caps_name": items,
            "long_name": items, "artid_year": ids or 1,
            "vintage_first_wein": wein_items or 1, "artid_year_wein": wein_ids or 1,
            "mixed_rates": invoices2 or 1,
            "qty_3dec": v12["qty_words"] or 1, "qty_thousands": v12["qty_words"] or 1,
            "qty_bare_int": v12["qty_words"] or 1}
    print("\nv12-Achsen (mit Boden):")
    floor_fails = []
    for key, floor in FLOORS.items():
        denom = base.get(key, total_var)
        share = v12[key] / max(1, denom)
        flag = "  UNTER BODEN" if share < floor else ""
        print(f"  {key:<24} {v12[key]:>7} / {denom:<7} {share:>7.1%}   >= {floor:.1%}{flag}")
        if share < floor:
            floor_fails.append((key, share, floor))
    if items:
        repeat = sum(n for n in names.values() if n > 1)
        print(f"\nNamensvielfalt: {len(names)} verschiedene von {items} Positionen, "
              f"Mehrfachnennungen {repeat / items:.1%}; "
              f"{len(suppliers)} verschiedene Lieferanten auf {invoices2} Rechnungen")
    if zeros:
        print("\nNULLEN:")
        for c, f in zeros:
            print(f"  {c} auf {f}")
    if fails:
        print("\nUNTER ZIEL (Anteil der Seiten mit dieser Klasse):")
        for c, share, target in fails:
            print(f"  {c:<20} {share:>7.1%} < {target:.0%}")
    if floor_fails:
        print("\nUNTER BODEN (v12-Achsen):")
        for key, share, floor in floor_fails:
            print(f"  {key:<24} {share:>7.1%} < {floor:.1%}")
    return 1 if zeros or fails or floor_fails or not marks["supplier"] else 0


if __name__ == "__main__":
    sys.exit(main())
