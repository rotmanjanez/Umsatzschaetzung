"""Gebindegrößen für Artikelnamen — die Einheit *im Namen*, nicht in der Mengenspalte.

Der Tagger verwechselt das Liter-l am Namensende mit einer 1: OCR liest "0,7 l"
als "0,71" oder als zwei Token "0,7" und "1", und das lose "1" wird dann als
quantity statt als name getaggt. Die Abhilfe gehört in den Korpus: genug Varianz
bei Maß, Schreibweise, Trennung und Gebinde, damit das Modell lernt, dass eine
Einheit hinter einer Dezimalzahl zum Namen gehört, egal wie sie gelesen wird.

`phrase()` liefert den gedruckten Text. Er landet unverändert in `expected.json`,
also ist jede Mutation hier selbstkonsistent: sie vergiftet die Supervision nicht,
sie erweitert sie. Zahlen in Mengen- oder Preisspalten fasst dieses Modul nicht an.
"""


# Schreibweisen je Maß. Die erste ist die häufigste; `weights` gewichtet sie.
SPELLINGS = {
    "l":   (["l", "l", "l", "L", "ltr", "ltr.", "Ltr.", "lt", "Liter"],
            [34, 34, 34, 14, 6, 5, 5, 2, 6]),
    "ml":  (["ml", "ml", "mL", "ML", "Milliliter"], [46, 46, 5, 2, 1]),
    "cl":  (["cl", "cl", "Cl"], [60, 35, 5]),
    "kg":  (["kg", "kg", "kg", "Kg", "KG", "kg.", "Kilo"], [36, 36, 36, 10, 5, 4, 3]),
    "g":   (["g", "g", "g", "gr", "gr.", "G", "Gramm"], [38, 38, 38, 8, 5, 3, 3]),
    "Stk": (["Stk", "Stk.", "St.", "Stck.", "Stück", "Stk"], [30, 26, 12, 8, 10, 14]),
    "cm":  (["cm", "cm", "mm"], [70, 25, 5]),
    "m":   (["m", "m", "lfm", "Meter"], [55, 25, 12, 8]),
    "%":   (["%", "%", "% vol", "Vol.-%"], [55, 25, 15, 5]),
}

# Übliche Werte je Maß, als gedruckte Zahl.
VALUES = {
    "l":   ["0,2", "0,25", "0,33", "0,4", "0,5", "0,7", "0,75", "1", "1,0", "1,5",
            "2", "2,0", "3", "5", "10", "20", "30", "50"],
    "ml":  ["100", "125", "150", "200", "250", "330", "375", "500", "700", "750"],
    "cl":  ["25", "33", "50", "70", "75", "100"],
    "kg":  ["0,4", "0,5", "0,75", "1", "1,0", "1,5", "2", "2,5", "3", "5", "10", "12,5", "25"],
    "g":   ["40", "55", "70", "90", "100", "125", "150", "180", "200", "250", "300",
            "400", "500", "750", "1000"],
    "Stk": ["6", "8", "10", "12", "16", "20", "24", "25", "30", "48", "50", "100",
            "144", "200", "250", "500", "1000"],
    "cm":  ["20", "24", "28", "30", "33", "38", "40", "45", "50"],
    "m":   ["50", "100", "150", "200", "250", "300", "500"],
    "%":   ["1,5", "3,5", "10", "13", "20", "30", "36", "38", "40", "45", "50", "60"],
}

# Gebinde vor der Größe: "Kiste 10 kg", "Btl. 20 Stk". Getrennt nach Inhalt, damit
# kein "Sack 0,33 l" entsteht — der Inhalt wählt den Topf über `containers()`.
GENERIC = (
    ["Kiste", "Kt", "Kt.", "Karton", "Kart.", "Pack", "Pkg.", "VE", "Gebinde", "Tray"],
    [8, 9, 6, 7, 4, 7, 4, 6, 4, 5],
)
LIQUID = (
    ["Fl.", "Flasche", "Kanister", "Bag in Box", "Eimer", "Dose", "Becher", "Fass",
     "Schlauch", "Träger"],
    [8, 5, 5, 3, 4, 4, 3, 3, 2, 3],
)
DRY = (
    ["Beutel", "Btl.", "Btl", "Netz", "Sack", "Steige", "Bund", "Schale", "Block",
     "Korb", "Tüte", "Vak.", "Rolle", "Eimer", "Dose"],
    [6, 7, 4, 5, 5, 4, 3, 3, 3, 2, 3, 3, 3, 3, 3],
)
LIQUID_KINDS = {"l", "ml", "cl"}


def containers(inner_kind):
    pool, weights = GENERIC
    extra, extra_w = LIQUID if inner_kind in LIQUID_KINDS else DRY
    return pool + extra, weights + extra_w


# Multiplikator-Schreibweisen: 6 x 0,7 l
TIMES = (["x", "x", "×", "à", "a"], [58, 22, 10, 7, 3])
PACKCOUNT = ["2", "3", "4", "6", "8", "10", "12", "20", "24", "30"]

# Welche Maße welche Warengruppe druckt, und wie oft.
SIZE_MIX = {
    "baeckerei":   [("g", 34), ("kg", 12), ("Stk", 26), ("pack", 18), ("curated", 10)],
    "metzgerei":   [("kg", 40), ("g", 24), ("Stk", 10), ("pack", 12), ("curated", 14)],
    "molkerei":    [("l", 22), ("ml", 12), ("g", 22), ("kg", 14), ("%", 10),
                    ("pack", 10), ("curated", 10)],
    "gemuese":     [("kg", 38), ("g", 14), ("Stk", 12), ("pack", 22), ("curated", 14)],
    "getraenke":   [("l", 40), ("ml", 14), ("cl", 6), ("pack", 26), ("curated", 14)],
    "wein":        [("l", 40), ("cl", 8), ("ml", 6), ("pack", 30), ("curated", 16)],
    "spirituosen": [("l", 40), ("cl", 6), ("%", 14), ("pack", 26), ("curated", 14)],
    "kaffee":      [("kg", 28), ("g", 26), ("Stk", 8), ("pack", 24), ("curated", 14)],
    "nonfood":     [("Stk", 26), ("cm", 14), ("m", 12), ("l", 14), ("pack", 22),
                    ("curated", 12)],
    "cc":          [("kg", 30), ("g", 14), ("l", 18), ("Stk", 8), ("pack", 18),
                    ("curated", 12)],
}
DEFAULT_MIX = [("kg", 30), ("l", 20), ("Stk", 20), ("pack", 20), ("curated", 10)]

# Anteil der Größen, die eine OCR-nahe Fehlschreibung gedruckt bekommen. Das Modell
# sieht damit "0,71", "0,7 I" und "0,7l" schon im Training als Teil des Namens.
ADVERSARIAL_SHARE = 0.18

# Verwechslungen, wie sie eine OCR auf schlechtem Druck produziert.
CONFUSIONS = [
    ("l", "1"), ("l", "I"), ("l", "|"), ("l", "t"),
    ("I", "l"), ("1", "l"),
    ("0", "O"), ("O", "0"), ("o", "0"),
    ("5", "S"), ("S", "5"), ("8", "B"), ("B", "8"), ("6", "b"), ("9", "g"),
    ("g", "9"), ("q", "g"),
    (",", "."), (".", ","),
    ("kg", "kq"), ("kg", "k9"), ("ml", "mI"), ("ml", "m1"), ("cl", "c1"),
    ("m", "rn"), ("rn", "m"),
]


def pick(rng, options, weights):
    return rng.choices(options, weights=weights, k=1)[0]


def spell(rng, measure):
    options, weights = SPELLINGS[measure]
    return pick(rng, options, weights)


def join(rng, number, unit):
    """Trennung zwischen Zahl und Einheit — mit Leerzeichen oder ohne.

    Ohne Leerzeichen entsteht ein einziges Token ("0,7l"), mit Leerzeichen zwei
    ("0,7", "l"). Beide Formen muss der Tagger als Namensbestandteil kennen.

    Kein geschütztes Leerzeichen: `blocks.words` trennt mit `str.split()`, und das
    trennt auch an U+00A0. Die Spans wären dieselben wie beim normalen Leerzeichen,
    nur bliebe das U+00A0 im erwarteten Namen stehen und `validate.py` schlüge an.
    """
    return f"{number} {unit}" if rng.random() < 0.76 else f"{number}{unit}"


def measure(rng, kind):
    return join(rng, rng.choice(VALUES[kind]), spell(rng, kind))


def packed(rng, inner_kinds):
    """Gebinde mit Inhalt: "Kt 6 x 0,7 l", "Karton 4 x 2,5 kg", "Pack 250 Stk"."""
    inner_kind = rng.choice(inner_kinds)
    container = pick(rng, *containers(inner_kind))
    inner = measure(rng, inner_kind)
    roll = rng.random()
    if roll < 0.46:
        times = pick(rng, *TIMES)
        count = rng.choice(PACKCOUNT)
        body = (f"{count} {times} {inner}" if rng.random() < 0.72
                else f"{count}{times}{inner}")
        return f"{container} {body}"
    if roll < 0.72:
        return f"{container} {inner}"
    if roll < 0.88:
        times = pick(rng, *TIMES)
        return f"{rng.choice(PACKCOUNT)} {times} {inner}"
    return f"{rng.choice(PACKCOUNT)}er {container}"


def corrupt(rng, text):
    """Eine OCR-nahe Fehlschreibung in den gedruckten Text setzen.

    Bevorzugt das Ende — dort steht die Einheit, und dort sitzt der Fehler, der
    das lose l/1 erzeugt. Der Rückgabewert ist der gedruckte *und* der erwartete
    Text, die Wahrheit bleibt also konsistent.
    """
    if rng.random() < 0.28:
        # Das Leerzeichen vor der Einheit verschlucken: "0,7 l" -> "0,7l" -> "0,71"
        parts = text.rsplit(" ", 1)
        glued = "".join(parts) if len(parts) == 2 else text
        for src, dst in CONFUSIONS:
            if glued.endswith(src) and rng.random() < 0.6:
                return glued[: -len(src)] + dst
        return glued
    hits = [(s, d) for s, d in CONFUSIONS if s in text]
    if not hits:
        return text
    src, dst = rng.choice(hits)
    where = text.rfind(src) if rng.random() < 0.75 else text.find(src)
    return text[:where] + dst + text[where + len(src):]


def phrase(rng, cat, category=None):
    """Eine Gebindegröße, wie sie im Artikelnamen gedruckt steht."""
    mix = SIZE_MIX.get(category, DEFAULT_MIX)
    kinds = [k for k, _ in mix]
    kind = pick(rng, kinds, [w for _, w in mix])
    if kind == "curated":
        text = rng.choice(cat["sizes"])
    elif kind == "pack":
        inner = [k for k in kinds if k not in ("pack", "curated", "%")] or ["Stk"]
        text = packed(rng, inner)
    else:
        text = measure(rng, kind)
    if rng.random() < ADVERSARIAL_SHARE:
        text = corrupt(rng, text)
    return text
