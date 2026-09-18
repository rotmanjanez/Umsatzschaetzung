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
        "letter_tight", "cell_currency", "pageno_place", "receipt"]

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
    for path in sorted(Path(args.root).glob("*/*/truth.json")):
        truth = json.loads(path.read_text())
        fmt = truth["layout"]["page_format"]
        variations[fmt] += 1
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
    if zeros:
        print("\nNULLEN:")
        for c, f in zeros:
            print(f"  {c} auf {f}")
    if fails:
        print("\nUNTER ZIEL (Anteil der Seiten mit dieser Klasse):")
        for c, share, target in fails:
            print(f"  {c:<20} {share:>7.1%} < {target:.0%}")
    return 1 if zeros or fails or not marks["supplier"] else 0


if __name__ == "__main__":
    sys.exit(main())
