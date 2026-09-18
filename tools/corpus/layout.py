import os
import random

import families

# Die Achse `tagline` (gemischt gesetzter Werbesatz im Briefkopf) erzwingen. Der
# Ergänzungskorpus `corpus-v11s` wird damit gebaut: jede Variation trägt einen
# Werbesatz, alles andere bleibt frei gewürfelt.
#
#     CORPUS_FORCE_TAGLINE=1 python3 generate.py --out ... --seed umsatz-v11s
#
# Eine Umgebungsvariable statt eines Schalters, weil `generate.py` die Vorlagen in
# Worker-Prozessen zieht: die Variable erbt jeder Worker von selbst, ein Schalter
# müsste durch `one()` und `variation()` durchgereicht werden.
FORCE_TAGLINE = os.environ.get("CORPUS_FORCE_TAGLINE", "") not in ("", "0")

LABELS = {
    "pos": ["Pos", "Pos.", "Nr.", "Position", "#", "Zeile"],
    "artikel": ["Art.-Nr.", "ArtNr", "Artikelnummer", "Art.Nr.", "Nr.", "Artikel-Nr.", "Art. Nr.",
                # Zusammengezogene Kopfzeile über einer einzigen Codespalte: die Position
                # steht dann gar nicht mehr für sich, der Kopf behauptet aber beides.
                "Pos Artikel", "Pos/Artikel", "Pos. Art.-Nr.", "Artikel"],
    "gtin": ["EAN", "GTIN", "EAN-Code", "EAN/GTIN"],
    "name": ["Bezeichnung", "Artikelbezeichnung", "Artikel", "Warenbezeichnung", "Beschreibung",
             "Text", "Bezeichnung der Ware", "Produkt"],
    "groesse": ["Größe", "Gr.", "Variante", "Ausf.", "Größe/Var.", "Var.", "Ausführung"],
    "menge": ["Menge", "Anz.", "Anzahl", "Stk", "Stück", "Mng.", "Liefermenge", "Mge"],
    "einheit": ["Einheit", "ME", "Einh.", "EH", "Einheit/ME"],
    "preis": ["Einzelpreis", "E-Preis", "Preis/Einh.", "Preis", "EP", "Einzel", "Stückpreis",
              "Preis je Einheit", "à Preis", "Einzelpr."],
    "basis": ["Basis", "je", "Preisbasis", "pro"],
    "rabatt": ["Rabatt", "Rab.%", "Rabatt %", "Nachlass", "Rab.", "%"],
    "mwst": ["MwSt", "USt", "MwSt%", "St.", "Steuer", "MwSt.-Satz", "%", "USt%", "Satz"],
    "betrag": ["Gesamt", "Betrag", "Gesamtpreis", "Summe", "Netto", "Wert", "Gesamtbetrag",
               "Nettobetrag", "Gesamt netto"],
    # Unbeschriftete Spalten, die neben einer unserer Spalten stehen und ihr zum
    # Verwechseln ähnlich sehen: die Währung neben dem Betrag, der Steuerschlüssel
    # neben dem Preis, die Warengruppe neben der Artikelnummer.
    "waehrung": ["Währ.", "EUR", "Whg", "Währung", "Whrg.", "Val."],
    "steuercode": ["St", "StC", "Code", "S", "MwSt-Kz", "Stschl."],
    "wg": ["WG", "WGr", "Gruppe", "Wgr.", "Warengr."],
}

# Zweizeilige Spaltenköpfe. Auf echten Rechnungen die Regel, sobald die Spalte
# schmal ist: "Einzelpreis / netto" steht untereinander, nicht nebeneinander.
LABELS2 = {
    "pos": [("Pos.", "Nr."), ("Position", "Nr.")],
    "artikel": [("Artikel-", "Nr."), ("Art.-Nr.", "intern"), ("Pos.", "Art.-Nr.")],
    "name": [("Artikel-", "bezeichnung"), ("Bezeichnung", "der Ware"), ("Waren-", "bezeichnung")],
    "groesse": [("Größe /", "Variante"), ("Aus-", "führung")],
    "menge": [("Menge", "Einheit"), ("Liefer-", "menge"), ("Menge", "gel."), ("Anzahl", "ME")],
    "einheit": [("Einheit", "ME"), ("Mengen-", "einheit")],
    "preis": [("Einzelpreis", "netto"), ("Preis", "je Einheit"), ("Einzel-", "preis"),
              ("E-Preis", "netto"), ("Preis", "EUR")],
    "basis": [("Preis-", "basis")],
    "rabatt": [("Rabatt", "%"), ("Nachlass", "%")],
    "mwst": [("MwSt.", "%"), ("USt", "%"), ("Steuer", "Satz"), ("MwSt.", "Satz")],
    "betrag": [("Gesamtpreis", "netto"), ("Gesamt", "netto"), ("Betrag", "EUR"),
               ("Summe", "netto"), ("Gesamt-", "betrag")],
    "waehrung": [("Wäh-", "rung")],
    "steuercode": [("St.-", "Schl.")],
    "wg": [("Waren-", "gruppe")],
}

# Einheit in Klammern hinter dem Kopf: "Menge (Stk)", "Betrag (EUR)".
HEADER_HINTS = {"menge": ["(Stk)", "(ME)", "(Stk.)"], "preis": ["(EUR)", "(€)", "(netto)"],
                "betrag": ["(EUR)", "(€)", "(netto)"], "mwst": ["(%)"], "rabatt": ["(%)"],
                "basis": ["(je)"]}

ORDERS = [
    ["pos", "artikel", "name", "menge", "einheit", "preis", "rabatt", "mwst", "betrag"],
    ["pos", "menge", "einheit", "artikel", "name", "preis", "rabatt", "mwst", "betrag"],
    ["artikel", "name", "menge", "einheit", "preis", "betrag", "mwst", "rabatt"],
    ["menge", "einheit", "name", "preis", "rabatt", "mwst", "betrag"],
    ["pos", "name", "artikel", "menge", "einheit", "preis", "mwst", "rabatt", "betrag"],
    ["pos", "menge", "name", "artikel", "preis", "betrag", "rabatt", "mwst"],
    ["artikel", "gtin", "name", "menge", "einheit", "preis", "rabatt", "mwst", "betrag"],
    ["pos", "name", "menge", "einheit", "basis", "preis", "rabatt", "mwst", "betrag"],
    ["menge", "name", "preis", "betrag", "mwst", "rabatt"],
    ["pos", "artikel", "name", "preis", "menge", "einheit", "rabatt", "betrag", "mwst"],
    # Kollisionen, die dem Tagger auf echten Rechnungen die Menge kosten: Pos oder
    # Artikelnummer stehen unmittelbar VOR der Menge, also ist die Zahl links von
    # der Menge keine Menge. Und zwischen Preis und Betrag steht etwas, das wie ein
    # Preis aussieht (Rabatt, Steuerschlüssel).
    ["pos", "name", "menge", "einheit", "preis", "steuercode", "betrag", "waehrung"],
    ["artikel", "menge", "einheit", "name", "preis", "rabatt", "betrag"],
    ["pos", "artikel", "menge", "name", "preis", "betrag", "waehrung"],
    ["wg", "artikel", "name", "menge", "einheit", "preis", "betrag", "mwst"],
    ["pos", "name", "wg", "menge", "preis", "steuercode", "rabatt", "betrag"],
    ["artikel", "name", "menge", "preis", "rabatt", "betrag", "waehrung"],
    # Einheit vor dem Preis, Menge als nackte Zahl zwischen Preis und Steuersatz.
    ["artikel", "name", "einheit", "preis", "menge", "mwst", "betrag"],
    ["pos", "name", "einheit", "preis", "menge", "steuercode", "mwst", "betrag"],
    # Größen-/Variantenspalte zwischen Artikelnummer und Bezeichnung.
    ["pos", "artikel", "groesse", "name", "menge", "preis", "mwst", "betrag"],
    ["artikel", "groesse", "name", "menge", "einheit", "preis", "betrag"],
]

# Schriftfamilien, die auf dem Rechner wirklich installiert sind — eine Liste mit
# Wunschnamen bringt keine Varianz, sie fällt still auf dieselbe Ersatzschrift
# zurück. Die Zahl daneben korrigiert den Grad, damit alle Familien ähnlich groß
# wirken.
SANS = [
    ("'Liberation Sans', Arial, sans-serif", 0),
    ("Arimo, 'Liberation Sans', sans-serif", 0),
    ("'Nimbus Sans', Helvetica, sans-serif", 0),
    ("'TeX Gyre Heros', Helvetica, sans-serif", 0),
    ("'TUM Neue Helvetica', Helvetica, sans-serif", 0),
    ("Roboto, 'Liberation Sans', sans-serif", -0.1),
    ("'Open Sans', 'Liberation Sans', sans-serif", -0.2),
    ("Lato, 'Liberation Sans', sans-serif", 0.1),
    ("Inter, 'Liberation Sans', sans-serif", -0.3),
    ("Carlito, Calibri, sans-serif", 0.2),
    ("Cabin, 'Liberation Sans', sans-serif", 0),
    ("'Clear Sans', 'Liberation Sans', sans-serif", 0),
    ("'DejaVu Sans', Verdana, sans-serif", -0.5),
    ("FreeSans, Helvetica, sans-serif", 0),
    ("'PT Sans', 'Liberation Sans', sans-serif", 0),
    ("Ubuntu, 'Liberation Sans', sans-serif", -0.1),
    ("Cantarell, 'Liberation Sans', sans-serif", 0),
    ("'Gillius ADF', 'Liberation Sans', sans-serif", 0.1),
    ("'Universalis ADF Std', 'Liberation Sans', sans-serif", 0),
    ("'Latin Modern Sans', 'Liberation Sans', sans-serif", 0),
]
CONDENSED = [
    ("'Roboto Condensed', 'Nimbus Sans Narrow', sans-serif", 0.2),
    ("'Open Sans Condensed', 'Nimbus Sans Narrow', sans-serif", 0.1),
    ("'PT Sans Narrow', 'Nimbus Sans Narrow', sans-serif", 0.3),
    ("'Nimbus Sans Narrow', 'Arial Narrow', sans-serif", 0.3),
    ("'DejaVu Sans Condensed', Verdana, sans-serif", -0.3),
    ("'TeX Gyre Heros Cn', Helvetica, sans-serif", 0.2),
    ("'Gillius ADF Cond', 'Nimbus Sans Narrow', sans-serif", 0.2),
    ("'LM Sans Demi Cond 10', 'Nimbus Sans Narrow', sans-serif", 0.2),
]
SERIF = [
    ("'Liberation Serif', 'Times New Roman', serif", 0.4),
    ("Tinos, 'Times New Roman', serif", 0.4),
    ("'Nimbus Roman', 'Times New Roman', serif", 0.4),
    ("'TeX Gyre Termes', 'Times New Roman', serif", 0.4),
    ("'PT Serif', Georgia, serif", 0.1),
    ("'EB Garamond', Garamond, serif", 0.7),
    ("Caladea, Cambria, serif", 0.2),
    ("'Gentium Basic', Georgia, serif", 0.3),
    ("'Bitstream Charter', Georgia, serif", 0.2),
    ("'Linux Libertine O', Georgia, serif", 0.3),
    ("'DejaVu Serif', Georgia, serif", -0.3),
    ("'Accanthis ADF Std', Georgia, serif", 0.4),
    ("'Berenis ADF Pro', Georgia, serif", 0.3),
    ("'TeX Gyre Pagella', Palatino, serif", 0.2),
    ("'TeX Gyre Bonum', Bookman, serif", 0.1),
    ("'TeX Gyre Schola', Century, serif", 0.1),
    ("C059, Century, serif", 0.2),
    ("P052, Palatino, serif", 0.2),
    ("'Roboto Slab', Georgia, serif", 0),
    ("'Latin Modern Roman', 'Times New Roman', serif", 0.3),
    ("'DejaVu Serif Condensed', Georgia, serif", -0.2),
]
MONO = [
    ("'Liberation Mono', 'Courier New', monospace", 0),
    ("Cousine, 'Courier New', monospace", 0),
    ("'DejaVu Sans Mono', monospace", -0.4),
    ("'Nimbus Mono PS', 'Courier New', monospace", 0),
    ("'PT Mono', monospace", 0),
    ("'Ubuntu Mono', monospace", 0.4),
    ("'Courier 10 Pitch', 'Courier New', monospace", 0.2),
    ("Hack, monospace", -0.3),
    ("'Go Mono', monospace", -0.2),
    ("'Latin Modern Mono', 'Courier New', monospace", 0),
]
# Nur für Wortmarken und Überschriften — als Fließschrift wäre das keine Rechnung.
DISPLAY = ["'Lobster Two', cursive", "Comfortaa, sans-serif", "'TeX Gyre Chorus', cursive",
           "Z003, cursive", "'Comic Neue', cursive", "NATS, sans-serif",
           "'Roboto Slab', serif", "'EB Garamond SC', serif", "'Go Smallcaps', sans-serif",
           "'Latin Modern Roman Caps', serif"]
FONTS = SANS + CONDENSED + SERIF + MONO
FONT_POOLS = ([SANS, CONDENSED, SERIF, MONO], [52, 17, 25, 6])

TITLES = {
    "invoice": ["RECHNUNG", "Rechnung", "Rechnung Nr.", "AUSGANGSRECHNUNG", "Faktura", "R E C H N U N G",
                "Rechnung / Faktura"],
    "delivery_note": ["LIEFERSCHEIN", "Lieferschein", "Lieferschein Nr.", "L I E F E R S C H E I N",
                      "Warenbegleitschein"],
    "credit": ["GUTSCHRIFT", "Gutschrift", "Rechnungskorrektur", "Gutschriftanzeige",
               "Stornorechnung", "G U T S C H R I F T", "Korrekturrechnung"],
}

# Überschriften, die in der Titelzeile selbst die Nummer beschriften dürfen.
# Gesperrt gesetzte Formen sind seit v10 dabei: "R E C H N U N G   N R.   2503561"
# steht so auf echten Rechnungen, und die OCR liefert die Buchstaben ohnehin
# einzeln — der ganze Schlüssellauf ist dann `numberLabel`. Die Sperrung erzeugt
# `title_spaced_key` zur Laufzeit, damit die Liste hier nicht doppelt geführt wird.
TITLE_KEYS = {
    "invoice": ["Rechnung Nr.", "Rechnung", "RECHNUNG Nr.", "Rechnung-Nr.", "Faktura Nr.",
                "RECHNUNG", "Rechnung Nummer"],
    "delivery_note": ["Lieferschein Nr.", "Lieferschein", "LIEFERSCHEIN Nr.", "Lieferschein-Nr."],
    "credit": ["Gutschrift Nr.", "Gutschrift", "GUTSCHRIFT Nr.", "Gutschrift-Nr.",
               "Rechnungskorrektur Nr."],
}

META_LABELS = {
    "number": ["Rechnungsnummer", "Rechnungs-Nr.", "Rechnung Nr.", "Beleg-Nr.", "Nr.", "RE-Nr.",
               "Dokumentnummer", "Lieferschein-Nr.", "Belegnummer"],
    "date": ["Rechnungsdatum", "Datum", "Belegdatum", "Rechnungs-Datum", "vom", "Ausstellungsdatum",
             "Datum der Rechnung"],
    "customer": ["Kundennummer", "Kd.-Nr.", "Kunden-Nr.", "Debitor", "Kd.Nr.", "Kundennr"],
    "delivery": ["Lieferdatum", "Liefertag", "Leistungsdatum", "Lieferung am"],
    "order": ["Bestellnummer", "Ihre Bestellung", "Auftrag", "Best.-Nr."],
    "due": ["Fällig am", "Zahlbar bis", "Fälligkeit", "Zahlungsziel"],
    # Ablenker: Kopfangaben, die aussehen wie die Rechnungsnummer oder das
    # Rechnungsdatum und keines von beidem sind. Ihre Werte bleiben `O`, ihre
    # Beschriftung ist `otherLabel` — daran hängt die Unterscheidung.
    "delivery_note": ["Lieferschein-Nr.", "Lieferschein", "LS-Nr.", "Lieferscheinnummer"],
    "order2": ["Auftrags-Nr.", "Auftragsnummer", "Auftrag Nr.", "Kommission", "Kommissions-Nr."],
    "taxno": ["Steuernummer", "St.-Nr.", "UID", "USt-IdNr.", "UID-Nr.", "ATU-Nr."],
    "clerk": ["Sachbearbeiter", "Bearbeiter", "Ihr Ansprechpartner", "Sachbearbeiterin", "Kontakt",
              "Sachb."],
    "ref": ["Ihr Zeichen", "Unser Zeichen", "Ihre Referenz", "Referenz", "Projekt"],
    "shipping": ["Versand", "Versandart", "Versand per", "Lieferart", "Spediteur"],
    "pageref": ["Seite", "Blatt", "Seite/Blatt"],
}

EXTRA_KEYS = ["delivery_note", "order2", "taxno", "clerk", "ref", "shipping", "pageref"]
# Schlüssel, die gedruckt werden und *keinen* Wert tragen — das Feld ist im
# Formular vorgesehen und bleibt leer. Die Beschriftung ist trotzdem `otherLabel`.
EMPTY_KEYS = ["Kd-UStIdNr.", "Ihre USt-IdNr.", "Bestellnummer", "Ihr Zeichen", "Abweichende Lieferadresse"]

CONTACT_LABELS = {
    "tel": ["Tel.", "Telefon", "Tel", "T"],
    "fax": ["Fax", "Telefax", "Fax.", "F"],
    "mail": ["E-Mail", "Mail", "E-Mail:", "eMail"],
    "web": ["Web", "Internet", "www", "Homepage"],
    "uid": ["UID", "USt-IdNr.", "ATU", "UID-Nr."],
    "stnr": ["Steuernummer", "St.-Nr.", "Steuer-Nr."],
}

ADDR_HEADINGS = ["Rechnungsadresse", "Lieferadresse", "Rechnung an", "Lieferanschrift",
                 "Kunde", "Rechnungsempfänger"]

# Lieferanschrift als Fließsatz auf einer Zeile — der Kundenname steht mitten im
# Satz und ist trotzdem kein Lieferant.
DELIVERY_SENTENCES = ["Die Ware wird geliefert an:", "Lieferung an:", "Versandadresse:",
                      "Wir liefern an:", "Warenempfänger:"]

TOTAL_EXTRA_LABELS = {
    "skonto": ["Skonto", "abzgl. Skonto", "Skontobetrag"],
    "paid": ["Bereits bezahlt", "Anzahlung", "Bereits beglichen", "Akonto"],
    "payuntil": ["Zahlbar bis", "Fällig am", "Zahlungsziel"],
}

# Zuschlags- und Abschlagszeilen im Summenblock. Sie stehen NICHT in den
# Positionen, gehen aber in den Nettobetrag ein — genau darum stimmt auf echten
# Rechnungen die Summe der Zeilen nie mit dem Nettobetrag überein.
CHARGE_LABELS = {
    "shipping": ["Versandkosten", "Versand", "Versandpauschale", "Porto", "Versand & Verpackung"],
    "freight": ["Fracht", "Frachtkosten", "Frachtanteil", "Transportkosten"],
    "packing": ["Verpackung", "Verpackungsanteil", "Verpackungskosten"],
    "deposit": ["Pfand", "Leergut", "Pfandanteil", "Leergutrücknahme", "Gebindepfand"],
    "discount": ["Rabatt", "Sonderrabatt", "Nachlass", "Aktionsrabatt", "Treuerabatt"],
    "rounding": ["Rundung", "Rundungsdifferenz", "Rundungsausgleich"],
    "minimum": ["Mindermengenzuschlag", "Mindermenge", "Kleinmengenzuschlag"],
}

TOTAL_LABELS = {
    "net": ["Nettobetrag", "Summe netto", "Zwischensumme", "Netto", "Gesamt netto", "Warenwert netto",
            "Nettosumme", "Netto-Gesamt", "Steuerpflichtiger Betrag", "Gesamt netto EUR"],
    "gross": ["Bruttobetrag", "Gesamtbetrag", "Rechnungsbetrag", "Zu zahlen", "Endbetrag", "Gesamt brutto",
              "Bruttosumme", "Zahlbetrag", "Brutto-Gesamt", "Summe"],
    "vat": ["MwSt", "USt", "Umsatzsteuer", "Mehrwertsteuer", "zzgl. MwSt", "Mwst", "MwSt."],
}
# Die Zeile über den Zuschlägen: die reine Summe der Positionen. Sie ist *nicht*
# der Nettobetrag, sobald Versand dazukommt, und bleibt deshalb `O`.
GOODS_LABELS = ["Warenwert", "Summe Positionen", "Zwischensumme", "Warenwert netto",
                "Summe Artikel", "Positionssumme"]

# Summen als Satz: "Rechnungsbetrag: 70,81 EUR".
TOTALS_SENTENCES = {
    "net": ["Nettobetrag", "Summe netto", "Netto"],
    "gross": ["Rechnungsbetrag", "Zu zahlen", "Gesamtbetrag", "Endbetrag"],
}

PAY_METHODS = ["Zahlung per Sofortüberweisung", "Zahlung per PayPal", "Zahlung per Vorkasse",
               "Zahlung per Lastschrift", "Zahlung per Kreditkarte", "Zahlung per Nachnahme",
               "Zahlungsart: Rechnung", "Bezahlt per Bankeinzug", "Zahlung bei Abholung"]

CAPTIONS = ["Positionen", "Leistungen", "Artikelübersicht", "Lieferpositionen",
            "Aufstellung", "Rechnungspositionen", "Ihre Bestellung"]

FOOTER_HEADS = {
    "contact": ["Anschrift", "Kontakt", "Sitz"],
    "bank": ["Bankverbindung", "Bank", "Zahlungsverkehr"],
    "legal": ["Register", "Rechtliches", "Firmenbuch"],
    "hours": ["Öffnungszeiten", "Servicezeiten", "Erreichbarkeit"],
    "terms": ["Bedingungen", "Hinweise", "AGB"],
}
FOOTER_HOURS = ["Mo–Fr 8–17 Uhr", "Mo–Do 7–16, Fr 7–13 Uhr", "Mo–Fr 9–18 Uhr, Sa 9–12 Uhr",
                "Mo–Fr 6–14 Uhr"]
FOOTER_TERMS = ["Es gelten unsere AGB.", "Eigentumsvorbehalt bis zur vollständigen Zahlung.",
                "Gerichtsstand ist der Sitz der Firma.", "Irrtümer und Änderungen vorbehalten."]

NUMERIC = {"menge", "preis", "rabatt", "mwst", "betrag"}
CENTERED = {"pos", "steuercode", "wg", "waehrung", "groesse"}
PRICE_COLUMNS = {"preis", "betrag", "rabatt", "mwst", "basis", "waehrung", "steuercode"}

ACCENTS = ["#1f3864", "#7b1f1f", "#1f5c2e", "#3d3d3d", "#0f4c81", "#5a3d7a", "#8a5a1f", "#000000",
           "#14505c", "#6b2d5c", "#2f4f2f", "#883c1e", "#25406b", "#555555", "#7a6a1f", "#123f6d",
           "#a33a12", "#1b6b5a", "#4b2e83", "#00566b"]
INKS = ["#111", "#000", "#222", "#333", "#1a1a1a", "#3a3a3a", "#4a4a4a", "#555", "#5c5c5c"]
TABLE_STYLES = ["grid", "rules", "zebra", "borderless", "rules", "zebra", "double", "headrule",
                "vrules", "dotted", "colshade"]
HEADER_STYLES = ["bold", "inverted", "underline", "plain", "boxed", "smallcaps", "accent",
                 "letterspaced", "bodyrow"]
TITLE_STYLES = ["plain", "plain", "plain", "letter", "smallcaps", "underline", "boxed", "band", "none"]
TITLE_WEIGHTS = [16, 16, 16, 9, 8, 10, 8, 9, 8]
TOTALS_STYLES = ["block", "boxed", "table", "block", "table", "inline", "panel", "grid", "sentence"]

# Wortlose Flächen hinter dem Satz. Kein Text, nie: eine verblasste Wortmarke
# *als Text* läse die OCR mit, und die Wahrheit könnte sie nicht sauber
# beschriften. Also ausschließlich Formen.
BG_ART = ["", "", "", "", "", "circle", "rings", "triangle", "stripes", "blob", "grid"]


# Seitenformate in mm. Die echten Scans sind ~88 % A4 und ~12 % US Letter; der
# Rest steht für alles, was ein Lieferant sonst noch durch den Drucker schickt.
# `quantise_box` normiert x an der Breite und y an der Höhe *getrennt*, also fällt
# das Seitenverhältnis gerade nicht heraus — es muss im Korpus vorkommen.
PAGE_FORMATS = {
    "a4":      (210.0, 297.0),
    "letter":  (215.9, 279.4),
    "legal":   (215.9, 355.6),
    "folio":   (210.0, 330.0),
    "b5":      (176.0, 250.0),
    "a5":      (148.0, 210.0),
    "a4_land": (297.0, 210.0),
    # Kassenbon: 80 mm Thermorolle. Kommt nicht aus PAGE_MIX, sondern nur aus der
    # Familie `receipt` — ein Bon ist kein Seitenformat, sondern ein eigenes
    # Dokumentbild, und `render.receipt_page` baut ihn ohne Tabelle.
    "receipt": (80.0, 240.0),
}
# A4 bleibt dominant wie in echt (~88 %), die selteneren Formate sind bewusst
# übergewichtet: das Modell braucht genug Beispiele, um sie zu lernen.
PAGE_MIX = (["a4", "letter", "legal", "folio", "b5", "a5", "a4_land"],
            [70, 12, 4, 4, 3, 4, 3])

# Was auf ein schmales Blatt (< 180 mm) noch passt: A5 (148) und B5 (176).
# Artikelnummer und MwSt sind hier drin, seit `render.build` den Breitenüberlauf
# selbst wegskaliert; ohne sie sah der Tagger auf 10.9 % der Seiten nie eine
# articleId und hätte "schmale Seite heißt keine Artikelnummer" gelernt.
NARROW_COLUMNS = {"pos", "name", "menge", "einheit", "preis", "betrag", "rabatt",
                  "artikel", "mwst", "waehrung", "steuercode", "wg", "groesse"}


# --------------------------------------------------------------------- Familien
#
# Der rein zufällige Wurf über alle Achsen erzeugt Vorlagen, die es so nie gibt:
# eine Serifenschrift mit Farbband und Kassenbon-Summen. Echte Rechnungen kommen
# aus einer Handvoll Programme, und jedes davon hat einen erkennbaren Stil. Eine
# Familie legt deshalb die Achsen fest, die dieses Programm ausmachen; alles
# andere wird weiter gezogen wie bisher. Eine Liste als Wert heißt "aus dieser
# Menge ziehen", ein Skalar heißt "fest".
FAMILIES = {
    # Die freie Ziehung bleibt der größte Anteil: die Familien sollen den Raum
    # ordnen, nicht einengen.
    "free": {"weight": 40, "set": {}},
    # Lexware / sevDesk: gerahmter Kopfblock rechts, Gittertabelle, Grotesk.
    "lexware": {"weight": 2.5, "set": {
        "font_pool": "sans", "meta_style": "boxed", "meta_place": "top_right",
        "table_style": ["grid", "rules"], "header_style": ["bold", "boxed"],
        "totals_style": ["table", "boxed"], "totals_side": "right",
        "footer": ["columns3", "columns2"], "title_style": ["plain", "underline"],
        "logo": ["mark", "wordmark"], "sender_place": "under"}},
    # SAP / ERP-Ausdruck: dicht, schmal, viele Codespalten, Kopf als Zeilenraster.
    "sap": {"weight": 2, "set": {
        "font_pool": ["mono", "condensed"], "size": [7.0, 7.4, 7.8], "leading": [1.08, 1.12, 1.2],
        "cell_pad_x": 1.2, "cell_pad_y": 0.7, "table_style": ["borderless", "rules"],
        "header_style": ["plain", "underline", "bodyrow"], "logo": "none",
        "meta_style": ["grid", "row"], "totals_style": ["block", "table"],
        "force_codes": True, "header_unit_hint": True, "pos_format": ["pad", "step"],
        "footer": ["line", "none"], "title_style": ["plain", "none"], "accent": "#000000"}},
    # DATEV: nüchtern, Serifen oder Arial, gerahmter Kopf, Linien.
    "datev": {"weight": 2, "set": {
        "font_pool": ["serif", "sans"], "meta_style": ["boxed", "pairs"],
        "table_style": ["rules", "grid"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "logo": ["none", "mark"],
        "pos_format": ["dot", "plain"], "footer": ["columns3", "columns2"],
        "accent": ["#3d3d3d", "#000000", "#25406b"]}},
    # WooCommerce / Shopify: Webschrift, Farbband, Summen als Schlüssel-/Wertzeile.
    "shop": {"weight": 2.5, "set": {
        "font_pool": "sans", "head_band": True, "table_style": ["zebra", "borderless", "rules"],
        "header_style": ["inverted", "accent", "bold"], "totals_style": ["grid", "block", "panel"],
        "logo": ["wordmark", "wordmark2", "band"], "sender_place": ["none", "under"],
        "meta_style": ["grid", "stacked", "pairs"], "footer": ["line", "columns2"],
        "title_style": ["plain", "band"], "bg_art": ["", "circle", "blob"]}},
    # Amazon Business: schmale Grotesk, Raster im Kopf, winzige Fußzeile.
    "amazon": {"weight": 2, "set": {
        "font_pool": ["condensed", "sans"], "meta_style": ["grid", "row"],
        "table_style": ["rules", "borderless"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "totals_side": "right",
        "logo": ["wordmark", "none"], "footer": ["line", "columns2"], "footer_size": [0.56, 0.6, 0.64],
        "size": [7.8, 8.0, 8.4], "title_style": ["plain", "smallcaps"]}},
    # InvoiceNinja (die GastroMarkt-Vorlage): Absender mittig oben, Kunde rechts
    # neben den Kopfdaten, Einheit vor dem Preis, Währung in jeder Zelle.
    "ninja": {"weight": 2, "set": {
        "font_pool": "sans", "logo_side": "center", "sender_place": "under",
        "address_corner": "left", "meta_place": "top_right", "meta_style": ["pairs", "stacked"],
        "order_pick": [16, 17], "cell_currency": ("post", "€"),
        "table_style": ["rules", "grid"], "header_style": ["bold", "inverted"],
        "totals_style": ["table", "block"], "totals_side": "right", "footer": "line",
        "title_style": ["plain", "letter"]}},
    # Metro / C+C: der Kassenbon.
    "receipt": {"weight": 2, "set": {
        "page_format": "receipt", "font_pool": ["mono", "condensed"],
        "table_style": "borderless", "header_style": "plain", "logo": ["none", "wordmark"],
        "footer": ["line", "none"], "title_style": ["plain", "letter", "none"],
        "accent": "#000000", "no_header_row": True}},
    # Klassischer Briefsatz aus der Textverarbeitung.
    "word": {"weight": 3, "set": {
        "font_pool": ["serif", "sans"], "meta_style": ["pairs", "stack", "dateline"],
        "meta_place": ["under_title", "top_right"], "table_style": ["rules", "borderless", "grid"],
        "header_style": ["bold", "plain", "underline"], "totals_style": ["block", "sentence", "table"],
        "logo": ["none", "wordmark", "mark"], "footer": ["none", "line", "bank"],
        "title_style": ["plain", "underline", "letter"], "bg_art": ""}},
    # Formularsatz wie der Nordfoto-Beleg: gerahmte Felder, Doppelpunktspalte,
    # gesperrte Überschrift mit der Nummer, Summen ganz unten, Barcode.
    "form": {"weight": 3, "set": {
        "font_pool": ["sans", "condensed"], "meta_style": "boxed", "meta_place": "top_right",
        "meta_colon": "column", "title_number": True, "title_style": "letter",
        "title_spaced_key": True, "logo": ["wordmark2", "wordmark"], "sender_place": "none",
        "head_contact": "block", "table_style": ["borderless", "rules"],
        "header_style": ["bold", "underline"], "totals_bottom": True,
        "totals_style": ["table", "block"], "footer": ["columns4", "columns5"],
        "footer_size": [0.55, 0.58, 0.62], "footer_heads": True, "decor_barcode": ["br", "bl"],
        "info_rows": True, "pay_box": True, "multi_row": ["desc2", "none"],
        "retline": True}},
}
# v11: rund hundert weitere Familien aus `families.py`.
families.merge(FAMILIES)
FAMILY_NAMES = sorted(FAMILIES)
FAMILY_WEIGHTS = [FAMILIES[n]["weight"] for n in FAMILY_NAMES]


def insert_column(columns, column, after=(), before=()):
    """Die Kollisionsspalten sollen in *jeder* Spaltenreihenfolge vorkommen können,
    nicht nur in denen, die sie zufällig führen. Steht die Spalte noch nicht in der
    Reihenfolge, wird sie an ihrem natürlichen Platz eingeschoben."""
    if column in columns:
        return
    for anchor in after:
        if anchor in columns:
            columns.insert(columns.index(anchor) + 1, column)
            return
    for anchor in before:
        if anchor in columns:
            columns.insert(columns.index(anchor), column)
            return
    columns.append(column)


def pick_font(rng, pool=None):
    if pool is None:
        table = rng.choices(*FONT_POOLS, k=1)[0]
    else:
        table = {"sans": SANS, "condensed": CONDENSED, "serif": SERIF, "mono": MONO}[pool]
    return rng.choice(table)


# Schlüssel in `set`, die keine Vorlagenachse sind, sondern die Ziehung selbst
# steuern — sie werden vor `apply_family` ausgewertet und nicht in die Vorlage
# geschrieben.
# `page_format` steht hier, weil `template()` es *vor* `apply_family` braucht: an ihm
# hängen `receipt`, `narrow_page` und die Spaltenauswahl. Zöge `apply_family` es ein
# zweites Mal, fiele der zweite Wurf anders aus als der erste — eine Familie mit
# `page_format: ["a5", "a4", "receipt"]` bekäme dann `page_format "receipt"` bei
# `receipt False`, also den vollen A4-Satz auf 80 mm Rollenbreite. `render.build`
# schrumpft dann bis an den Schriftboden, und die Betragsspalte läuft trotzdem aus
# der Seite: 74 Beanstandungen in der ersten v11-Probe, alle aus dieser Zeile.
CONTROL_KEYS = {"font_pool", "force_codes", "order_pick", "page_format"}


def apply_family(spec, family, rng):
    """Die Festlegungen der Familie über den freien Wurf legen."""
    for key, value in FAMILIES[family]["set"].items():
        if key in CONTROL_KEYS:
            continue
        if key in families.LIST_AXES:
            # Achsen, deren *Wert* selbst eine Liste ist (doc_note, meta_keys,
            # total_extras): ein Tupel heißt "fest", eine Liste von Tupeln "eines
            # davon". Ohne diese Unterscheidung zöge `rng.choice` das erste
            # Element heraus und die Achse bekäme einen String statt einer Liste.
            if value and isinstance(value[0], (list, tuple)):
                value = rng.choice(value)
            spec[key] = list(value)
            continue
        spec[key] = rng.choice(value) if isinstance(value, list) else value
    return spec


def template(seed, family=None, doctype="plain"):
    """Eine Vorlage ziehen. `family` erzwingt eine Familie (für `generate.py
    --family` und für die Belegarten, deren Zahlen schon feststehen)."""
    rng = random.Random(seed)
    if family is None:
        # Belegarten, die die Zahlen verändern, hängen an ihren eigenen Familien:
        # eine Kleinunternehmerrechnung kann nicht in einer Familie stecken, die
        # eine MwSt-Spalte druckt, und sie fällt je *Rechnung*, nicht je Variation.
        pool = families.DOCTYPE_FAMILIES.get(doctype)
        family = (rng.choice(pool) if pool
                  else rng.choices(FAMILY_NAMES, FAMILY_WEIGHTS, k=1)[0])
    fixed = FAMILIES[family]["set"]
    page_format = fixed.get("page_format") or rng.choices(PAGE_MIX[0],
                                                        weights=PAGE_MIX[1], k=1)[0]
    if isinstance(page_format, list):
        page_format = rng.choice(page_format)
    receipt = page_format == "receipt"
    narrow_page = PAGE_FORMATS[page_format][0] < 180.0
    order_pick = fixed.get("order_pick")
    order = list(ORDERS[rng.choice(order_pick)] if order_pick else rng.choice(ORDERS))
    # Die drei Kollisionsspalten sind auf schmalen Blättern nachrangig — dort steht
    # der Platz der Bezeichnung zu. Ganz ausgeschlossen sind sie nicht, sonst lernt
    # das Modell "schmale Seite heißt keine Währungsspalte".
    rare = 0.35 if narrow_page else 1.0
    codes = 1.0 if fixed.get("force_codes") else rare
    present = {"pos": rng.random() < 0.65, "artikel": rng.random() < 0.7, "gtin": rng.random() < 0.15,
               "name": True, "menge": True, "einheit": rng.random() < 0.6, "preis": True,
               "basis": rng.random() < 0.12, "rabatt": False, "mwst": rng.random() < 0.45,
               "betrag": True, "groesse": rng.random() < 0.16,
               "waehrung": rng.random() < (0.55 if codes > rare else 0.15) * codes,
               "steuercode": rng.random() < (0.5 if codes > rare else 0.13) * codes,
               "wg": rng.random() < (0.45 if codes > rare else 0.11) * codes}
    columns = [c for c in order if present.get(c)]
    # EAN und Preisbasis stehen jeweils nur in einer Grundreihenfolge; ohne
    # Einschub kamen sie auf 0,7 % der Vorlagen vor, also praktisch nie.
    if present["gtin"]:
        insert_column(columns, "gtin", after=("artikel",), before=("name",))
    if present["basis"]:
        insert_column(columns, "basis", after=("preis",), before=("betrag",))
    if present["groesse"]:
        insert_column(columns, "groesse", after=("artikel", "gtin"), before=("name",))
    if present["wg"]:
        insert_column(columns, "wg", after=("artikel", "name"))
    if present["steuercode"]:
        insert_column(columns, "steuercode", before=("betrag",), after=("preis",))
    if present["waehrung"]:
        insert_column(columns, "waehrung", after=("betrag", "preis"))
    # Ein schmales Blatt trägt nicht beliebig viele Spalten. Den Überlauf fängt
    # inzwischen `render.build` ab — es misst neben der Höhe auch die Breite und
    # verkleinert Schrift und Zellenabstand, bis die Zeile passt. Gestrichen wird
    # hier nur noch, was auf 148 mm auch gedruckt keinen Sinn ergibt (GTIN neben
    # Artikelnummer, Bemessungsgrundlage). Artikelnummer und MwSt-Spalte bleiben:
    # sie vorschnell zu streichen kostete A5/B5 jede articleId-Supervision.
    if narrow_page:
        columns = [c for c in columns if c in NARROW_COLUMNS]
    # v11, Bruttospalte je Position: nur *neben* der Nettospalte, also erst nach dem
    # Schmalseiten-Filter eingeschoben (sonst fiele `lineGross` auf A5/B5 ganz weg und
    # das Modell lernte "schmale Seite hat kein Brutto"). Ihr Kopf und ihre Ausrichtung
    # stehen weiter unten in `spec`, damit `LABELS` unberührt bleibt.
    if not receipt and "betrag" in columns and rng.random() < 0.18:
        insert_column(columns, "brutto", after=("betrag", "mwst"))
    if receipt:
        # Der Bon hat keine Tabelle; die Liste bleibt trotzdem gefüllt, damit
        # `fit()` und die Wahrheit dieselben Felder kennen.
        columns = [c for c in ("artikel", "name", "menge", "einheit", "preis", "mwst", "betrag")
                   if c in ("name", "menge", "preis", "betrag") or rng.random() < 0.55]
    pool = fixed.get("font_pool")
    font, tweak = pick_font(rng, rng.choice(pool) if isinstance(pool, list) else pool)
    # Serifen im Fließsatz, Grotesk im Briefkopf (und umgekehrt) ist auf echten
    # Rechnungen die Regel, nicht die Ausnahme.
    head_font = font if rng.random() < 0.5 else pick_font(rng)[0]
    narrow = rng.random() < 0.3
    logo = rng.choice(["none", "mark", "wordmark", "band", "mark", "wordmark", "wordmark",
                       "wordmark2"])
    # Ohne Absenderblock steht der Lieferantenname nur noch in der Wortmarke — und
    # wenn es auch die nicht gibt, muss ihn die Fußzeile tragen, sonst hat die
    # Seite keinen beschrifteten Lieferanten mehr.
    sender_place = rng.choice(["under", "under", "under", "beside", "above", "right", "none"])
    footer = rng.choice(["bank", "bank", "columns3", "columns2", "none", "line", "address",
                         "columns4", "columns5"])

    meta_style = rng.choice(["pairs", "stack", "boxed", "stacked", "grid", "title", "dateline",
                             "line"] + ([] if narrow_page else ["row"]))
    places = ["under_title", "under_title", "top_right", "panel", "bottom", "split"]
    meta_place = rng.choice(places)
    extras = [k for k in EXTRA_KEYS if rng.random() < 0.32]
    meta_keys = ["customer", "order", "delivery", "due"] + extras
    rng.shuffle(meta_keys)
    # v11: drei Kopfschlüssel mehr, deren Werte eigene Klassen tragen — Bestelldatum
    # (`orderDate`), Leistungszeitraum (`deliveryDate`) und eine erzwungene
    # Lieferschein-Nummer (`deliveryNoteNumber`). Sie werden an zufälliger Stelle
    # eingeschoben, weil die schmalen Kopfformen (`grid`, `row`, `stacked`, `line`)
    # nur die ersten drei bis fünf Angaben drucken.
    v11_labels = {
        "orderdate": rng.choice(["Bestelldatum", "Bestellt am", "Ihre Bestellung vom",
                                 "Bestelldat.", "Auftragsdatum"]),
        "service": rng.choice(["Leistungszeitraum", "Leistungsdatum", "Leistungszeit",
                               "Zeitraum", "Lieferzeitraum"]),
        "deliverynote": rng.choice(["Lieferschein-Nr.", "Lieferscheinnummer", "LS-Nr.",
                                    "Lieferschein", "Lieferschein Nr."]),
    }
    for key in ("orderdate", "service", "deliverynote"):
        if rng.random() < 0.34:
            meta_keys.insert(rng.randrange(len(meta_keys) + 1), key)
        else:
            v11_labels[key] = ""
    # Der Topf der Dankes- und Hinweissätze steht in vocab.py (Textbestand, nicht
    # Layout); hier lokal geholt, damit die Modulschnittstelle von layout.py unberührt
    # bleibt.
    from vocab import THANKS as THANKS_POOL  # noqa: PLC0415
    contacts = [k for k in ("tel", "fax", "mail", "web", "uid", "stnr") if rng.random() < 0.3][:4]

    spec = {
        "id": str(seed),
        "family": family,
        "page_format": page_format,
        "receipt": receipt,
        "columns": columns,
        "glue_unit": "einheit" not in columns,
        "headers": {c: rng.choice(LABELS[c]) for c in LABELS},
        "headers2": {c: rng.choice(LABELS2[c]) for c in LABELS2},
        "header_hints": {c: rng.choice(v) for c, v in HEADER_HINTS.items()},
        "header_two_line": rng.random() < 0.22,
        "header_unit_hint": rng.random() < 0.15,
        "no_header_row": rng.random() < 0.12,
        "caption": rng.choice(CAPTIONS) if rng.random() < 0.45 else "",
        "header_style": rng.choice(HEADER_STYLES),
        "align": {c: rng.choice(["right", "right", "right", "left", "center"]) if c in NUMERIC
                  else ("center" if c in CENTERED and rng.random() < 0.45 else "left")
                  for c in LABELS},
        "table_style": rng.choice(TABLE_STYLES),
        "font": font,
        "head_font": head_font,
        "mark_font": rng.choice(DISPLAY),
        "size": round(rng.uniform(6.9, 11.4) + tweak, 2),
        "leading": round(rng.uniform(1.06, 1.72), 2),
        "body_weight": rng.choices([400, 300, 500, 600], [72, 12, 10, 6], k=1)[0],
        "letter_tight": rng.choice([0.0, 0.0, 0.0, -0.02, -0.015, 0.02]),
        "cell_pad_x": round(rng.uniform(0.5, 4.0), 1),
        "cell_pad_y": round(rng.uniform(0.5, 5.2), 1),
        "page_h_mm": None,
        "narrow_name": narrow,
        "name_width": round(rng.uniform(26, 42) if narrow else rng.uniform(46, 74), 1),
        "multi_row": rng.choices(["none", "desc2", "priceline"], [72, 18, 10], k=1)[0],
        "info_rows": rng.random() < 0.16,
        "detail_bold": rng.random() < 0.35,
        "logo": logo,
        "logo_side": rng.choice(["left", "left", "right", "center"]),
        "sender_place": sender_place,
        "head_contact": rng.choices(["lines", "block"], [78, 22], k=1)[0],
        "wordmark_caps": rng.choice(["none", "none", "css", "literal"]),
        "wordmark_lines": rng.choice([1, 1, 2]),
        "show_tagline": rng.random() < 0.3,
        "show_owner": rng.random() < 0.25,
        "contacts": contacts,
        "contact_labels": {k: rng.choice(v) for k, v in CONTACT_LABELS.items()},
        "address_corner": rng.choice(["left", "left", "right", "center"]),
        "retline": rng.random() < 0.75,
        "addr_heading": rng.choice(ADDR_HEADINGS) if rng.random() < 0.28 else "",
        "addr_customer_no": rng.random() < 0.25,
        "delivery_sentence": rng.choice(DELIVERY_SENTENCES) if rng.random() < 0.12 else "",
        "meta_style": meta_style,
        "meta_place": meta_place,
        "meta_side": rng.choice(["left", "right", "right"]),
        "meta_keys": meta_keys,
        "meta_extra_labels": {k: rng.choice(META_LABELS[k]) for k in EXTRA_KEYS},
        "meta_date_first": rng.random() < 0.25,
        "meta_empty_key": rng.choice(EMPTY_KEYS) if rng.random() < 0.16 else "",
        "meta_bare_date": rng.random() < 0.35,
        "meta_line_page": rng.random() < 0.5,
        "grid_cols": rng.randint(3, 3 if narrow_page else 5),
        "grid_box": rng.random() < 0.5,
        "dateline_word": rng.choice(["", "", "am", "den"]),
        "title_number": False,
        "title_date": False,
        "title_spaced_key": rng.random() < 0.18,
        "title_date_key": rng.choice(["vom", "Datum", "vom", "am", "Datum:", "vom Datum"]),
        "title_sep": rng.choice(["", "", "·", "—", "|"]),
        "title_style": "plain",
        "title_align": rng.choice(["left", "left", "left", "center", "right"]),
        "title_em": round(rng.uniform(1.3, 2.9), 2),
        "totals_style": rng.choice(TOTALS_STYLES),
        "totals_side": rng.choice(["right", "right", "right", "full", "left"]),
        "totals_bottom": rng.random() < 0.14,
        "totals_shade": rng.random() < 0.18,
        "goods_label": rng.choice(GOODS_LABELS),
        "charge_labels": {k: rng.choice(v) for k, v in CHARGE_LABELS.items()},
        "vat_row_form": rng.choices(["of", "bare", "base"], [40, 45, 15], k=1)[0],
        "total_extras": [k for k in ("skonto", "paid", "payuntil") if rng.random() < 0.18],
        "extra_labels": {k: rng.choice(v) for k, v in TOTAL_EXTRA_LABELS.items()},
        "sentence_net": rng.choice(TOTALS_SENTENCES["net"]),
        "sentence_gross": rng.choice(TOTALS_SENTENCES["gross"]),
        "gross_first": rng.random() < 0.18,
        "pay_box": rng.random() < 0.18,
        "pay_method": rng.choice(PAY_METHODS),
        "footer": footer,
        "footer_size": round(rng.uniform(0.55, 0.82), 2),
        "footer_heads": rng.random() < 0.3,
        "footer_code": rng.random() < 0.22,
        "footer_heads_text": {k: rng.choice(v) for k, v in FOOTER_HEADS.items()},
        "footer_hours": rng.choice(FOOTER_HOURS),
        "footer_terms": rng.choice(FOOTER_TERMS),
        "pageno_place": rng.choice(["tr", "tr", "bc", "bl"]),
        "pageno_form": rng.choice(["von", "von", "slash", "dash", "bare"]),
        "page_margin": (round(rng.uniform(9, 26), 1), round(rng.uniform(8, 24), 1)),
        "accent": rng.choice(ACCENTS),
        "accent2": rng.choice(ACCENTS),
        "ink": rng.choice(INKS),
        "rule_ink": rng.choice(["#333", "#111", "#444", "#555"]),
        "rule_light": rng.choice(["#999", "#aaa", "#888", "#bbb", "#777"]),
        "rule_weight": round(rng.uniform(0.4, 1.6), 2),
        "carry_label": rng.choice(["Übertrag", "Übertrag Seite", "Zwischensumme", "Übertrag netto"]),
        "group_headings": rng.random() < 0.18,
        "second_row_details": rng.random() < 0.22,
        "uppercase_headers": rng.random() < 0.25,
        "pos_format": rng.choice(["plain", "plain", "dot", "pad", "step"]),
        "cell_currency": rng.choice([("post", "€"), ("pre", "€"), ("post", "EUR"), ("pre", "EUR")])
        if rng.random() < 0.4 else None,
        "waehrung_text": rng.choice(["EUR", "€", "EUR", "€"]),
        "meta_number": rng.choice(META_LABELS["number"]),
        "meta_date": rng.choice(META_LABELS["date"]),
        "meta_customer": rng.choice(META_LABELS["customer"]),
        "meta_order": rng.choice(META_LABELS["order"]),
        "meta_delivery": rng.choice(META_LABELS["delivery"]),
        "meta_due": rng.choice(META_LABELS["due"]),
        "meta_colon": rng.choices(["", "glued", "span", "column"], [38, 30, 16, 16], k=1)[0],
        "show_delivery": rng.random() < 0.55,
        "show_due": rng.random() < 0.45,
        "price_decimals": rng.choices([2, 3, 4], [78, 15, 7], k=1)[0],
        "group_sep": rng.choices([".", " ", ""], [66, 20, 14], k=1)[0],
        "minus_style": rng.choices(["lead", "trail"], [72, 28], k=1)[0],
        "qty_style": rng.choices(["trim", "d2", "d3"], [62, 26, 12], k=1)[0],
        "vat_percent_sign": rng.random() < 0.5,
        "total_net": rng.choice(TOTAL_LABELS["net"]),
        "total_gross": rng.choice(TOTAL_LABELS["gross"]),
        "total_vat": rng.choice(TOTAL_LABELS["vat"]),
        "bg_art": rng.choice(BG_ART),
        "bg_opacity": round(rng.uniform(0.06, 0.18), 3),
        "bg_place": rng.choice(["table", "page", "table"]),
        "head_band": rng.random() < 0.14,
        "tint_panel": rng.random() < 0.12,
        "decor_qr": "",
        "decor_stamp": rng.random() < 0.08,
        "decor_barcode": rng.choice(["", "", "", "", "", "", "br", "bl", "tr"]),
        "title": "",
        "title_key": "",
        "date_format": rng.choices(
            ["%d.%m.%Y", "%d.%m.%y", "%d.%m.%Y", "%-d.%-m.%y", "%d. %B %Y", "%Y-%m-%d",
             "%d/%m/%Y", "%-d. %B %Y"],
            [34, 12, 16, 8, 10, 8, 6, 6], k=1)[0],

        # ------------------------------------------------------------------ v11
        # Freie Ziehungen für die neunzehn feinen Klassen. Die Regeln dazu stehen in
        # v11/CONVENTIONS.md; hier wird nur gewürfelt, beschriftet wird in blocks.py.
        "meta_v11_labels": v11_labels,
        # "Zwischensumme"/"Warenwert" als Beschriftung des *Nettobetrags*, wenn keine
        # Zuschlagszeile folgt. v10 hat sie ausnahmslos zum Warenwert erklärt und damit
        # auf zuschlagsfreien Belegen den netTotal verloren.
        "net_alias": rng.choices(
            ["", "Zwischensumme", "Warenwert", "Nettowarenwert", "Summe Positionen",
             "Warenwert netto", "Zwischensumme netto"],
            [58, 12, 9, 6, 6, 5, 4], k=1)[0],
        # Anzahlung + Zahlbetrag: der einzige Weg zu `amountDue`.
        "show_amount_due": rng.random() < 0.22,
        "due_amount_label": rng.choice(["Zahlbetrag", "Fälliger Betrag", "Restbetrag",
                                        "Noch zu zahlen", "Offener Betrag", "Zu zahlen"]),
        # Regionale und zweitsprachige Schlüssel (vocab.ALT_LABELS). **Nicht** `lang`:
        # das gehört den Familien (families.LANG, families.defaults setzt es auf "de"
        # zurück), und eine zweite Achse mit demselben Namen würde deren Übersetzung
        # überschreiben. `key_region` tauscht nur die Schlüsselwörter einer sonst
        # deutschen Vorlage — österreichisch, schweizerisch, und gelegentlich die
        # englische Exportrechnung eines deutschen Händlers.
        "key_region": rng.choices(["", "at", "ch", "en"], [93, 4, 2, 1], k=1)[0],
        # GTIN im Namensfeld statt in einer eigenen Spalte.
        # 14 %, nicht 10 %: auf schmalen Blättern (A5/B5) streicht `NARROW_COLUMNS` die
        # GTIN-Spalte, dort ist das Namensfeld die *einzige* Quelle für `gtin`.
        "name_gtin": rng.random() < 0.14,
        "name_gtin_sep": rng.choice(["·", "-", "|", "/", ""]),
        "name_gtin_key": rng.choice(["GTIN", "EAN", "EAN:", "GTIN:", "Art.EAN"]),
        # Preiseinheit im Preisfeld: "12,50 / 100 g".
        "basis_inline": rng.random() < 0.08,
        # UN/ECE-Code am Namensende, ohne Leerzeichen: "Rum 0,7lH87".
        "name_unit_code": rng.random() < 0.03,
        # "19", "19 %", "19,00", "19,00 %", "19,00%" — die letzte Form hat das Modell
        # auf einem echten Beleg reihenweise als unitPrice gelesen.
        "vat_pct_form": rng.choices(["plain", "sign", "d2", "d2sign", "d2glued"],
                                    [40, 18, 14, 16, 12], k=1)[0],
        "discount_form": rng.choices(["pct", "pctsign", "amount"], [46, 26, 28], k=1)[0],
        # Beschrifteter Lieferant: "Firmenname: Brückner Spirituosen & Barbedarf GmbH".
        "sender_keyed": rng.random() < 0.12,
        "sender_key": rng.choice(["Firmenname", "Lieferant", "Name", "Rechnungssteller",
                                  "Verkäufer", "Firma"]),
        # Fließtext unter dem Summenblock und in der Fußzeile: `O` mit Rolle `footer`.
        "thanks_note": rng.choice(THANKS_POOL) if rng.random() < 0.35 else "",
        "footer_prose": rng.choice(THANKS_POOL) if rng.random() < 0.22 else "",

        # ------------------------------------------------ v11, Werbesatz (tagline)
        # Der gemischt gesetzte Werbesatz *im Briefkopf*, neben oder unter dem Namen
        # (`vocab.SLOGANS`, `meta["slogan"]`). Bis hierher druckte der Korpus den
        # Werbesatz nur als VERSALZEILE oben rechts (`blocks.head_block`, `CLAIMS`);
        # eine gemischt gesetzte Zeile direkt am Namen war nie `O`, und das Modell
        # hat sie auf echten Belegen an den `supplier` gehängt. Jedes Wort ist `O`,
        # und der Satz steht immer in einem eigenen Span — nie in einem
        # `supplier`-Lauf.
        #
        # Der Wurf steht *vor* `FORCE_TAGLINE`, damit die Achse den Zufallsstrom
        # nicht verschiebt: ein erzwungener Korpus unterscheidet sich vom freien
        # dann nur in dieser Achse und nicht in jeder folgenden.
        "tagline_axis": (rng.random() < 0.30) or FORCE_TAGLINE,
        # Wo er steht. `sender` — eigene Zeile direkt unter dem Namen;
        # `sender_owner` — eigene Zeile unter der Inhaberzeile; `owner` — in
        # derselben Zeile neben der Inhaberzeile ("Inh. Georg Hofmann   Fleisch und
        # Wurst aus eigener Schlachtung"), genau die Form, die das Modell gekostet
        # hat; `logo` — Subzeile unter der Wortmarke; `sender_caps` — die
        # VERSALFORM (`meta["claim"]`) im Absenderblock statt oben rechts.
        # Aufgelöst wird die Wahl nach `apply_family`, weil Familien `logo` und
        # `sender_place` überschreiben.
        "tagline_place": rng.choices(
            ["sender", "sender_owner", "owner", "logo", "sender_caps"],
            [32, 12, 26, 22, 8], k=1)[0],
        "tagline_style": rng.choices(
            ["plain", "italic", "accent", "accent_italic", "smallcaps", "light"],
            [28, 22, 14, 10, 14, 12], k=1)[0],
    }
    # Kopf und Ausrichtung der v11-Bruttospalte. Sie stehen hier statt in `LABELS`,
    # damit die Spaltentabelle unverändert bleibt.
    spec["headers"]["brutto"] = rng.choice(["Brutto", "Bruttobetrag", "Gesamt brutto",
                                            "Betrag brutto", "inkl. MwSt", "Brutto EUR"])
    spec["align"]["brutto"] = rng.choice(["right", "right", "right", "center"])
    # Die neuen Achsen von v11 stehen in `families.defaults` und sind dort alle
    # auf "druckt nichts Neues" vorbelegt: nur eine Familie (oder eine Belegart)
    # schaltet sie ein. Sonst wanderte der halbe Korpus in die neuen Formen ab.
    spec.update(families.defaults(rng))
    spec["doctype"] = doctype
    apply_family(spec, family, rng)

    # Abhängigkeiten, die erst nach der Familie feststehen: sie darf `meta_style`,
    # `logo`, `sender_place` und die Überschrift überschrieben haben.
    if spec["meta_style"] == "title":
        spec["title_number"] = True
        spec["title_date"] = True
    elif spec["meta_style"] == "dateline" and rng.random() < 0.5:
        spec["title_number"] = True
    spec["dateline"] = spec["meta_style"] == "dateline"
    if spec["title_number"]:
        spec["title_style"] = spec["title_style"] if spec["title_style"] in (
            "letter", "smallcaps", "underline") else rng.choice(
            ["plain", "plain", "underline", "letter", "smallcaps"])
    elif spec["title_style"] == "plain" and not fixed.get("title_style"):
        spec["title_style"] = rng.choices(TITLE_STYLES, TITLE_WEIGHTS, k=1)[0]
    spec["title_spaced_key"] = spec["title_spaced_key"] and spec["title_number"]
    # "row" legt die Kopfdaten als eine Zeile nicht umbrechender Zellen an. Das
    # ist die breiteste Variante und passt auf A5 bei keiner Schriftgröße mehr;
    # `render.build` schrumpft dann bis an den Font-Boden und die Rechnungsnummer
    # wird trotzdem abgeschnitten.
    if narrow_page and spec["totals_style"] == "grid":
        # Das Summenraster stellt Warenwert, jeden Zuschlag, den Nettobetrag, jeden
        # Steuersatz und die Summe *nebeneinander*, jede Zelle nicht umbrechend. Auf
        # 148 mm passt das bei keinem Schriftgrad; `render.build` schrumpft bis an den
        # Font-Boden, und der letzte Betrag wird trotzdem am Blattrand abgeschnitten —
        # dieselbe Falle wie `meta_style: row` in v9.
        spec["totals_style"] = rng.choice(["block", "boxed", "table", "panel"])
    if narrow_page and spec["meta_style"] == "row":
        spec["meta_style"] = rng.choice(["pairs", "stack", "boxed", "grid"])
    spec["meta_table"] = spec["meta_style"] if spec["meta_style"] in (
        "pairs", "stack", "boxed", "row", "stacked", "grid", "line") else rng.choice(
        ["pairs", "stack", "boxed"])
    if spec["meta_table"] in ("row", "grid", "line") and spec["meta_place"] in ("top_right", "split"):
        spec["meta_place"] = rng.choice(["under_title", "panel", "bottom"])
    # ----------------------------------------- v11, Werbesatz: Platz auflösen
    # Erst hier, weil die Familie `logo` und `sender_place` überschrieben haben kann.
    # Der Bon hat keinen Briefkopf, druckt den Satz aber unter dem Namen im Bonkopf;
    # der E-Rechnungs-Viewer hat weder Briefkopf noch Werbesatz.
    if spec["einvoice"]:
        spec["tagline_axis"] = False
    elif receipt:
        spec["tagline_place"] = "receipt"
    elif spec["tagline_axis"]:
        if spec["tagline_place"] == "logo" and spec["logo"] not in ("wordmark", "wordmark2"):
            spec["tagline_place"] = "sender"
        if spec["tagline_place"] == "sender_caps" and spec["head_contact"] == "block":
            # `head_block` druckt den VERSALSATZ schon oben rechts; zweimal derselbe
            # Satz auf einer Seite ist kein Ablenker mehr, sondern eine Dublette.
            spec["tagline_place"] = "sender"
        if spec["tagline_place"] != "logo" and spec["sender_place"] == "none":
            if spec["logo"] in ("wordmark", "wordmark2"):
                spec["tagline_place"] = "logo"
            elif FORCE_TAGLINE:
                # Erzwungener Korpus: ohne Absender und ohne Wortmarke hat der
                # Briefkopf keine Stelle für den Satz. Dann bekommt die Vorlage
                # einen Absenderblock — die einzige Achse, die `--force` anfasst.
                spec["sender_place"] = "under"
            else:
                spec["tagline_axis"] = False
        if spec["tagline_place"] in ("owner", "sender_owner"):
            # Die Zeile, an der das Modell gescheitert ist, braucht die Inhaberzeile.
            spec["show_owner"] = True
        elif spec["tagline_place"] in ("sender", "sender_caps") and rng.random() < 0.45:
            # "Inhaberzeile bleibt häufig, auch wenn ein Werbesatz danebensteht":
            # sonst lernte das Modell "Werbesatz statt Inhaberzeile".
            spec["show_owner"] = True
    if not spec["tagline_axis"]:
        spec["tagline_place"] = ""
    spec["footer_supplier"] = spec["sender_place"] == "none" and spec["logo"] not in (
        "wordmark", "wordmark2")
    if spec["footer_supplier"]:
        # Kein Absender und keine Wortmarke: dann steht der Lieferantenname nur
        # noch in der Fußzeile, und nur dort darf er dann beschriftet sein. Die
        # Spaltenfußzeilen drucken ihn gar nicht — also eine, die ihn druckt.
        spec["footer"] = rng.choice(["line", "address"])
    if spec["no_header_row"] and not spec["caption"]:
        # Ohne Kopfzeile trägt eine Überschriftzeile über der Tabelle den Platz —
        # sonst beginnt die Tabelle im Nichts.
        spec["caption"] = rng.choice(CAPTIONS) if rng.random() < 0.6 else ""
    # Wortlose Deko am oberen Rand nur, wenn dort nichts steht.
    decor_top_free = (not spec["contacts"] and spec["meta_place"] != "top_right"
                      and spec["sender_place"] != "right" and spec["head_contact"] != "block")
    spec["decor_qr"] = (rng.choice(["bl", "tr"] if decor_top_free else ["bl"])
                        if rng.random() < 0.1 else "")
    if spec["decor_barcode"] == "tr" and not decor_top_free:
        spec["decor_barcode"] = "br"
    if spec["multi_row"] != "none":
        # Wer Preise in eine eigene Zeile setzt, setzt nicht auch noch 5 mm
        # Zellenabstand darunter — sonst stehen sieben Positionen auf einer A4-Seite.
        spec["cell_pad_y"] = round(min(spec["cell_pad_y"], 2.2), 1)
    if receipt:
        # Eine Rolle wird abgeschnitten, nicht auf ein Format gedruckt.
        spec["page_h_mm"] = round(rng.uniform(150.0, 285.0), 1)
        spec["bg_art"] = ""
        spec["head_band"] = False
        spec["totals_bottom"] = False
        spec["decor_barcode"] = rng.choice(["", "bl", "bl"])
        spec["decor_qr"] = "bl" if rng.random() < 0.3 else ""
        spec["page_margin"] = (round(rng.uniform(2.5, 5.0), 1), round(rng.uniform(4.0, 9.0), 1))
        spec["size"] = round(rng.uniform(6.6, 8.6), 2)
        spec["title_align"] = "center"
        spec["meta_place"] = "under_title"
        spec["totals_side"] = "full"
        spec["multi_row"] = "none"
        spec["rc_amount_row"] = rng.random() < 0.45
        spec["rc_rule"] = rng.choice(["dash", "line", "none", "dash"])
        spec["rc_articleid"] = rng.random() < 0.6
    # --------------------------------------------------- Abhängigkeiten von v11
    if spec["lang"] != "de":
        families.apply_lang(spec, rng)
    if spec["hand"]:
        # Die Quittung vom Wochenmarkt ist handgeschrieben. Eine Display-Schrift
        # als *Fließschrift* ist sonst nirgends erlaubt — hier ist sie der Punkt.
        spec["font"] = rng.choice(families.HAND)
        spec["head_font"] = spec["font"]
        spec["table_style"] = "borderless"
        spec["bg_art"] = ""
    if spec["einvoice"]:
        # Der Viewer-Ausdruck baut die ganze Seite selbst: kein Briefkopf, keine
        # Anschrift, keine Meta-Tabelle. Was davon stehen bliebe, druckte den
        # Lieferanten ein zweites Mal.
        spec["item_form"] = "table"
        spec["sidebar"] = ""
        spec["giant_logo"] = False
        spec["head_band"] = False
        spec["tint_panel"] = False
        spec["decor_qr"] = ""
        spec["decor_stamp"] = False
        spec["totals_bottom"] = False
        spec["multi_row"] = "none"
        spec["info_rows"] = False
        spec["group_headings"] = False
        spec["second_row_details"] = False
        spec["no_header_row"] = False
        spec["caption"] = ""
        spec["bg_art"] = ""
        # Der Verkäuferblock trägt den Lieferanten beschriftet; die Fußzeile darf
        # ihn dann nicht ein zweites Mal als `supplier` führen.
        spec["footer_supplier"] = False
    if spec["item_form"] != "table":
        # Zweispaltig und Schlüssel-Wert vertragen keine mehrzeilige Position:
        # beide Formen setzen die Position selbst schon über mehrere Zeilen.
        spec["multi_row"] = "none"
        if spec["item_form"] == "kv":
            spec["no_header_row"] = True
            spec["group_headings"] = False
            spec["second_row_details"] = False
    if spec["sidebar"]:
        # Der Streifen frisst 26 mm Blattbreite; ein Barcode an derselben Kante
        # läge darunter.
        spec["decor_barcode"] = ""
        spec["bg_place"] = "table"
    if spec["qr_bill"]:
        # Die QR-Rechnung sitzt absolut am Blattfuß, wie die Fußzeile — beides
        # zusammen passt nicht.
        spec["footer"] = "line" if spec["footer_supplier"] else "none"
        spec["decor_barcode"] = ""
    if narrow_page or receipt:
        # Der Zeitraumblock stellt Beschriftung und zwei Daten in eine nicht
        # umbrechende Zeile; auf 148 mm passt die längste Form nicht, und
        # `render.build` schrumpft dann bis an den Schriftboden, ohne dass die
        # Zeile je hineinpasst.
        spec["period_block"] = "range" if spec["period_block"] else ""
        spec["qr_bill"] = False
        spec["sidebar"] = ""
        spec["item_form"] = "table" if spec["item_form"] == "twocol" else spec["item_form"]
    if spec["title_text"]:
        spec["title"] = spec["title_text"]
    return spec


def fit(spec, meta):
    columns = list(spec["columns"])
    if meta.get("no_vat"):
        # Kleinunternehmer und Reverse Charge weisen NIRGENDS Umsatzsteuer aus:
        # keine MwSt-Spalte, kein Steuerschlüssel. Die Steuerzeile im
        # Summenblock entfällt von selbst, weil `vatBreakdown` leer ist.
        columns = [c for c in columns if c not in ("mwst", "steuercode")]
    if meta["kind"] == "delivery_note":
        columns = [c for c in columns if c not in PRICE_COLUMNS]
    elif meta["needs_discount_column"]:
        if "rabatt" not in columns:
            columns.insert(max(1, columns.index("betrag")), "rabatt")
    else:
        columns = [c for c in columns if c != "rabatt"]
    if not any(l["priceBaseText"] for l in meta["render_lines"]):
        columns = [c for c in columns if c != "basis"]
    if not any(l["gtin"] for l in meta["render_lines"]):
        columns = [c for c in columns if c != "gtin"]
    if not any(l["sellerArticleId"] for l in meta["render_lines"]):
        columns = [c for c in columns if c != "artikel"]
    if not any(l["variant"] for l in meta["render_lines"]):
        columns = [c for c in columns if c != "groesse"]
    # Druckt die Rechnung gar keinen Einheitentext (nackte Mengen), dann gibt es
    # auch keine Einheitenspalte — und `glue_unit` klebt nichts an die Menge.
    if not any(l["unitText"] for l in meta["render_lines"]):
        columns = [c for c in columns if c != "einheit"]
    out = dict(spec)
    out["columns"] = columns
    out["glue_unit"] = "einheit" not in columns
    return out
