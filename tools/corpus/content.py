import datetime as dt
import random
import sys
from pathlib import Path

import money
import sizes
import vocab

sys.path[:0] = [str(Path(__file__).resolve().parent.parent)]
import units  # noqa: E402


CONSONANTS = "bcdfghjklmnpqrstvwxyz"
VOWELS = "aeiou"


def opaque_name(rng):
    style = rng.random()
    if style < 0.22:
        return " ".join(str(rng.randint(100, 999999)) for _ in range(rng.randint(1, 3)))
    if style < 0.4:
        return f"{rng.randint(1000, 99999)}-{rng.randint(10, 999)}"
    if style < 0.55:
        letters = "".join(rng.choice(CONSONANTS.upper()) for _ in range(rng.randint(2, 4)))
        return f"{letters}{rng.randint(100, 99999)}{rng.choice(['', '/A', '-02', 'RT', '.5'])}"
    if style < 0.7:
        word = "".join(rng.choice(CONSONANTS) + rng.choice(VOWELS) for _ in range(rng.randint(2, 5)))
        # Ohne Leerzeichen: `coverage.py` erkennt einen undurchsichtigen Namen daran,
        # dass **kein** Token rein alphabetisch und mindestens vier Zeichen lang ist.
        # Mit Leerzeichen wäre "BAKOLE 1234" nicht von einem Versalnamen zu trennen.
        return word.upper() + str(rng.randint(10, 9999))
    if style < 0.84:
        return " ".join("".join(rng.choice(CONSONANTS + VOWELS) for _ in range(rng.randint(2, 6)))
                        + str(rng.randint(1, 999))
                        for _ in range(rng.randint(1, 3))).upper()
    return rng.choice(["POS", "ART", "SON", "DIV", "NN", "X"]) + " " + \
        "".join(rng.choice("0123456789") for _ in range(rng.randint(4, 10)))


def abbreviate(rng, name):
    """Der stark abgekürzte Name, wie ihn eine Warenwirtschaft mit 24 Zeichen Feldbreite
    druckt: `Schw.Schn.nat.ausgel.`, `PUTENSCHN. PAN. 180G`.

    Die geklebte Form (ohne Leerzeichen zwischen den Abkürzungen) ist die schwierigere:
    die OCR liest dann *ein* langes Token, das keinem Wort gleicht.
    """
    parts = name.split()
    out = []
    for p in parts:
        if len(p) > 4 and rng.random() < 0.7:
            p = p[:rng.randint(3, 5)] + "."
        out.append(p)
    glued = rng.random() < 0.35
    text = ("".join(out) if glued else " ".join(out))
    return text.upper() if rng.random() < 0.45 else text


def wine_name(rng, cat, category):
    """Der Weinname, wie ein Weingut ihn druckt — **Jahrgang zuerst**.

        2024 Iphöfer Kronsberg Silvaner trocken 0,75 l
        2022 Domina trocken 0,75 l
        Sommer Sekt b.A. brut 0,75 l

    Fehlerbild 1 aus v12/PLAN.md. v11 druckte den Jahrgang als *Variante* am Ende
    ("Grüner Veltliner Ried Hochrain 2022") und kannte nur österreichische Rieden.
    Auf dem echten Weingut-Beleg stand er vorn, und das Modell hat ihn als Menge
    gelesen und die Bezeichnung erst ab der Lage begonnen. Eine vierstellige Zahl am
    Anfang der Bezeichnungsspalte **muss** im Korpus oft genug Teil des Namens sein.
    """
    vintage = rng.choice(vocab.WINE_VINTAGES)
    roll = rng.random()
    if roll < 0.18:
        # Schaumwein trägt selten einen Jahrgang: "Sommer Sekt b.A. brut 0,75 l".
        parts = []
        if rng.random() < 0.55:
            parts.append(rng.choice(vocab.SUPPLIER_NAMES))
        parts.append(rng.choice(vocab.SEKT_FORMS))
    elif roll < 0.62:
        # Lagenwein: Jahrgang, Einzellage, Rebsorte, Geschmack.
        parts = [rng.choice(vocab.WINE_SITES), rng.choice(vocab.WINE_GRAPES),
                 rng.choice(vocab.WINE_QUALITY)]
        if rng.random() < 0.18:
            parts.insert(0, rng.choice(vocab.WINE_REGIONS))
    else:
        # Gutswein: nur Rebsorte und Geschmack — "2022 Domina trocken".
        parts = [rng.choice(vocab.WINE_GRAPES), rng.choice(vocab.WINE_QUALITY)]
        if rng.random() < 0.22:
            parts.append(rng.choice(vocab.WINE_REGIONS))
    if rng.random() < 0.72:
        parts.append(sizes.phrase(rng, cat, category))
    if rng.random() < 0.10:
        parts.append(rng.choice(vocab.VOL_PERCENTS[:8]))
    # Der Jahrgang steht auf rund 40 % der Weinzeilen **vor** dem Namen, auf weiteren
    # 12 % hinten (die alte v11-Form bleibt im Korpus, sonst lernt das Modell nur den
    # Tausch statt beider Formen).
    place = rng.random()
    if place < 0.66:
        parts.insert(0, vintage)
    elif place < 0.86:
        parts.insert(min(1, len(parts)), vintage)
    return " ".join(p for p in parts if p)


def procedural_parts(rng, cat, category):
    """Die Bausteine, die *zusätzlich* zur Basis in den Namen dürfen.

    Marke, Güteklasse, Herkunft, Zuschnitt, Fett-/Alkoholgehalt, Farbe. Jeder davon
    macht den Namen länger und weniger memorierbar; zusammen mit `sizes.phrase`
    kommen aus 1162 Basen Zehntausende verschiedener gedruckter Namen.
    """
    lead, tail = [], []
    if rng.random() < 0.22:
        (lead if rng.random() < 0.6 else tail).append(rng.choice(vocab.BRANDS))
    if rng.random() < 0.75:
        v = rng.choice(cat["variants"])
        if v:
            tail.append(v)
    if rng.random() < 0.16:
        tail.append(rng.choice(vocab.GRADES))
    if rng.random() < 0.14:
        tail.append(rng.choice(vocab.ORIGINS))
    if category == "fisch" and rng.random() < 0.35:
        tail.append(rng.choice(vocab.FISH_CUTS))
    elif category in ("metzgerei", "gefluegel") and rng.random() < 0.35:
        tail.append(rng.choice(vocab.CUTS))
    if rng.random() < 0.10:
        # Alkoholgehalt nur, wo es einen gibt; sonst Fettgehalt.
        tail.append(rng.choice(vocab.VOL_PERCENTS
                               if category in ("wein", "spirituosen", "brauerei")
                               else vocab.FAT_PERCENTS))
    if category in ("nonfood", "verpackung_einweg", "waesche_service", "blumen_deko",
                    "gastrobedarf_technik") and rng.random() < 0.25:
        tail.append(rng.choice(vocab.COLOURS))
    if category in ("feinkost", "wein", "spirituosen", "kaffee") and rng.random() < 0.20:
        tail.append(rng.choice(vocab.FOREIGN_TAILS))
    return lead, tail


def article_name(rng, cat, opaque_share=0.0, category=None):
    """Der gedruckte Artikelname.

    v11 setzte ihn aus Basis + einem Variantenwort + Größe zusammen; bei 136 Basen war
    der ganze Namensraum kleiner als das, was ein Modell auswendig lernt. v12 zieht aus
    1162 Basen, hängt prozedurale Bausteine an und würfelt danach eine **Schreibform**:
    VERSALIEN, abgekürzt (`Schw.Schn.nat.ausgel.`), zahlenvoran (`12er Tray Cola 0,33`)
    oder lang genug für zwei bis drei gedruckte Zeilen.
    """
    if rng.random() < opaque_share:
        return opaque_name(rng)
    if category == "wein" and rng.random() < 0.85:
        return wine_name(rng, cat, category)
    lead, tail = procedural_parts(rng, cat, category)
    parts = lead + [rng.choice(cat["bases"])] + tail
    if rng.random() < 0.7:
        parts.append(sizes.phrase(rng, cat, category))
    style = rng.random()
    if style < 0.06:
        # Zahl zuerst: "12er Tray Cola 0,33". Die Bezeichnungszelle beginnt mit einer
        # Zahl — genau das, was der Tagger als Menge liest.
        parts.insert(0, rng.choice(vocab.LEADING_PACKS))
    elif style < 0.14:
        # Langer Name, der im Satz über zwei bis drei Zeilen umbricht.
        parts.append(rng.choice(vocab.ORIGINS))
        parts.append(sizes.phrase(rng, cat, category))
        if rng.random() < 0.5:
            parts.append(rng.choice(vocab.GRADES))
    name = " ".join(p for p in parts if p)
    style = rng.random()
    if style < 0.12:
        return abbreviate(rng, name)
    if style < 0.20:
        return name.upper()
    return name


# Jahrgangscodierte Artikelnummern: `2024-S-01`, `24-R-003`, `2023/17`, `SEKT-01`.
# Fehlerbild 1: auf dem echten Weingut-Beleg steht in der Artikelspalte eine Zahl, die
# aussieht wie der Jahrgang im Namen daneben — und der Tagger hat beide verwechselt.
# Global rund 4 % der Positionen, bei Wein und Spirituosen über 30 %.
YEAR_ID_LETTERS = ["S", "R", "W", "T", "B", "G", "RW", "WW", "SE", "SP"]
YEAR_ID_WORDS = ["SEKT", "WEIN", "ROT", "WEISS", "ROSE", "BRAND", "GIN", "RUM",
                 "WHISKY", "LIKOER", "VELT", "RIES"]


def year_article_id(rng, year=None):
    y = year or rng.choice([int(v) for v in vocab.WINE_VINTAGES])
    style = rng.random()
    if style < 0.24:
        return f"{y}-{rng.choice(YEAR_ID_LETTERS)}-{rng.randint(1, 99):02d}"
    if style < 0.44:
        return f"{y % 100:02d}-{rng.choice(YEAR_ID_LETTERS)}-{rng.randint(1, 999):03d}"
    if style < 0.60:
        return f"{y}/{rng.randint(1, 99)}"
    if style < 0.72:
        return f"{rng.choice(YEAR_ID_WORDS)}-{rng.randint(1, 99):02d}"
    if style < 0.84:
        return f"{y}{rng.randint(100, 999)}"
    if style < 0.94:
        return f"{rng.choice(YEAR_ID_LETTERS)}{y % 100:02d}{rng.randint(1, 99):02d}"
    return f"{y}"


def article_id(rng, year_share=0.0):
    if year_share and rng.random() < year_share:
        return year_article_id(rng)
    style = rng.random()
    if style < 0.28:
        return str(rng.randint(100, 9999))
    if style < 0.5:
        return f"{rng.randint(1, 99):02d}-{rng.randint(1000, 9999)}"
    if style < 0.68:
        return f"A{rng.randint(10000, 999999)}"
    if style < 0.82:
        return f"{rng.randint(100000, 999999)}"
    if style < 0.94:
        # Kleingeschriebene Buchstabensuppe mit angehängter Zahl, wie sie
        # Webshops führen: "dnpdsrx1", "rx11015". Ohne Trennzeichen, also liest
        # die OCR ein Wort, das weder Name noch Zahl ist.
        letters = "".join(rng.choice("abcdefghijklmnprstuvwxyz")
                          for _ in range(rng.randint(2, 6)))
        return letters + str(rng.randint(1, 99999))
    return f"{rng.choice('KLMSTZ')}{rng.randint(100, 999)}.{rng.randint(10, 99)}"


def gtin(rng):
    return f"{rng.randint(4000000000000, 9999999999999)}"


NUMERIC_ID_CATEGORIES = ("baeckerei", "metzgerei", "konditorei", "molkerei")


def numeric_article_id(rng):
    """Eine rein numerische, drei- bis vierstellige Artikelnummer.

    Der Gegenbeleg, den v10 nicht hatte: auf einer Bäckereirechnung bekamen `101`
    und `108` die Klasse `quantity` mit 0,44 Konfidenz und gewannen gegen die echte
    Menge, weil sie links davon stehen (v10/REPORT.md, Lücke 5). Eine kleine nackte
    Ganzzahl in einer Positionszeile *muss* im Korpus oft genug eine Artikelnummer
    sein, sonst ist sie für das Modell immer eine Menge.
    """
    return str(rng.randint(100, 9999))


def invoice_number(rng, date, seq):
    y = date.year
    style = rng.randint(0, 6)
    return [
        f"RE-{y}{seq:05d}",
        f"RG {seq:06d}",
        f"{y}/{seq:04d}",
        f"AR{y % 100}{seq:05d}",
        f"{seq:08d}",
        f"F-{y}-{seq:04d}",
        f"RE{y % 100}{seq:04d}/{rng.randint(1, 9)}",
    ][style]


def company_name(rng, category):
    """Firmenname im Stil eines Lieferanten. Auch der Kunde zieht daraus: eine
    Rechnung geht selten an "Gasthaus Zur Alten Post", sondern meist an eine GmbH,
    die genauso heißt wie der Absender — und genau diese Verwechslung kostete den
    Kopf bisher den `supplier`."""
    roll = rng.random()
    if roll < 0.30:
        # Familienbetrieb: `Metzgerei Hofmann`, `Weingut Zehentner`, `Bäckerei Krenn
        # GmbH`. Die häufigste Form auf den echten Belegen und im Korpus bis v11 gar
        # nicht vorhanden — dort hiess jeder Lieferant "<Kopfwort> <Nachname> <GmbH>".
        head = rng.choice(vocab.TRADE_HEADS.get(category, ["Handelshaus"]))
        return " ".join(x for x in [
            head, rng.choice(vocab.SUPPLIER_NAMES),
            rng.choice(vocab.SUPPLIER_TAILS) if rng.random() < 0.45 else "",
        ] if x)
    if roll < 0.40:
        # Zwei Nachnamen: `Gebr. Pucher & Krenn OHG`, `Huemer & Ortner GmbH & Co. KG`.
        a, b = rng.sample(vocab.SUPPLIER_NAMES, 2)
        lead = "Gebr. " if rng.random() < 0.3 else ""
        return f"{lead}{a} & {b} {rng.choice(vocab.SUPPLIER_TAILS)}"
    return " ".join(x for x in [
        rng.choice(vocab.SUPPLIER_HEADS) if rng.random() < 0.45 else "",
        rng.choice(vocab.SUPPLIER_NAMES),
        vocab.TRADES[category] if rng.random() < 0.4 else "",
        rng.choice(vocab.SUPPLIER_TAILS),
    ] if x)


def long_company_name(rng, category):
    """Vier bis sechs Wörter, die über zwei Zeilen umbrechen — „Brückner Spirituosen &
    Barbedarf GmbH".

    Diese Form steht in E-Rechnungsausdrucken hinter einem Schlüssel (`Firmenname:`,
    `Lieferant:`), und das Modell hat sie in v10 nie gesehen: dort war der Lieferant
    entweder eine Absenderzeile oder eine Wortmarke, nie ein beschrifteter Wert. Jedes
    Wort davon ist `supplier`, auch das `&`.
    """
    a, b = rng.sample(vocab.TRADE_WORDS, 2)
    if rng.random() < 0.25:
        return " ".join([rng.choice(vocab.TRADE_HEADS.get(category, ["Handelshaus"])),
                         rng.choice(vocab.SUPPLIER_NAMES), "Inh.",
                         rng.choice(vocab.FIRST_NAMES), rng.choice(vocab.SUPPLIER_NAMES)])
    middle = rng.choice([f"{a} & {b}", f"{a} und {b}", f"{a}-{b}", a, f"{a} {b}",
                         f"{a} & {b}"])
    return " ".join(x for x in [
        rng.choice(vocab.SUPPLIER_NAMES),
        middle,
        rng.choice(vocab.SUPPLIER_TAILS),
    ] if x)


def person(rng):
    return f"{rng.choice(vocab.FIRST_NAMES)} {rng.choice(vocab.SUPPLIER_NAMES)}"


# Wörter, die auch in einer Title-Case-Zeile klein bleiben: ein Briefkopf, der
# "Ihr Partner Für Gastronomie Und Handel" setzt, lässt die Trenner stehen.
LOWER_KEEP = {"·", "|", "—", "-", "&", "/"}


def slogan(rng, category, year):
    """Der gemischt gesetzte Werbesatz des Briefkopfs — `O`, kein Lieferantenname.

    Gezogen wird aus dem fachspezifischen Topf (`vocab.SLOGAN_TRADE`, doppelt
    gewichtet, weil genau diese Sätze — `Fleisch und Wurst aus eigener Schlachtung` —
    das Fehlerbild tragen) und aus dem allgemeinen (`vocab.SLOGANS`). Danach zwei
    Schreibvarianten: in ~12 % Title Case, wie sie Wortmarken setzen, und in ~18 %
    ein abschliessender Punkt. Beides ändert nur die gedruckten Tokens; die Klasse
    bleibt `O`.
    """
    trade = vocab.SLOGAN_TRADE.get(category, [])
    text = rng.choice(trade + trade + vocab.SLOGANS)
    text = text.format(trade=vocab.TRADES[category], year=rng.randint(1890, 1995))
    if rng.random() < 0.12:
        text = " ".join(w if w in LOWER_KEEP else w[:1].upper() + w[1:]
                        for w in text.split())
    if rng.random() < 0.18 and not text.endswith("."):
        text += "."
    return text


def customer_name(rng, categories):
    """Der `buyer`. In 40 % eine Firma aus demselben Topf wie der Lieferant — genau die
    Verwechslung, die den `supplier` kostet —, sonst ein Gastrobetrieb, und in gut 8 %
    eine Privatperson (B2C und Kassenbon)."""
    roll = rng.random()
    if roll < 0.40:
        return company_name(rng, rng.choice(categories))
    if roll < 0.48:
        return f"{rng.choice(vocab.PRIVATE_CUSTOMERS)} {person(rng)}"
    if roll < 0.54:
        return long_company_name(rng, rng.choice(categories))
    return rng.choice(vocab.CUSTOMERS)


def customer(rng, sup):
    """Der Rechnungsempfänger — Name (`buyer`), Anschrift (PLZ `postcode`), Kundennummer.

    Der Name darf nie Wort für Wort der Lieferantenname sein: zwei identische Läufe auf
    einer Seite, einmal `supplier` und einmal `buyer`, sind kein Kontextsignal, sondern
    Rauschen. `validate.py` prüft es, hier wird es verhindert.
    """
    categories = sorted(vocab.CATEGORIES)
    name = customer_name(rng, categories)
    for _ in range(4):
        if name.casefold() != sup["name"].casefold():
            break
        name = customer_name(rng, categories)
    pool = vocab.CUSTOMER_CITIES if rng.random() < 0.55 else vocab.CITIES
    zipc, city = rng.choice(pool)
    return {
        "name": name,
        "street": f"{rng.choice(vocab.STREETS)} {rng.randint(1, 90)}",
        "zip": zipc,
        "city": city,
        "number": rng.choice([f"{rng.randint(10000, 999999)}",
                              f"{rng.randint(1000, 99999)}",
                              f"K{rng.randint(10000, 99999)}",
                              f"D-{rng.randint(1000, 9999)}"]),
    }


def swiss_supplier(rng, name, category):
    """Ein Schweizer Lieferant: CHE-Nummer, CH-IBAN, +41, vierstellige PLZ.

    Die Familie `swiss` rechnet in CHF, hatte bis jetzt aber einen österreichischen
    Absender mit ATU-Nummer über dem Schweizer Zahlteil. Der Anteil ist klein (5 % der
    Rechnungen), aber jede Klasse darin — `taxId`, `bankId`, `postcode`, `phone` —
    sieht in der Schweiz anders aus, und genau das soll das Modell aushalten.
    """
    zipc, city = rng.choice(vocab.CH_CITIES)
    slug = "".join(c for c in name.split()[0].lower() if c.isalpha())
    return {
        "name": name,
        "long_name": name if len(name.split()) >= 4 else long_company_name(rng, category),
        "street": f"{rng.choice(vocab.CH_STREETS)} {rng.randint(1, 140)}",
        "zip": zipc,
        "city": city,
        "country": "CH",
        "vatId": (f"CHE-{rng.randint(100, 999)}.{rng.randint(100, 999)}."
                  f"{rng.randint(100, 999)} MWST"),
        "vatId2": "",
        "phone": f"+41 {rng.randint(21, 91)} {rng.randint(100, 999)} {rng.randint(10, 99)} "
                 f"{rng.randint(10, 99)}",
        "fax": f"+41 {rng.randint(21, 91)} {rng.randint(100, 999)} {rng.randint(10, 99)} "
               f"{rng.randint(10, 99)}",
        "mail": f"info@{slug}.ch",
        "web": f"www.{slug}.ch",
        "taxNumber": f"CHE-{rng.randint(100, 999)}.{rng.randint(100, 999)}.{rng.randint(100, 999)}",
        "bank": f"{rng.choice(vocab.CH_BANKS)} {city}",
        "iban": f"CH{rng.randint(10, 99)} {rng.randint(1000, 9999)} {rng.randint(1000, 9999)} "
                f"{rng.randint(1000, 9999)} {rng.randint(1000, 9999)} {rng.randint(1, 9)}",
        "bic": "".join(rng.choice("ABCDEFGHIKLMNOPRSTUVWZ") for _ in range(4)) + "CHZZ80A",
        "register": f"CH-{rng.randint(100, 999)}.{rng.randint(1, 9)}.{rng.randint(100, 999)}.{rng.randint(100, 999)}-{rng.randint(1, 9)}",
    }


def supplier(rng, category):
    name = long_company_name(rng, category) if rng.random() < 0.22 else company_name(rng, category)
    # 5 % Schweizer Absender — so oft, wie die Familie `swiss` gezogen wird.
    if rng.random() < 0.05:
        return swiss_supplier(rng, name, category)
    zipc, city = rng.choice(vocab.CITIES)
    at = len(zipc) == 4
    return {
        "name": name,
        # Der lange, beschriftete Name für die XRechnung-Form (`Firmenname: …`). Er ist
        # derselbe Lieferant, nur in der Schreibweise, die ein E-Rechnungsausdruck führt.
        "long_name": name if len(name.split()) >= 4 else long_company_name(rng, category),
        "street": f"{rng.choice(vocab.STREETS)} {rng.randint(1, 180)}{rng.choice(['', '', '', 'a', 'b', '/2'])}",
        "zip": zipc,
        "city": city,
        "country": "AT" if at else "DE",
        # Die Schweizer Form kommt auf Rechnungen österreichischer und deutscher Händler
        # als *zweite* UID vor (Leistungsort Schweiz). Sie ist `taxId` wie jede andere.
        "vatId": (f"ATU{rng.randint(10000000, 99999999)}" if at
                  else f"DE{rng.randint(100000000, 999999999)}"),
        "vatId2": (f"CHE-{rng.randint(100, 999)}.{rng.randint(100, 999)}."
                   f"{rng.randint(100, 999)} MWST" if rng.random() < 0.12 else ""),
        "phone": f"+{'43' if at else '49'} {rng.randint(100, 999)} {rng.randint(100000, 9999999)}",
        "fax": f"+{'43' if at else '49'} {rng.randint(100, 999)} {rng.randint(100000, 9999999)}-{rng.randint(10, 99)}",
        "mail": "office@" + "".join(c for c in name.split()[0].lower() if c.isalpha()) + (".at" if at else ".de"),
        "web": "www." + "".join(c for c in name.split()[0].lower() if c.isalpha()) + (".at" if at else ".de"),
        "taxNumber": (f"{rng.randint(10, 99)}-{rng.randint(100, 999)}/{rng.randint(1000, 9999)}" if at
                      else f"{rng.randint(10, 99)}/{rng.randint(100, 999)}/{rng.randint(10000, 99999)}"),
        "bank": f"{rng.choice(vocab.BANKS)} {city}",
        "iban": ("AT" if at else "DE") + f"{rng.randint(10, 99)} {rng.randint(1000, 9999)} "
                f"{rng.randint(1000, 9999)} {rng.randint(1000, 9999)} {rng.randint(1000, 9999)}",
        "bic": "".join(rng.choice("ABCDEFGHIKLMNOPRSTUVWZ") for _ in range(4)) + ("ATWW" if at else "DEFF"),
        "register": f"FN {rng.randint(10000, 999999)}{rng.choice('abdfghikmnpstvwxyz')}" if at
                    else f"HRB {rng.randint(1000, 99999)}",
    }


def price_base(rng, unit_code, unit_text):
    """Preiseinheit: (Menge, Tokens). Die Zahl ist `priceBasis`, das Einheitenwort `unit`.

    Die Zelle ist seit v11 **nie leer** — eine Preisbasisspalte, die nur bei Kilo- und
    Literware etwas druckt, lehrt das Modell, dass „1" dort nichts bedeutet. Gedruckt
    wird `1 Stk`, `100 g`, `je 100`, `1 XBO`, `pro 1`.

    Die eine harte Regel: auf einer Zeile ohne gedruckten Einheitentext (nackte Menge,
    `unitCode H87`) darf **kein** `unit`-Wort in der Wahrheit stehen — dort druckt die
    Zelle nur die Zahl. `validate.py` prüft das.
    """
    lead = rng.choice(["", "", "", "je", "pro", "per"])
    if unit_code in ("KGM", "LTR") and rng.random() < 0.25:
        number, word = "100", ("g" if unit_code == "KGM" else "ml")
        qty = 100 * money.QTY
    elif rng.random() < 0.08:
        number, word, qty = "10", (unit_text or ""), 10 * money.QTY
    else:
        number, word, qty = "1", (unit_text or ""), money.QTY
    # `/kg`, `/Fl`, `/100 g`: der Schrägstrich statt „je". Ohne Zahl davor gibt es auf
    # dieser Zeile keine `priceBasis` — die Zelle druckt dann nur die Einheit, und
    # genau so steht es in der Preisspaltenkopfzeile echter Belege ("EP/kg").
    if word and number == "1" and rng.random() < 0.14:
        return qty, [("/", "O"), (word, "unit", 0, True)]
    tokens = ([(lead, "O")] if lead else []) + [(number, "priceBasis")]
    if word:
        tokens.append((word, "unit"))
    return qty, tokens


GROUPS = {
    "baeckerei": ["Kleingebäck", "Brote", "Feine Backwaren", "Tiefkühl"],
    "metzgerei": ["Rind", "Schwein", "Geflügel", "Wurstwaren"],
    "molkerei": ["Frischmilch", "Käse", "Joghurt & Desserts", "Butter & Fette"],
    "gemuese": ["Gemüse", "Salate", "Obst", "Kräuter"],
    "getraenke": ["Alkoholfrei", "Bier", "Sirup & Postmix", "Leergut"],
    "wein": ["Weißwein", "Rotwein", "Schaumwein"],
    "spirituosen": ["Weiße Spirituosen", "Braune Spirituosen", "Liköre"],
    "kaffee": ["Kaffee", "Tee", "Schokolade"],
    "gefluegel": ["Hähnchen", "Pute", "Ente & Gans", "Geflügelwurst"],
    "fisch": ["Süßwasserfisch", "Seefisch", "Räucherware", "Krusten- & Schalentiere"],
    "obst": ["Kernobst", "Steinobst", "Beeren", "Südfrüchte", "Zitrusfrüchte"],
    "feinkost": ["Salumi", "Käse", "Antipasti", "Öle & Essige", "Pasta & Reis"],
    "tiefkuehl": ["Kartoffelprodukte", "Gemüse TK", "Backwaren TK", "Fertiggerichte",
                  "Desserts TK"],
    "eis_dessert": ["Speiseeis", "Sorbets", "Desserts", "Torten"],
    "suesswaren": ["Schokolade", "Zuckerwaren", "Gebäck", "Knabberartikel", "Nüsse"],
    "tee_gewuerze": ["Tee", "Gewürze", "Kräuter", "Fonds & Brühen"],
    "saefte_alkoholfrei": ["Direktsäfte", "Nektare", "Shots & Smoothies", "Schorlen"],
    "brauerei": ["Fassbier", "Flaschenbier", "Alkoholfrei", "Craft"],
    "reinigung_hygiene": ["Spülen", "Flächen", "Sanitär", "Desinfektion", "Wäsche"],
    "verpackung_einweg": ["Menüverpackung", "Becher", "Folien & Papiere", "Besteck",
                          "Etiketten"],
    "gastrobedarf_technik": ["Kochgeschirr", "Geschirr", "Gläser", "Besteck",
                             "Küchentechnik"],
    "waesche_service": ["Tischwäsche", "Berufskleidung", "Frottee", "Serviceleistung"],
    "blumen_deko": ["Schnittblumen", "Pflanzen", "Deko", "Kerzen"],
    "bio_hof": ["Bio-Frischware", "Bio-Trockensortiment", "Bio-Molkerei", "Bio-Getränke"],
    "catering": ["Buffet", "Platten", "Warme Küche", "Personal", "Logistik"],
    "tabak": ["Zigaretten", "Feinschnitt", "Zigarren", "Zubehör"],
    "nonfood": ["Hygiene", "Reinigung", "Verpackung"],
    "cc": ["Tiefkühl", "Konserven", "Öle & Fette", "Trockensortiment"],
}


def detail(rng):
    """Die Zeile unter der Position: Charge, MHD, Seriennummer — und manchmal eine
    Lieferschein-Nummer mit Datum.

    Liefert (Text, Klasse)-Paare statt eines Strings: die Lieferscheinnummer darin ist
    `deliveryNoteNumber` und das Datum `deliveryDate`, alles andere `O`. Als eine
    einzige `O`-Zeile war sie bis v10 eine Ziffernfolge ohne Bedeutung.
    """
    if rng.random() > 0.35:
        return []
    date = dt.date(2025, 1, 1) + dt.timedelta(days=rng.randint(0, 729))
    pick = rng.random()
    if pick < 0.12:
        return [("Lieferschein", "O"), (f"{rng.randint(10000, 99999)}", "deliveryNoteNumber"),
                ("vom", "O"), (date.strftime("%d.%m.%Y"), "deliveryDate")]
    if pick < 0.18:
        return [("LS-Nr.", "O"), (f"{rng.randint(100000, 999999)}", "deliveryNoteNumber")]
    text = rng.choice([
        f"Charge {rng.randint(100000, 999999)}",
        f"MHD {rng.randint(1, 12):02d}/{rng.randint(25, 27)}",
        f"Herkunft {rng.choice(['AT', 'DE', 'IT', 'ES', 'NL', 'FR'])}",
        f"Charge {rng.randint(1000, 9999)} / MHD {rng.randint(1, 12):02d}.{rng.randint(2025, 2027)}",
        # Seriennummer: ein langer Code in der Zeile unter dem Artikel, der wie
        # eine Artikelnummer aussieht und keine ist.
        f"Seriennr: {rng.choice('ABCDEFGH')}{rng.choice('ABCDEFGH')}{rng.randint(1, 9)}"
        f"{rng.choice('ABCDEFGH')}{rng.randint(10000000, 99999999)}",
        f"Serien-Nr. {rng.randint(100000000, 999999999)}",
        f"Größe {rng.choice(['S', 'M', 'L', 'XL', '38', '42', '46'])}, "
        f"Farbe {rng.choice(vocab.COLOURS)}",
        f"Farbe: {rng.choice(vocab.COLOURS)}",
    ])
    # Keine EAN in der Detailzeile: sie wäre eine *zweite*, andere GTIN für dieselbe
    # Position, und die Wahrheit führt je Zeile genau eine.
    return [(text, "O")]


def variant(rng, category):
    """Größen-/Variantenspalte zwischen Artikelnummer und Bezeichnung. `O`."""
    return rng.choice([
        rng.choice(["S", "M", "L", "XL", "XXL"]),
        str(rng.choice([36, 38, 40, 42, 44, 46, 48])),
        rng.choice(["0,33 l", "0,5 l", "0,75 l", "1 l", "5 kg", "10 kg", "250 g"]),
        rng.choice(vocab.COLOURS),
        rng.choice(["6er", "12er", "24er", "Einzel", "Gebinde"]),
    ])


def info_row(rng, date):
    """Eine Zeile mitten in der Positionstabelle, die keine Position ist.

    Bis v10 war sie ganz `O`. Sie trägt aber genau die Werte, für die es jetzt Klassen
    gibt: eine Bestellnummer, eine Lieferscheinnummer, ein Lieferdatum — mitten im
    Raster der Positionstabelle, also an der Stelle, an der das Modell am ehesten eine
    Position vermutet.
    """
    day = date.strftime("%d.%m.%Y")
    return rng.choice([
        [("Shopbestellung:", "O"), (f"{rng.randint(100000, 999999)}", "orderNumber"),
         ("/", "O"), (day, "orderDate"), ("/", "O"), (str(rng.randint(1, 4)), "O")],
        [("Ihre Bestellung", "O"), (f"{rng.randint(10000, 999999)}", "orderNumber"),
         ("vom", "O"), (day, "orderDate")],
        [("Lieferschein", "O"), (f"{rng.randint(10000, 99999)}", "deliveryNoteNumber"),
         ("vom", "O"), (day, "deliveryDate")],
        [("Auftrag", "O"), (f"{rng.randint(100000, 999999)}", "orderNumber"),
         ("/ Kommission", "O"), (f"{rng.choice('ABCDEFG')}{rng.randint(100, 999)}", "O")],
        [("Sendung", "O"), (f"{rng.randint(10000000, 99999999)}", "O"), ("/", "O"),
         (day, "deliveryDate")],
        [("Lieferdatum", "otherLabel"), (day, "deliveryDate"), ("·", "O"),
         ("Lieferschein-Nr.", "otherLabel"),
         (f"LS-{rng.randint(10000, 999999)}", "deliveryNoteNumber")],
    ])


def sample_lines(rng, category, count, vat_rates, with_discounts, opaque_share,
                 sign=1, bare_units=False, with_variants=False, code_units=False,
                 year_ids=0.0):
    cat = vocab.CATEGORIES[category]
    # Steuerschlüssel und Warengruppe sind die beiden unbeschrifteten Codes, die
    # neben Preis und Artikelnummer stehen und wie eine Zahl von uns aussehen.
    scheme = rng.choice([["A", "B", "C"], ["1", "2", "3"], ["N", "E", "H"], ["V", "R", "S"]])
    tax_codes = {r: scheme[i % len(scheme)] for i, r in enumerate(sorted(set(vat_rates)))}
    wg_codes = {g: f"{rng.randint(1, 99):02d}" for g in GROUPS.get(category, ["Sortiment"])}
    lines = []
    for no in range(1, count + 1):
        unit_text, pack = rng.choice(cat["units"])
        unit_code = units.code(unit_text)
        # Fehlerbild 6: die Menge ist die unzuverlässigste der zwölf Klassen. v11 zog
        # aus achtzehn kleinen Ganzzahlen; die echten Belege drucken `5,450`,
        # `13,760 kg`, `57 Stk`, `1.200 Stk` und `68` nackt. Drei Ergänzungen:
        # Kilo/Liter bekommen häufiger drei Nachkommastellen, es gibt vierstellige
        # Mengen mit Tausendertrennung (ein Punkt, der **kein** Komma ist), und die
        # kleinen Zahlen bleiben dominant, weil sie es in echt auch sind.
        big = rng.random() < 0.07
        quantity = (rng.choice([1000, 1200, 1500, 1800, 2000, 2400, 3000, 5000, 1100, 1250])
                    if big else
                    rng.choice([1, 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 17, 20, 24, 30, 36,
                                48, 57, 60, 68, 72, 96, 100, 120, 144, 200, 240]))
        if unit_code in ("KGM", "LTR") and rng.random() < 0.72:
            quantity = quantity * money.QTY + rng.choice(
                [0, 50, 100, 120, 250, 330, 400, 450, 500, 640, 750, 760, 890]) * (money.QTY // 1000)
        else:
            quantity *= money.QTY
        # Nackte Menge ohne jede Einheit: auf echten Rechnungen 14,5 % der Zeilen.
        # Nur bei Stückware — Kilo und Liter drucken ihr Maß immer.
        if bare_units and units.TABLE[unit_code]["base"] == "piece":
            unit_text, unit_code = None, "H87"
        # E-Rechnungsausdruck: die Einheitenspalte druckt den UN/ECE-Code selbst. Der
        # gedruckte Text *ist* dann der Code, also bleibt die Wahrheit in sich stimmig
        # (unitText "XBO", unitCode "XBO"). v10 kannte nur deutsche Einheitenwörter und
        # hat dem Modell beigebracht, dass ein Großbuchstabencode keine Einheit ist.
        elif code_units and unit_text:
            unit_text = unit_code
        lo, hi = cat["price"]
        unit_price = rng.randint(lo, hi) * (money.PRICE // money.CENT)
        # Vierstellige Mengen gibt es nur bei billiger Ware. Ohne die Kappung druckt
        # der Korpus Positionsbeträge von 2,4 Mio € — eine Rechnung, die es nicht gibt,
        # und der Tagger lernt daraus die falsche Größenordnung für `lineNet`.
        if quantity >= 1000 * money.QTY:
            unit_price = min(unit_price, rng.randint(15, 850) * (money.PRICE // money.CENT))
        base_qty, base_text = price_base(rng, unit_code, unit_text)
        if base_qty > money.QTY:
            unit_price = max(money.PRICE // 100, unit_price * base_qty // (money.QTY * 4))
        unit_price *= sign
        vat = rng.choice(vat_rates)
        net = money.line_net(quantity, unit_price, base_qty)
        lines.append({
            "no": no,
            "name": article_name(rng, cat, opaque_share, category),
            # Nackte Menge und rein numerische Artikelnummer gehören zusammen: genau
            # dieses Paar hat in v10 die Menge gekostet (Lücke 5). Auf Rechnungen mit
            # nackten Mengen ist die Artikelnummer deshalb in der Hälfte der Fälle eine
            # blanke drei- bis vierstellige Zahl.
            # v12.1: nicht nur bei nackten Mengen. Bäckerei, Metzgerei und Konditorei
            # drucken auf den echten Belegen fast immer eine blanke 3-4-stellige
            # Artikelnummer VOR dem Namen (101 Brötchen … 137 Stk) — v12 hat 101 als
            # Menge gelesen, weil das Muster im Korpus verdünnt war.
            "sellerArticleId": (numeric_article_id(rng)
                                if rng.random() < (0.6 if category in NUMERIC_ID_CATEGORIES
                                                   else 0.5 if bare_units else 0.15)
                                else article_id(rng, year_ids)) if rng.random() < 0.85 else None,
            # Die GTIN wird jetzt viel öfter gezogen als sie gedruckt wird: die Spalte
            # gibt es nur in einem Teil der Vorlagen, und `layout.fit` streicht sie,
            # sobald keine Zeile eine GTIN führt.
            "gtin": gtin(rng) if rng.random() < 0.55 else None,
            "quantity": quantity,
            "unitText": unit_text,
            "unitCode": unit_code,
            "unitPrice": unit_price,
            "priceBaseQty": base_qty,
            "priceBaseText": base_text,
            "lineNet": net,
            "vat": vat,
            "discount": rng.choice([300, 500, 1000, 250, 150]) if with_discounts and rng.random() < 0.45 else 0,
            "discountAmount": 0,
            "pack": pack,
            "group": rng.choice(GROUPS.get(category, ["Sortiment"])),
            "detail": detail(rng),
            "variant": variant(rng, category) if with_variants and rng.random() < 0.8 else None,
            "free": False,
        })
        lines[-1]["taxCode"] = tax_codes[vat]
        lines[-1]["wg"] = wg_codes[lines[-1]["group"]]
    return lines


def extra_lines(rng, lines, category, vat_rates, kind, free_count, deposit_count):
    """Zeilen, die keine Ware sind.

    *Freizeilen* (Beigaben, Dank, Flyer) haben Menge 1 und drucken Preis und
    Betrag als **leere Zelle** — nicht als "0,00". In `expected.json` stehen sie
    mit `unitPrice` 0 und `lineNet` 0, ohne Einheitentext, also mit H87.

    *Pfand- und Leergutzeilen* haben einen negativen Preis und damit einen
    negativen Positionsbetrag; sie ziehen die Summe herunter.
    """
    proto = lines[0] if lines else None
    out = []
    for _ in range(free_count):
        out.append({
            "no": 0, "name": rng.choice(vocab.FREE_ITEMS), "sellerArticleId":
            article_id(rng) if rng.random() < 0.3 else None, "gtin": None,
            "quantity": money.QTY, "unitText": None, "unitCode": "H87",
            # Freizeile: leere Preis- *und* leere Preisbasiszelle.
            "unitPrice": 0, "priceBaseQty": money.QTY, "priceBaseText": [],
            "lineNet": 0, "vat": vat_rates[0], "discount": 0, "discountAmount": 0,
            "pack": 1,
            "group": proto["group"] if proto else "Sortiment", "detail": [],
            "variant": None, "free": True,
            "taxCode": proto["taxCode"] if proto else "A",
            "wg": proto["wg"] if proto else "01",
        })
    for _ in range(deposit_count):
        unit_text = rng.choice(["Stk", "Kiste", "Kt", None])
        unit_code = units.code(unit_text) if unit_text else "H87"
        quantity = rng.choice([2, 4, 6, 10, 12, 20, 24]) * money.QTY
        unit_price = -rng.choice([9, 15, 25, 33, 50, 150, 300]) * (money.PRICE // money.CENT)
        net = money.line_net(quantity, unit_price, money.QTY)
        out.append({
            "no": 0, "name": rng.choice(vocab.DEPOSIT_ITEMS), "sellerArticleId":
            article_id(rng) if rng.random() < 0.6 else None, "gtin": None,
            "quantity": quantity, "unitText": unit_text, "unitCode": unit_code,
            "unitPrice": unit_price, "priceBaseQty": money.QTY,
            "priceBaseText": [("1", "priceBasis")] + ([(unit_text, "unit")] if unit_text else []),
            "lineNet": net, "vat": vat_rates[-1], "discount": 0, "discountAmount": 0,
            "pack": 1,
            "group": proto["group"] if proto else "Leergut", "detail": [],
            "variant": None, "free": False,
            "taxCode": proto["taxCode"] if proto else "A",
            "wg": proto["wg"] if proto else "01",
        })
    if kind == "delivery_note":
        for l in out:
            l["unitPrice"] = l["lineNet"] = 0
    rng.shuffle(out)
    merged = lines + out if rng.random() < 0.7 else out + lines
    for i, line in enumerate(merged, 1):
        line["no"] = i
    return merged


CHARGE_KINDS = ["shipping", "shipping", "shipping", "freight", "packing", "deposit",
                "discount", "rounding", "minimum"]


def charges(rng, breakdown, count):
    """Zuschlags- und Abschlagszeilen im Summenblock.

    Sie stehen in keiner Position, gehen aber in den Nettobetrag ein. Genau
    deshalb geht auf echten Rechnungen die Summe der Positionen nicht auf den
    Nettobetrag auf, und ein Modell, das das nie gesehen hat, rechnet falsch.
    """
    out = []
    base = breakdown[-1]["vat"] if breakdown else 2000
    for kind in rng.sample(CHARGE_KINDS, k=min(count, len(set(CHARGE_KINDS)))):
        if kind == "rounding":
            amount = rng.choice([-4, -3, -2, -1, 1, 2, 3, 4])
        elif kind == "discount":
            amount = -rng.randint(150, 4500)
        elif kind == "deposit":
            amount = rng.choice([1, -1]) * rng.randint(150, 2400)
        elif kind == "freight":
            amount = rng.randint(900, 8500)
        elif kind == "minimum":
            amount = rng.randint(500, 2000)
        elif kind == "packing":
            amount = rng.randint(150, 1200)
        else:
            amount = rng.choice([390, 495, 590, 690, 790, 895, 990, 1490])
        out.append({"kind": kind, "amount": amount, "vat": base})
    return out


def add_charges(rows, net, gross, breakdown):
    for row in rows:
        amount = row["amount"]
        net += amount
        hit = next((b for b in breakdown if b["vat"] == row["vat"]), None)
        if hit is None:
            hit = {"vat": row["vat"], "net": 0, "tax": 0}
            breakdown.append(hit)
            breakdown.sort(key=lambda b: b["vat"])
        hit["net"] += amount
        before = hit["tax"]
        hit["tax"] = money.round_div(hit["net"] * hit["vat"], money.BP)
        gross += amount + (hit["tax"] - before)
    return net, gross, breakdown


def apply_discounts(lines):
    """Der Zeilenrabatt, und was er in Geld ausmacht.

    `discount` ist der Satz in Basispunkten, `discountAmount` der Betrag, den die Zeile
    dadurch verliert. Manche Vorlagen drucken den Satz in der Rabattspalte, manche den
    Betrag; beide Formen sind `lineDiscount`, und beide müssen aus derselben Rechnung
    kommen, sonst stimmt der Positionsbetrag nicht mehr.
    """
    for l in lines:
        if l["discount"]:
            gross = money.line_net(l["quantity"], l["unitPrice"], l["priceBaseQty"])
            l["lineNet"] = gross - money.round_div(gross * l["discount"], money.BP)
            l["discountAmount"] = gross - l["lineNet"]


def totals(lines):
    by_rate = {}
    for l in lines:
        by_rate[l["vat"]] = by_rate.get(l["vat"], 0) + l["lineNet"]
    breakdown = []
    net = gross = 0
    for rate in sorted(by_rate):
        base = by_rate[rate]
        tax = money.round_div(base * rate, money.BP)
        breakdown.append({"vat": rate, "net": base, "tax": tax})
        net += base
        gross += base + tax
    return net, gross, breakdown


FLAWS = ["rounding", "missing_line", "extra_charge", "cent_drift"]


def flaw(rng, lines, net, gross, breakdown):
    kind = rng.choice(FLAWS)
    if kind == "rounding":
        gross += rng.choice([-2, -1, 1, 2])
    elif kind == "cent_drift":
        net += rng.choice([-1, 1])
    elif kind == "extra_charge":
        amount = rng.randint(250, 1500)
        net += amount
        gross += amount + money.round_div(amount * breakdown[-1]["vat"], money.BP)
        breakdown[-1]["net"] += amount
        breakdown[-1]["tax"] = money.round_div(breakdown[-1]["net"] * breakdown[-1]["vat"], money.BP)
        return net, gross, breakdown, {"kind": kind, "amount": amount}
    else:
        victim = rng.choice(lines)
        net -= victim["lineNet"]
        gross -= victim["lineNet"] + money.round_div(victim["lineNet"] * victim["vat"], money.BP)
    return net, gross, breakdown, {"kind": kind}


# Positionen je Rechnung. Bis v9 gleichverteilt über die ganze Liste, also im
# Schnitt 17 Positionen und 3,3 gedruckte Seiten je Variation. Die 109 echten
# Belege des Nutzers haben im Schnitt **1,22 Seiten**: die meisten Eingangs-
# rechnungen eines Wirts sind kurz. Die Gewichte bilden das nach — rund 58 % der
# Rechnungen passen auf eine Seite, 27 % auf zwei, der lange Schwanz bleibt
# erhalten, damit mehrseitige Belege mit Übertrag weiter vorkommen.
LINE_COUNTS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 22, 26, 31, 38, 45, 60]
LINE_WEIGHTS = [8, 10, 11, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2.5, 2, 2, 1.5, 1.2, 1, 0.8, 0.6]


def make(seed, index):
    rng = random.Random(seed)
    names, weights = vocab.category_pool()
    category = rng.choices(names, weights, k=1)[0]
    sup = supplier(rng, category)
    date = dt.date(2025, 1, 1) + dt.timedelta(days=rng.randint(0, 729))
    count = rng.choices(LINE_COUNTS, LINE_WEIGHTS, k=1)[0]
    table = vocab.RATES[sup["country"]]
    kinds = vocab.CATEGORIES[category]["rates"]
    if sup["country"] == "AT" and category == "wein" and rng.random() < 0.25:
        kinds = ["special"]
    # Fehlerbild 4: **gemischte Sätze in einer Tabelle**. v11 zog zwei Sätze nur, wenn
    # die Warengruppe zwei führte *und* dann in 45 % davon — über alle Rechnungen
    # gerechnet knapp 17 %. Auf echten Belegen ist die Mischung die Regel: eine
    # Getränkerechnung führt Bier mit 19 % und Milchmixgetränke mit 7 %, eine
    # Metzgereirechnung Wurst mit 7 % und Grillzubehör mit 19 %. Seit v12 tragen
    # 55 % der Rechnungen zwei Sätze, unabhängig von der Warengruppe.
    if len(kinds) == 1 and "special" not in kinds and rng.random() < 0.55:
        kinds = sorted({kinds[0], "standard", "reduced"})
    vat_rates = sorted({table[k] for k in (kinds if rng.random() < 0.82 else kinds[:1])})
    # v11: 35 % statt 25 %. Der Zeilenrabatt ist die einzige Quelle für `lineDiscount`,
    # und die Rabattspalte wird gestrichen, sobald keine Zeile einen Rabatt trägt
    # (`layout.fit`) — bei 25 % lag die Klasse unter dem Ziel von 15 % der Seiten.
    with_discounts = rng.random() < 0.35
    roll = rng.random()
    # Undurchsichtige Namen (reine Codes, Buchstabensuppe): ~15 % aller Positionen.
    # 8 % der Rechnungen sind fast ganz undurchsichtig, 22 % teilweise, der Rest
    # streut einzelne Codes ein. Ohne sie findet das Modell die Bezeichnungsspalte
    # daran, dass dort Lebensmittelwörter stehen.
    opaque_share = 0.92 if roll < 0.06 else (rng.uniform(0.15, 0.45) if roll < 0.24 else 0.05)
    # Belegart zuerst: eine Gutschrift dreht das Vorzeichen jeder Position, und
    # das muss vor dem Ziehen der Preise feststehen.
    roll = rng.random()
    kind = "delivery_note" if roll < 0.11 else ("credit" if roll < 0.19 else "invoice")
    sign = -1 if kind == "credit" else 1
    bare_units = rng.random() < 0.25
    with_variants = rng.random() < 0.3
    # E-Rechnungsausdruck: die Einheitenspalte führt den UN/ECE-Code statt des
    # deutschen Worts. Fünf Prozent, so oft wie in den echten Belegen des Nutzers.
    code_units = rng.random() < 0.07
    # Jahrgangscodierte Artikelnummern (`2024-S-01`): bei Wein und Spirituosen die
    # Regel, sonst die Ausnahme. Global landet das bei rund 4 % der Positionen.
    year_ids = 0.45 if category in ("wein", "spirituosen") else (
        0.10 if category in ("brauerei", "feinkost", "saefte_alkoholfrei") else 0.015)
    lines = sample_lines(rng, category, count, vat_rates, with_discounts, opaque_share,
                         sign, bare_units, with_variants, code_units, year_ids)
    free_count = rng.randint(1, 3) if rng.random() < 0.12 else 0
    deposit_count = (rng.randint(1, 2) if rng.random() < (0.3 if category == "getraenke" else 0.07)
                     and kind != "credit" else 0)
    if free_count or deposit_count:
        lines = extra_lines(rng, lines, category, vat_rates, kind, free_count, deposit_count)
    apply_discounts(lines)
    goods_net, gross, breakdown = totals(lines)
    net = goods_net
    # Versand, Fracht, Verpackung, Pfand, Rabatt, Rundung: gedruckte Summenzeilen,
    # die den Nettobetrag verändern und in keiner Position stehen.
    # v11: 48 % statt 30 %. Die Zuschlagszeile ist die einzige Quelle für `charge`
    # und `subtotal`, und beide erscheinen nur auf der *letzten* Seite einer Rechnung —
    # über alle Seiten gerechnet lagen sie bei 30 % unter dem Ziel von 15 %.
    charge_rows = charges(rng, breakdown, rng.randint(1, 2)) \
        if kind == "invoice" and rng.random() < 0.48 else []
    if charge_rows:
        net, gross, breakdown = add_charges(charge_rows, net, gross, breakdown)
    defect = None
    if rng.random() < 0.15:
        net, gross, breakdown, defect = flaw(rng, lines, net, gross, breakdown)
    number = invoice_number(rng, date, index % 100000 + rng.randint(1, 900))
    if kind == "delivery_note":
        for l in lines:
            l["unitPrice"] = l["lineNet"] = l["discount"] = 0
        charge_rows = []
    invoice = {
        "source": "scan",
        "supplierName": sup["name"],
        "supplierVatId": sup["vatId"],
        "number": number,
        "date": date.isoformat(),
        "currency": "EUR",
        "netTotal": 0 if kind == "delivery_note" else net,
        "grossTotal": 0 if kind == "delivery_note" else gross,
        "vatBreakdown": [] if kind == "delivery_note" else breakdown,
        "lines": [{k: l[k] for k in ("no", "name", "sellerArticleId", "gtin", "quantity", "unitText",
                                     "unitCode", "unitPrice", "priceBaseQty", "lineNet", "vat")}
                  for l in lines],
    }
    skonto_pct = rng.choice([2, 2, 3])
    meta = {
        "category": category,
        "kind": kind,
        "supplier": sup,
        "customer": customer(rng, sup),
        "payment": rng.choice(vocab.PAYMENT),
        "due": (date + dt.timedelta(days=rng.choice([8, 14, 21, 30]))).isoformat(),
        "delivery": (date - dt.timedelta(days=rng.randint(0, 3))).isoformat(),
        # Bestelldatum und Leistungszeitraum: zwei Daten mehr im Kopf, die aussehen wie
        # das Rechnungsdatum und keines sind. In v10 gab es sie gar nicht, also war das
        # Rechnungsdatum das einzige Datum der Seite — und das Modell hat gelernt,
        # "Datum oben rechts" zu lesen statt der Beschriftung.
        "order_date": (date - dt.timedelta(days=rng.randint(1, 21))).isoformat(),
        "service_from": (date.replace(day=1)).isoformat(),
        "service_to": date.isoformat(),
        # Die Bestellnummer stand bisher nur auf jeder zweiten Rechnung; als eigene
        # Klasse braucht sie Beispiele.
        "order": (f"B{rng.randint(10000, 999999)}" if rng.random() < 0.5
                  else rng.choice([f"BE-{rng.randint(1000, 99999)}",
                                   f"{rng.randint(100000, 9999999)}",
                                   f"AB{date.year % 100}{rng.randint(1000, 9999)}",
                                   f"PO-{rng.randint(10000, 99999)}"])),
        # Die Inhaberzeile trägt bei Familienbetrieben denselben Nachnamen wie der
        # Briefkopf — `METZGEREI HOFMANN / Inh. Georg Hofmann`. Genau dieser Briefkopf
        # hat v11 den Lieferantennamen gekostet (v11/REPORT-families.md, Achse
        # `tagline`), und der Korpus kannte ihn nur mit zwei fremden Namen.
        "owner_line": (f"Inh. {rng.choice(vocab.FIRST_NAMES)} {sup['name'].split()[-2]}"
                       if len(sup["name"].split()) > 2 and rng.random() < 0.45
                       else f"Inh. {person(rng)}"),
        # Der gemischt gesetzte Werbesatz neben dem Namen (v11, Achse `tagline`).
        # Er ist `O` — siehe vocab.SLOGANS.
        "slogan": slogan(rng, category, date.year),
        "tagline": rng.choice([vocab.TRADES[category], vocab.TRADES[category],
                               f"{vocab.TRADES[category]} seit {rng.randint(1890, 1995)}",
                               "Großhandel", "Zustellservice"]),
        # Der lange Werbesatz über dem Absender, in Versalien und Akzentfarbe —
        # die auffälligste Zeile am Kopf und garantiert kein Lieferantenname.
        "claim": rng.choice(vocab.CLAIMS).format(trade=vocab.TRADES[category].upper()),
        # Ablenker im Kopf: eine Lieferschein-Nummer ist eine Nummer und kein
        # Datum, ein Sachbearbeiter ist ein Name und kein Lieferant.
        "extras": {
            "delivery_note": rng.choice([f"LS-{rng.randint(10000, 999999)}",
                                         f"{rng.randint(100000, 9999999)}",
                                         f"L{date.year % 100}/{rng.randint(1000, 9999)}"]),
            "order2": f"A{date.year % 100}-{rng.randint(1000, 99999)}",
            "taxno": (sup["vatId2"] if sup["vatId2"] and rng.random() < 0.3
                      else sup["taxNumber"] if rng.random() < 0.5 else sup["vatId"]),
            "clerk": person(rng),
            "ref": rng.choice([f"P-{date.year}-{rng.randint(10, 99)}",
                               f"{rng.choice('ABCDEFGHKLMRSTW')}{rng.choice('ABCDEFGHKLMRSTW')}/"
                               f"{rng.randint(100, 999)}",
                               person(rng)]),
            "shipping": rng.choice(vocab.SHIPPERS),
            "pageref": "",
        },
        "skonto_pct": skonto_pct,
        "skonto": money.round_div(abs(gross) * skonto_pct * 100, money.BP),
        "skonto_date": (date + dt.timedelta(days=rng.choice([7, 8, 10, 14]))).isoformat(),
        "paid": (abs(gross) // 2 // 100) * 100,
        "defect": defect,
        "opaque_share": round(opaque_share, 2),
        "bare_units": bare_units,
        "free_lines": free_count,
        "deposit_lines": deposit_count,
        "goods_net": goods_net,
        "charges": charge_rows,
        "info_text": info_row(rng, date),
        "print_code": f"{rng.choice('RKB')}{rng.randint(1, 9)} {rng.randint(10000000, 99999999)}",
        "receipt_time": f"{rng.randint(7, 20):02d}:{rng.randint(0, 59):02d}",
        "barcode_text": f"{rng.randint(1000000000, 9999999999)}",
        "needs_discount_column": any(l["discount"] for l in lines),
        # Für coverage.py: die Achsen aus den v10-Diagnosen, die aus den *Daten*
        # kommen und nicht aus der Vorlage.
        "code_units": code_units,
        "numeric_ids": sum(1 for l in lines
                           if l["sellerArticleId"] and l["sellerArticleId"].isdigit()
                           and 3 <= len(l["sellerArticleId"]) <= 4),
        "gtin_lines": sum(1 for l in lines if l["gtin"]),
        "render_lines": lines,
    }
    return invoice, meta
