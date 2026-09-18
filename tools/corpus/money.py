QTY = 1000
PRICE = 1_000_000
CENT = 100
BP = 10_000

# Tausendertrennung, wie sie gedruckt wird. Das Leerzeichen ist der interessante
# Fall: "1 234,56" zerfällt in *zwei* Tokens, beide mit der Klasse der Zahl. Echte
# Rechnungen drucken es so, und der Tagger muss die zweite Hälfte mitnehmen.
GROUPS = (".", " ", "")
# Vorzeichen: "-70,81" oder "70,81-". Die nachgestellte Form kommt von
# Warenwirtschaften mit COBOL-Erbe und steht auf jeder zweiten Gutschrift.
MINUS = ("lead", "trail")


def round_div(num, den):
    return -((-num + den // 2) // den) if num < 0 else (num + den // 2) // den


def line_net(quantity, unit_price, base_qty):
    return round_div(quantity * unit_price, (base_qty or QTY) * 10_000)


def de(value, scale, decimals=2, group=".", minus="lead"):
    negative = value < 0
    value = abs(value)
    whole, frac = divmod(round_div(value * 10**decimals, scale), 10**decimals)
    text = f"{whole:,}".replace(",", group)
    if decimals:
        text = f"{text},{frac:0{decimals}d}"
    if not negative:
        return text
    return text + "-" if minus == "trail" else "-" + text


def cents(value, group=".", minus="lead"):
    return de(value, CENT, 2, group, minus)


def price(value, decimals=2, group=".", minus="lead"):
    return de(value, PRICE, decimals, group, minus)


def qty(value, style="trim", group="."):
    if style == "d2":
        return de(value, QTY, 2, group)
    if style == "d3":
        return de(value, QTY, 3, group)
    text = de(value, QTY, 3, group).rstrip("0").rstrip(",")
    return text if "," in text or value % QTY else de(value, QTY, 0, group)


def pct(value):
    text = de(value, BP // 100, 2)
    return text[:-3] if text.endswith(",00") else text


def forms(value, scale=CENT, decimals=2):
    """Alle Schreibweisen desselben Betrags — jede Trennung, jedes Vorzeichen.

    `validate.py` kennt die Vorlage nicht, die eine Variation gezogen hat, prüft
    aber den gedruckten Betrag gegen `expected.json`. Statt die Achsen dorthin zu
    schleppen, vergleicht es gegen diese Menge.
    """
    return {de(value, scale, decimals, g, m) for g in GROUPS for m in MINUS}
