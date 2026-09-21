import argparse
import json
import re
import os
import sys
from collections import Counter
from pathlib import Path

import money
import vocab

# Die Klassenliste kommt aus tools/train/schema.py und wird **nicht** hier gepflegt:
# eine zweite Kopie ist genau die Stelle, an der eine neue Klasse im Korpus landet,
# ohne dass der Trainingskopf sie kennt (oder umgekehrt). Ein `data-f`, das dort fehlt,
# ist ein Tippfehler im Renderer und kein neues Feld.
sys.path[:0] = [str(Path(__file__).resolve().parent.parent / "train")]
import schema  # noqa: E402

FIELDS = set(schema.FIELDS)

# v12: Einheitenwörter, die niemals die Klasse `quantity` tragen dürfen, und die Form,
# die ein `vat`-Wort haben muss (nur der Satz, ohne Prozentzeichen).
QTY_WORDS = {w.strip(".").casefold() for w in vocab.QTY_UNIT_WORDS} | {
    w.casefold() for w in vocab.UNIT_CODE_TEXT}
VAT_VALUE = re.compile(r"^-?\d{1,2}(?:,\d{1,2})?$")


def join(words, field, line):
    picked = [w["t"] for w in words if w["f"] == field and w["l"] == line]
    return " ".join(picked)


def runs(words, field):
    """Die zusammenhängenden Läufe einer Klasse auf einer Seite.

    Zwei Wörter gehören zu einem Lauf, wenn sie auf derselben Zeile stehen (ihre
    Boxen überlappen senkrecht) und waagrecht nicht weiter auseinander liegen als
    eine halbe Zeilenhöhe mal drei. Das ist grob dieselbe Gruppierung wie in
    `align.group_rows`, reicht hier aber, weil nur gezählt wird.
    """
    picked = sorted((w for w in words if w["f"] == field),
                    key=lambda w: (round(w["box"][1] / max(w["box"][3], 1.0)), w["box"][0]))
    out = []
    for w in picked:
        x, y, bw, bh = w["box"]
        hit = None
        for run in out:
            lx, ly, lw, lh = run[-1]["box"]
            same_row = ly <= y + bh / 2 < ly + lh or y <= ly + lh / 2 < y + bh
            if same_row and x - (lx + lw) < 3 * bh:
                hit = run
                break
        if hit is None:
            out.append([w])
        else:
            hit.append(w)
    return out


def norm_words(words, field):
    return " ".join(w["t"] for w in words if w["f"] == field).casefold().replace(" ", "")


def same(shown, value, scale=money.CENT, decimals=2):
    """Gedruckter Betrag gegen `expected.json`.

    Die Vorlage bestimmt Tausendertrennung ("1.234,56", "1 234,56", "1234,56") und
    die Stellung des Minus ("-70,81", "70,81-"). `validate.py` kennt die Vorlage
    nicht, also vergleicht es gegen alle Schreibweisen desselben Betrags.
    """
    bare = shown
    # Jede Währung, die eine Vorlage drucken kann — die Familie `swiss` rechnet in CHF.
    for code in ("€", "EUR", "CHF"):
        bare = bare.replace(code, "")
    bare = bare.replace(" ", "")
    return bare in {f.replace(" ", "") for f in money.forms(value, scale, decimals)}


def check(directory):
    problems = []
    expected = json.load(open(os.path.join(directory, "expected.json"), encoding="utf-8"))
    source_path = os.path.join(directory, "source.json")
    source = json.load(open(source_path, encoding="utf-8")) if os.path.exists(source_path) else {}
    counts = Counter()

    # Zuschläge im Nettobetrag: die Summe der Positionen ist NICHT der Nettobetrag,
    # sobald Versand, Fracht, Pfand oder Rundung als eigene Summenzeile gedruckt
    # werden. Genau das ist die Konvention der echten Belege, und genau deshalb
    # muss sie hier geprüft werden statt "Summe der Zeilen == netTotal".
    charges = sum(c["amount"] for c in source.get("charges", []))
    lines_net = sum(l["lineNet"] for l in expected["lines"])
    # Die Belegart steht in `source.json` und ist die verlässliche Quelle. Die alte
    # Ableitung „kein vatBreakdown und netTotal 0" trägt nicht mehr: `families.adapt`
    # rechnet die Sätze eines Belegs um (Schweiz, Kleinunternehmer) und hinterlässt
    # dabei auch auf einem Lieferschein eine Aufschlüsselung mit Nullbeträgen.
    delivery = (source.get("kind") == "delivery_note"
                or (not expected["vatBreakdown"] and expected["netTotal"] == 0))
    if not delivery and not source.get("defect"):
        if lines_net + charges != expected["netTotal"]:
            problems.append(f"netTotal {expected['netTotal']} != Positionen {lines_net} "
                            f"+ Zuschläge {charges}")

    # Einheitenkonvention: kein gedruckter Einheitentext heißt `unitText: null`
    # und `unitCode: "H87"`.
    free_lines = set()
    bare_lines = set()
    for line in expected["lines"]:
        if line["unitText"] is None:
            bare_lines.add(line["no"])
            if line["unitCode"] != "H87":
                problems.append(f"line {line['no']}: unitText null, aber unitCode "
                                f"{line['unitCode']!r} statt H87")
        if not delivery and line["unitPrice"] == 0 and line["lineNet"] == 0:
            free_lines.add(line["no"])

    for entry in sorted(os.listdir(directory)):
        path = os.path.join(directory, entry)
        if not os.path.isdir(path):
            continue
        record = os.path.join(path, "truth.json")
        if not os.path.exists(record):
            problems.append(f"{entry}: no truth.json (incomplete variation)")
            continue
        truth = json.load(open(record, encoding="utf-8"))
        words = [w for page in truth["pages"] for w in page["words"]]
        first = truth["pages"][0]["words"]
        for w in words:
            counts[w["f"]] += 1
            if w["f"] != "O" and w["f"] not in FIELDS:
                problems.append(f"{entry}: unknown field {w['f']}")
        for page in truth["pages"]:
            if not os.path.exists(os.path.join(path, page["image"])):
                problems.append(f"{entry}: missing image {page['image']}")
            for w in page["words"]:
                x, y, bw, bh = w["box"]
                if bw <= 0 or bh <= 0:
                    problems.append(f"{entry}/{page['image']}: empty box for {w['t']!r}")
                if x < -20 or y < -20 or x + bw > page["width"] + 20 or y + bh > page["height"] + 20:
                    problems.append(f"{entry}/{page['image']}: box outside page for {w['t']!r}")
        # ------------------------------------------------------------ v11-Prüfungen
        for page in truth["pages"]:
            pw = page["words"]
            # ---------------------------------------------------- v12-Prüfungen
            # Eine Menge ist nie ein Einheitenwort. Die geklebte Form (`17Fl`) steht
            # in der Wahrheit als zwei Tokens — geht dabei das Leerzeichen an der
            # falschen Stelle verloren, landet "Fl" als `quantity`, und das ist genau
            # die Verwechslung, die Fehlerbild 6 ausmacht.
            for w in pw:
                if w["f"] == "quantity":
                    if w["t"].strip(".").casefold() in QTY_WORDS:
                        problems.append(f"{entry}/{page['image']}: quantity ist ein "
                                        f"Einheitenwort ({w['t']!r})")
                    elif not any(ch.isdigit() for ch in w["t"]):
                        problems.append(f"{entry}/{page['image']}: quantity ohne Ziffer "
                                        f"({w['t']!r})")
                # Nur der *Satz* ist `vat`. Das Prozentzeichen steht als eigenes
                # `O`-Token daneben, auch wenn es gedruckt daran klebt (`7%`).
                elif w["f"] == "vat" and not VAT_VALUE.match(w["t"]):
                    problems.append(f"{entry}/{page['image']}: vat-Wort {w['t']!r} ist "
                                    f"kein reiner Satz")
            # Genau *ein* Lauf der Rechnungsnummer je Seite. Zwei Läufe hieße, dass
            # Überschrift und Kopfblock sie beide tragen — die Montage nimmt dann den
            # ersten, und welcher das ist, hängt an der Lesereihenfolge.
            n = len(runs(pw, "invoiceNumber"))
            if not delivery and n != 1:
                problems.append(f"{entry}/{page['image']}: {n} invoiceNumber-Läufe statt einem")
            # Der Kunde darf nie Wort für Wort der Lieferant sein: zwei identische Läufe
            # auf einer Seite, einmal `supplier` und einmal `buyer`, sind kein
            # Kontextsignal, sondern Rauschen.
            b, s = norm_words(pw, "buyer"), norm_words(pw, "supplier")
            if b and b == s:
                problems.append(f"{entry}/{page['image']}: buyer == supplier ({b!r})")
            # `subtotal` gibt es nur, wenn danach wirklich Zuschlagszeilen folgen.
            if any(w["f"] == "subtotal" for w in pw) and not source.get("charges"):
                problems.append(f"{entry}/{page['image']}: subtotal ohne Zuschlagszeile")
            # `amountDue` gibt es nur neben einer Vorauszahlungszeile.
            if any(w["f"] == "amountDue" for w in pw) and not any(
                    w["f"] == "discount" for w in pw):
                problems.append(f"{entry}/{page['image']}: amountDue ohne Anzahlungszeile")
            if any(w["f"] == "charge" for w in pw) and not source.get("charges"):
                problems.append(f"{entry}/{page['image']}: charge ohne Zuschlag in source.json")
        for line in expected["lines"]:
            name = join(words, "name", line["no"])
            if name.replace(" ", "") != line["name"].replace(" ", ""):
                problems.append(f"{entry}: line {line['no']} name {name!r} != {line['name']!r}")
            # Die GTIN steht nie *im* Namen: gedruckt hängt sie hinter ihm, in
            # `expected.json` endet der Name davor, und ihre Wörter tragen `gtin`.
            if line.get("gtin"):
                if line["gtin"] in line["name"].replace(" ", ""):
                    problems.append(f"{entry}: line {line['no']} GTIN steckt im Namen")
                shown = join(words, "gtin", line["no"])
                if shown and shown.replace(" ", "") != line["gtin"]:
                    problems.append(f"{entry}: line {line['no']} gtin {shown!r} != "
                                    f"{line['gtin']!r}")
            if line["no"] in bare_lines and join(words, "unit", line["no"]):
                problems.append(f"{entry}: line {line['no']} hat unitText null, druckt aber "
                                f"die Einheit {join(words, 'unit', line['no'])!r}")
            if delivery:
                continue
            if line["no"] in free_lines:
                # Freizeilen drucken leere Zellen, keine Null.
                for field in ("unitPrice", "lineNet"):
                    shown = join(words, field, line["no"])
                    if shown:
                        problems.append(f"{entry}: Freizeile {line['no']} druckt {field} "
                                        f"{shown!r} statt einer leeren Zelle")
                continue
            net = join(words, "lineNet", line["no"])
            if not same(net, line["lineNet"]):
                problems.append(f"{entry}: line {line['no']} lineNet {net!r} != "
                                f"{money.cents(line['lineNet'])!r}")
        if not delivery:
            # Die Gesamtbeträge stehen dort, wo eine Region mit der Rolle `total`
            # steht — nicht zwingend auf der letzten Seite. Der XRechnung-Viewer
            # druckt sie seit v12 im Drei-Seiten-Schnitt (Übersicht / Details /
            # Zusätze) auf die **Übersicht**, also auf Seite 1. Die alte Prüfung
            # gegen `pages[-1]` hätte jede solche Variation beanstandet.
            totals_pages = [p for p in truth["pages"]
                            if any(r.get("role") == "total"
                                   for r in p.get("regions", []))] or truth["pages"][-1:]
            last = [w for p in totals_pages for w in p["words"]]
            for field, value in (("netTotal", expected["netTotal"]),
                                 ("grossTotal", expected["grossTotal"])):
                shown = join(last, field, 0)
                if not same(shown, value):
                    problems.append(f"{entry}: {field} {shown!r} != {money.cents(value)!r}")
            shown = join(first, "invoiceNumber", 0)
            if shown != expected["number"]:
                problems.append(f"{entry}: number {shown!r} != {expected['number']!r}")
            if not join(first, "invoiceDate", 0):
                problems.append(f"{entry}: no invoiceDate word on the first page")
        # Der Lieferantenname steht je nach Briefkopf im Absender, in der Wortmarke
        # oder — wenn es beides nicht gibt — in der Fußzeile. Genau eine dieser
        # Stellen muss ihn beschriftet tragen, sonst hat die Seite keinen Lieferanten.
        if not any(w["f"] == "supplier" for w in words):
            problems.append(f"{entry}: no supplier word anywhere")
    return problems, counts


def units_gate():
    """Jedes Einheitenwort, das eine Warengruppe drucken kann, muss in
    `data/units.json` stehen.

    `units.code()` wirft absichtlich statt zu raten — und `content.make` ruft es je
    Position. Eine neue Warengruppe mit einem Wort, das dort fehlt, tötet in
    `generate.py` mitten im Lauf einen Worker (`pool.map` bricht ab, die Rechnungen
    danach fehlen), und es fällt erst nach Stunden auf. Genau das ist am 20.09. mit
    `Becher` und `Glas` passiert. Diese Prüfung läuft vor jedem `validate.py`-Lauf
    und ist auch einzeln aufrufbar:

        python3 -c "import validate; print(validate.units_gate())"
    """
    sys.path[:0] = [str(Path(__file__).resolve().parent.parent)]
    import units as unit_table
    import vocab as v
    bad = []
    for name, cat in v.CATEGORIES.items():
        for word, _ in cat["units"]:
            if unit_table.resolve(word.strip().strip(".")) is None:
                bad.append(f"{name}: Einheit {word!r} fehlt in data/units.json")
    for word in v.QTY_UNIT_WORDS:
        if unit_table.resolve(word.strip().strip(".")) is None:
            bad.append(f"QTY_UNIT_WORDS: {word!r} fehlt in data/units.json")
    # Die UN/ECE-Codes werden als *Code* gedruckt, nicht als Alias — sie stehen in
    # `units.TABLE`, nicht in `units.ALIAS`.
    for word in v.UNIT_CODE_TEXT:
        if word not in unit_table.TABLE:
            bad.append(f"UNIT_CODE_TEXT: {word!r} ist kein Code in data/units.json")
    return bad


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    args = ap.parse_args()
    gate = units_gate()
    for line in gate:
        print(f"UNITS-GATE: {line}")
    total = Counter()
    failures = 0
    checked = 0
    for name in sorted(os.listdir(args.root)):
        directory = os.path.join(args.root, name)
        if not os.path.isdir(directory) or not os.path.exists(os.path.join(directory, "expected.json")):
            continue
        checked += 1
        problems, counts = check(directory)
        total.update(counts)
        for p in problems:
            print(f"{name}/{p}")
        failures += len(problems)
    failures += len(gate)
    print(f"\n{checked} invoices checked, {failures} problems")
    for field, n in total.most_common():
        print(f"  {field:<14} {n:>7}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
