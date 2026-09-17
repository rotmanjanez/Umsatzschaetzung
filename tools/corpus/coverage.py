"""Wörter je Seite, je Klasse und je Seitenformat.

`validate.py` sieht eine gestrichene Spalte oder eine Klasse, die auf einem
Format nie vorkommt, *nicht*: die Seiten sind in sich stimmig. Erst diese
Tabelle zeigt die Null — und eine Null heißt, dass das Modell für dieses Format
"gibt es hier nicht" lernt. Nur die drei Kollisionsspalten dürfen auf A5/B5
fehlen; sie sind dort nachrangig.
"""
import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

CLASSES = ["quantity", "unit", "name", "articleId", "unitPrice", "lineNet", "vat",
           "invoiceNumber", "invoiceDate", "supplier", "netTotal", "grossTotal",
           "numberLabel", "dateLabel", "netLabel", "grossLabel", "vatLabel", "otherLabel", "O"]


def table(pages, counts, title):
    width = max(len(c) for c in CLASSES) + 1
    formats = sorted(pages)
    print(title.ljust(width) + "".join(f"{f:>10}" for f in formats))
    print("Seiten".ljust(width) + "".join(f"{pages[f]:>10}" for f in formats))
    zeros = []
    for c in CLASSES:
        print(c.ljust(width) + "".join(f"{counts[f][c] / pages[f]:>10.2f}" for f in formats))
        zeros += [(c, f) for f in formats if not counts[f][c]]
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
    for path in sorted(Path(args.root).glob("*/*/truth.json")):
        truth = json.loads(path.read_text())
        fmt = truth["layout"]["page_format"]
        variations[fmt] += 1
        for c in truth["layout"]["columns"]:
            columns[fmt][c] += 1
        for axis in ("meta_style", "meta_place", "sender_place", "title_style", "table_style",
                     "header_style", "totals_style", "logo", "logo_side", "footer", "pos_format"):
            styles[f"{axis}={truth['layout'][axis]}"] += 1
        wordmark_only = (truth["layout"]["logo"] == "wordmark"
                         and truth["layout"]["sender_place"] == "none")
        for page in truth["pages"]:
            pages[fmt] += 1
            for w in page["words"]:
                counts[fmt][w["f"]] += 1
            if wordmark_only:
                marks["pages"] += 1
                marks["supplier"] += sum(1 for w in page["words"] if w["f"] == "supplier")

    zeros = table(pages, counts, "Wörter je Seite")
    width = max(len(c) for c in CLASSES) + 1
    formats = sorted(pages)
    print("\nSpalten je Variation".ljust(width) + "".join(f"{f:>10}" for f in formats))
    for c in ("pos", "artikel", "gtin", "name", "menge", "einheit", "basis", "preis", "rabatt",
              "mwst", "betrag", "waehrung", "steuercode", "wg"):
        print(c.ljust(width) + "".join(f"{columns[f][c] / variations[f]:>10.2f}" for f in formats))
    print("\nAchsen:", ", ".join(f"{k} {v}" for k, v in sorted(styles.items())))
    print(f"\nWortmarken-Seiten ohne Absender: {marks['pages']}, "
          f"supplier-Wörter darauf: {marks['supplier']}")
    if zeros:
        print("\nNULLEN:")
        for c, f in zeros:
            print(f"  {c} auf {f}")
    return 1 if zeros or not marks["supplier"] else 0


if __name__ == "__main__":
    sys.exit(main())
