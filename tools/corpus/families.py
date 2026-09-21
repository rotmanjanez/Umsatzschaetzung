"""v11: benannte Vorlagenfamilien und die Bausteine, die sie brauchen.

v10 hatte zehn Familien und der Rest war freier Wurf. Das war der größte Gewinn
von v1 auf v10, also wird er hier vervielfacht: rund hundert benannte Familien,
jede nach einem echten Erzeuger oder einer echten Belegart modelliert. Eine
Familie legt die Achsen fest, die sie ausmachen, und lässt alles andere gewürfelt
— `free` bleibt mit Abstand der größte Anteil (>= 25 %), damit der Raum geordnet
und nicht enger wird.

Warum ein eigenes Modul: `blocks.py`, `content.py` und `validate.py` gehören in
dieser Runde dem Labels-Agenten. Alles Neue steht deshalb hier, `layout.py` zieht
die Familien und die neuen Achsen daraus, `render.py` ruft die neuen Bausteine.
Dieses Modul darf `blocks` und `money` importieren, aber **nicht** `layout` —
`layout` importiert es, ein Zyklus wäre die Folge.

Belegarten, die *Zahlen* ändern (Kleinunternehmer, Reverse Charge, Schweiz),
können keine Familie je Variation sein: `expected.json` wird in
`generate.one()` **einmal je Rechnung** geschrieben, die Familie aber je
Variation gezogen. Sie sind deshalb eine eigene Achse `doctype`, die je Rechnung
fällt (`DOCTYPES`, `adapt()`), und ihre Familien tragen `weight: 0` — sie werden
nie zufällig gezogen, sondern nur von ihrer Belegart.
"""
import zlib

import blocks as B
import money

# --------------------------------------------------------------- Sprachen
#
# Eine englische Rechnung an einen deutschen Käufer ist im Gastro-Einkauf Alltag
# (Spirituosen, Kaffee, Technik), und niederländische, italienische und polnische
# Lieferanten drucken ihre eigenen Schlüssel. Getauscht werden nur die *Wörter*,
# nicht die Struktur — die Klassen bleiben dieselben.
LANG = {
    "en": {
        "title": {"invoice": "Invoice", "credit": "Credit Note", "delivery_note": "Delivery Note"},
        "number": ["Invoice No.", "Invoice Number", "Invoice #", "Document No."],
        "date": ["Invoice Date", "Date", "Date of Issue"],
        "customer": ["Customer No.", "Account No.", "Client ID"],
        "order": ["Order No.", "PO Number", "Your Order"],
        "delivery": ["Delivery Date", "Ship Date", "Service Date"],
        "due": ["Due Date", "Payment Due", "Terms"],
        "net": ["Subtotal", "Net Total", "Total excl. VAT", "Net amount"],
        "gross": ["Total", "Total Due", "Grand Total", "Total incl. VAT"],
        "vat": ["VAT", "Tax", "VAT at", "Sales Tax"],
        "due_amount": ["Amount Due", "Balance Due", "Total Due"],
        "subtotal": ["Subtotal", "Items total", "Goods value"],
        "headers": {"pos": ["#", "Item", "No."], "artikel": ["Item No.", "SKU", "Code"],
                    "gtin": ["EAN", "GTIN", "Barcode"], "groesse": ["Size", "Variant", "Option"],
                    "name": ["Description", "Item", "Product", "Details"],
                    "menge": ["Qty", "Quantity", "Units"],
                    "einheit": ["Unit", "UoM", "Each"], "basis": ["Per", "Price per", "Base"],
                    "preis": ["Unit Price", "Rate", "Price", "Price/Unit"],
                    "rabatt": ["Disc.", "Discount", "Disc %"],
                    "mwst": ["VAT", "Tax", "VAT %", "Rate"],
                    "betrag": ["Amount", "Total", "Line Total", "Net"],
                    "waehrung": ["Cur.", "CUR", "Ccy"], "steuercode": ["TC", "Tax", "Code"],
                    "wg": ["Grp", "PG", "Group"]},
        "pay": "Payable within 14 days without deduction.",
    },
    "nl": {
        "title": {"invoice": "Factuur", "credit": "Creditnota", "delivery_note": "Pakbon"},
        "number": ["Factuurnummer", "Factuurnr.", "Nummer"],
        "date": ["Factuurdatum", "Datum"],
        "customer": ["Klantnummer", "Debiteurnr."],
        "order": ["Ordernummer", "Uw order"],
        "delivery": ["Leverdatum", "Verzenddatum"],
        "due": ["Vervaldatum", "Te betalen voor"],
        "net": ["Subtotaal", "Totaal excl. btw", "Netto"],
        "gross": ["Totaal", "Totaal incl. btw", "Te betalen"],
        "vat": ["Btw", "BTW", "Btw-bedrag"],
        "due_amount": ["Te betalen", "Openstaand bedrag"],
        "subtotal": ["Subtotaal", "Goederenwaarde"],
        "headers": {"pos": ["Nr.", "#"], "artikel": ["Artikelnr.", "Code"],
                    "gtin": ["EAN"], "groesse": ["Maat", "Variant"],
                    "name": ["Omschrijving", "Artikel"], "menge": ["Aantal", "Hoev."],
                    "einheit": ["Eenheid", "Eh."], "basis": ["Per"],
                    "preis": ["Prijs", "Stukprijs", "Prijs/eh."], "rabatt": ["Korting"],
                    "mwst": ["Btw", "Btw %"], "betrag": ["Bedrag", "Totaal"],
                    "waehrung": ["Val."], "steuercode": ["Cd"], "wg": ["Grp"]},
        "pay": "Betaling binnen 14 dagen na factuurdatum.",
    },
    "it": {
        "title": {"invoice": "Fattura", "credit": "Nota di credito",
                  "delivery_note": "Documento di trasporto"},
        "number": ["Numero fattura", "Fattura n.", "N. documento"],
        "date": ["Data fattura", "Data", "Data documento"],
        "customer": ["Codice cliente", "Cliente n."],
        "order": ["Ordine n.", "Vostro ordine"],
        "delivery": ["Data consegna", "Data DDT"],
        "due": ["Scadenza", "Pagamento entro"],
        "net": ["Imponibile", "Totale imponibile", "Netto"],
        "gross": ["Totale documento", "Totale", "Totale fattura"],
        "vat": ["IVA", "Imposta", "IVA al"],
        "due_amount": ["Importo dovuto", "Netto a pagare"],
        "subtotal": ["Totale merce", "Subtotale"],
        "headers": {"pos": ["Pos.", "Rif."], "artikel": ["Codice", "Cod. art."],
                    "gtin": ["EAN"], "groesse": ["Misura", "Variante"],
                    "name": ["Descrizione", "Articolo"], "menge": ["Q.tà", "Quantità"],
                    "einheit": ["UM", "U.M."], "basis": ["Per"],
                    "preis": ["Prezzo", "Prezzo unit.", "Prz. un."], "rabatt": ["Sconto"],
                    "mwst": ["IVA", "IVA %"], "betrag": ["Importo", "Totale"],
                    "waehrung": ["Val."], "steuercode": ["Cd"], "wg": ["Gr"]},
        "pay": "Pagamento a 30 giorni data fattura.",
    },
    "pl": {
        "title": {"invoice": "Faktura", "credit": "Faktura korygująca",
                  "delivery_note": "Dokument WZ"},
        "number": ["Numer faktury", "Faktura nr", "Nr dokumentu"],
        "date": ["Data wystawienia", "Data"],
        "customer": ["Nr klienta", "Kod kontrahenta"],
        "order": ["Nr zamówienia", "Zamówienie"],
        "delivery": ["Data dostawy", "Data sprzedaży"],
        "due": ["Termin płatności"],
        "net": ["Wartość netto", "Razem netto", "Netto"],
        "gross": ["Do zapłaty", "Razem brutto", "Brutto"],
        "vat": ["VAT", "Podatek VAT", "Stawka VAT"],
        "due_amount": ["Do zapłaty", "Pozostało do zapłaty"],
        "subtotal": ["Wartość towarów", "Suma pozycji"],
        "headers": {"pos": ["Lp.", "Poz."], "artikel": ["Indeks", "Kod"],
                    "gtin": ["EAN"], "groesse": ["Rozmiar"],
                    "name": ["Nazwa towaru", "Nazwa", "Opis"], "menge": ["Ilość"],
                    "einheit": ["J.m.", "jm"], "basis": ["Za"],
                    "preis": ["Cena netto", "Cena jedn.", "Cena"], "rabatt": ["Rabat"],
                    "mwst": ["VAT", "VAT %"], "betrag": ["Wartość", "Netto"],
                    "waehrung": ["Wal."], "steuercode": ["Kd"], "wg": ["Gr"]},
        "pay": "Płatność przelewem w terminie 14 dni.",
    },
}
LANGS = sorted(LANG)


# ------------------------------------------------------------ Rechtshinweise
#
# Beschriftung `otherLabel` gibt es hier nicht: das sind Sätze, keine Schlüssel.
# Sie stehen komplett auf `O` — und genau deshalb sind sie wertvoll. Ein Satz mit
# "Umsatzsteuer", "Betrag" und einer Paragrafenziffer mitten darin ist der
# härteste Ablenker, den eine Rechnung zu bieten hat.
NOTES = {
    "kleinunternehmer": [
        "Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.",
        "Im Rechnungsbetrag ist gem. § 19 Abs. 1 UStG keine Umsatzsteuer enthalten.",
        "Kleinunternehmer im Sinne von § 19 UStG – kein Ausweis der Umsatzsteuer.",
    ],
    "reverse_charge": [
        "Steuerschuldnerschaft des Leistungsempfängers (§ 13b UStG).",
        "Reverse Charge – Steuerschuldnerschaft des Leistungsempfängers.",
        "Umkehr der Steuerschuld gem. Art. 196 MwSt-Systemrichtlinie.",
    ],
    "b13b_bau": [
        "Bauleistung nach § 13b Abs. 2 Nr. 4 UStG – Steuerschuldner ist der "
        "Leistungsempfänger.",
    ],
    "s35a": [
        "Im Rechnungsbetrag sind Lohnkosten nach § 35a EStG enthalten, die "
        "steuerlich geltend gemacht werden können.",
        "Nach § 35a EStG begünstigte Arbeitskosten: siehe Ausweis oben.",
    ],
    "innergemein": [
        "Steuerfreie innergemeinschaftliche Lieferung gem. § 4 Nr. 1b i.V.m. § 6a UStG.",
    ],
    "proforma": [
        "Diese Proforma-Rechnung ist keine Rechnung im Sinne des § 14 UStG und "
        "berechtigt nicht zum Vorsteuerabzug.",
        "Proforma – nur zu Zoll- und Informationszwecken, keine Zahlungsaufforderung.",
    ],
    "abschlag": [
        "Die geleisteten Abschlagszahlungen wurden in Abzug gebracht.",
        "Bereits abgerechnete Abschläge sind oben abgesetzt.",
    ],
    "gutschrift": [
        "Der Betrag wird Ihrem Konto gutgeschrieben. Eine Zahlung ist nicht erforderlich.",
        "Diese Rechnungskorrektur ersetzt die ursprüngliche Rechnung anteilig.",
    ],
    "arzt": [
        "Die Leistungen sind nach § 4 Nr. 14 UStG von der Umsatzsteuer befreit.",
        "Abrechnung nach der Gebührenordnung für Ärzte (GOÄ).",
    ],
    "miete": [
        "Umsatzsteuerfreie Vermietung gem. § 4 Nr. 12a UStG.",
    ],
    "versicherung": [
        "Versicherungsteuer 19 % gem. VersStG; kein Vorsteuerabzug möglich.",
    ],
    "bewirtung": [
        "Bewirtungsbeleg – Anlass und Teilnehmer sind auf der Rückseite zu vermerken.",
    ],
    "pfand": [
        "Pfandbeträge sind gemäß § 10 UStG im Entgelt enthalten.",
    ],
    "eigentum": [
        "Die Ware bleibt bis zur vollständigen Bezahlung unser Eigentum.",
    ],
    "swiss": [
        "MWST-Nr. CHE-116.281.277 MWST – Beträge in Schweizer Franken.",
    ],
    "en_vat": [
        "VAT is due under the reverse charge procedure, Art. 196 Council Directive 2006/112/EC.",
        "Please quote the invoice number with your payment.",
    ],
    "xrechnung": [
        "Dieses Dokument ist die visuelle Darstellung einer elektronischen Rechnung "
        "nach EN 16931 (XRechnung). Maßgeblich ist der XML-Datensatz.",
        "Visualisierung der übermittelten XRechnung. Der strukturierte Datensatz ist "
        "Bestandteil dieser Rechnung.",
    ],
}


# ------------------------------------------------------- Neue Achsen der Vorlage
#
# Jede davon steht am Ende in truth.json `layout` und ist damit auswertbar. Die
# Vorgabe ist immer die, die *keine* neue Form druckt — nur eine Familie (oder
# eine Belegart) schaltet sie ein. Sonst wanderte der halbe Korpus in die neuen
# Formen ab und der alte Raum verschwände.
def defaults(rng):
    return {
        # Der E-Rechnungs-Viewer-Ausdruck: "", "xrechnung", "zugferd", "portal", "peppol".
        "einvoice": "",
        # Sprache der Schlüssel und Spaltenköpfe.
        "lang": "de",
        # Belegart; wird in generate.py je *Rechnung* gezogen, nicht je Variation.
        "doctype": "plain",
        # Rechtshinweise als Fließsatz (Schlüssel aus NOTES), alles `O`.
        "doc_note": [],
        "note_place": rng.choice(["under_totals", "under_table", "footer"]),
        # Zeitraumzeilen: Strom/Gas/Miete/Versicherung/Hotel drucken sie, Handel nicht.
        "period_block": "",
        # Vorauszahlungen und der Restbetrag darunter -> amountDue.
        "prepaid": "",
        # Abschnittsüberschriften in der Tabelle, die aus der Belegart kommen.
        "sections": "",
        # Positionen als Tabelle, zweispaltig nebeneinander oder als Schlüssel-Wert-Block.
        "item_form": "table",
        # Senkrechter Streifen am Blattrand.
        "sidebar": "",
        # Nadeldrucker auf Endlospapier.
        "dotmatrix": False,
        # Riesenlogo über die halbe Blattbreite.
        "giant_logo": False,
        # Schweizer QR-Rechnung am Blattfuß.
        "qr_bill": False,
        # Käuferblock VOR dem Verkäuferblock — die Normalform jedes E-Rechnungs-Viewers.
        "buyer_first": False,
        # Feste Überschrift statt der gezogenen (Proforma, Bewirtungsbeleg, ...).
        "title_text": "",
        # Gedruckte Währung. Die Beträge bleiben, wie `content.py` sie gerechnet hat.
        "currency": "EUR",
        # Dicke Linie unter dem Briefkopf.
        "head_rule": False,
        # Unterstrichene Formularfelder statt gerahmter.
        "form_fields": False,
        # Handschrift statt Satzschrift (Quittung vom Wochenmarkt).
        "hand": False,
        # Eine Zeile "Bewirtungsbeleg"-Felder (Anlass, Teilnehmer) unter den Summen.
        "host_fields": False,

        # ------------------------------------------------------------------ v12
        # Die Einheit IM Preiskopf: "Preis je Fl", "Preis je kg", "EP/kg", "EUR/Stk",
        # und zweizeilig "Preis je" / "kg bzw. Stueck". Ausgespielt wird sie in
        # `layout.apply_price_header`, weil erst dort das Einheitenwort der
        # Positionen bekannt ist. 12 % frei gezogen, dazu die Familien, die sie
        # fest setzen (Weingut, Metzgerei, Fisch, Kaese, Roesterei ...) — zusammen
        # ueber der Untergrenze von 8 % aus PLAN.md.
        "price_header_unit": rng.choices(["", "je", "pro", "ep", "cur", "two"],
                                         [88, 4, 1, 3, 2, 2], k=1)[0],
        # Menge und Einheit ohne Leerzeichen: "17Fl", "10XBO", "5,450kg". Greift nur
        # bei `glue_unit` (keine eigene Einheitenspalte); `layout.fit` schaltet sie
        # sonst ab. Gelesen wird sie von `blocks.item_cells` — bis der Content-Agent
        # das vierte Feld durchreicht (Bitte 2 in STATUS-families.md), steht sie nur
        # in der Wahrheit und aendert nichts.
        "qty_unit_glue": rng.random() < 0.35,
        # XRechnung-Viewer: die nackte Positionszeile (Name ohne den Schluessel
        # "Bezeichnung:", darunter kursive Unterzeilen) statt der Schluesselform.
        # Der echte KoSIT-Ausdruck kennt nur die nackte Form.
        "ei_bare": rng.random() < 0.72,
        # XRechnung-Viewer: der Drei-Seiten-Schnitt Uebersicht / Details / Zusaetze.
        "ei_split": rng.random() < 0.40,
        # NICHT vorhanden und mit Absicht nicht angelegt: `vat_letter` (Steuerbuchstabe
        # je Position), `deposit_column` (Pfand-/Leergutspalte) und `day_columns`
        # (Tagesspalten des Baeckerei-Lieferscheins). Alle drei brauchen eine neue
        # Zelle in `blocks.item_cells`, und `blocks.py` gehoert in dieser Runde dem
        # Content-Agenten. Eine Achse, die in truth.json steht und nichts druckt,
        # laesst `coverage.py` einen Anteil melden, der nicht auf der Seite ist —
        # also wird sie gar nicht erst gezogen.
    }


# `apply_family` zieht aus einer Liste; eine Achse, deren *Wert* eine Liste ist,
# muss deshalb als Tupel stehen (fest) oder als Liste von Tupeln (eine davon).
LIST_AXES = {"total_extras", "contacts", "doc_note", "meta_keys"}

# Handschrift fuer die Quittung vom Wochenmarkt. Nur was auf dem Rechner wirklich
# installiert ist (`fc-list`) und wirklich geschrieben aussieht — `layout.DISPLAY`
# fuehrt auch Slab- und Kapitaelchenschriften, und die gaeben eine gedruckte
# Quittung statt einer geschriebenen.
HAND = ["Z003, cursive", "'TeX Gyre Chorus', cursive", "'Comic Neue', cursive",
        "'Lobster Two', cursive"]

# Die Belegarten, die Zahlen verändern. Sie fallen je Rechnung in `generate.py`,
# nicht je Variation, weil `expected.json` je Rechnung geschrieben wird.
DOCTYPES = (["plain", "kleinunternehmer", "reverse_charge", "swiss"], [90, 4, 3, 3])

# ------------------------------------------------------------------ Die Familien
#
# Jede Familie nennt im Kommentar ihr Vorbild und ihre Merkmale. Die Gewichte sind
# relativ; `layout.FAMILIES["free"]["weight"]` haelt die freie Ziehung bei >= 25 %.
#
# A) Rechnungsprogramme -------------------------------------------------------
EXTRA_FAMILIES = {
    # sevDesk: helles Blau, Grotesk, gerahmter Kopfblock rechts, Gittertabelle,
    # Summen als Tabelle rechts, dreispaltige Fusszeile mit Ueberschriften.
    "sevdesk": {"weight": 1.1, "set": {
        "font_pool": "sans", "accent": ["#0f6fc6", "#1f7ac4"], "meta_style": "boxed",
        "meta_place": "top_right", "meta_colon": "glued", "table_style": ["grid", "rules"],
        "header_style": ["bold", "accent"], "totals_style": "table", "totals_side": "right",
        "logo": ["mark", "wordmark"], "logo_side": "left", "footer": "columns3",
        "footer_heads": True, "title_style": "plain", "title_align": "left", "bg_art": ""}},
    # Lexoffice: sehr aufgeraeumt, duenne Linien, Kopfraster unter der Ueberschrift,
    # fetter Bruttobetrag, winzige einzeilige Fusszeile.
    "lexoffice": {"weight": 1.1, "set": {
        "font_pool": "sans", "accent": ["#00a0a0", "#1b6b5a"], "meta_style": "grid",
        "meta_place": "under_title", "grid_cols": [3, 4], "grid_box": False,
        "table_style": ["rules", "borderless"], "header_style": ["plain", "underline"],
        "totals_style": ["block", "table"], "totals_side": "right", "logo": ["wordmark", "none"],
        "footer": ["line", "columns3"], "footer_size": [0.6, 0.64], "rule_light": "#bbb",
        "cell_pad_y": [1.4, 1.8], "title_style": "plain"}},
    # easybill: Akzentband unter dem Briefkopf, Zebratabelle, Kopfdaten als Paare
    # rechts oben, Summen in einem getoenten Panel.
    "easybill": {"weight": 0.9, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#c8451e", "#a33a12"],
        "meta_style": "pairs", "meta_place": "top_right", "table_style": "zebra",
        "header_style": ["inverted", "bold"], "totals_style": "panel", "totals_shade": True,
        "logo": ["wordmark", "mark"], "footer": ["columns2", "columns3"]}},
    # Billomat: Wortmarke links gross, linienlose Tabelle, Summen in einem Kasten,
    # Positionsnummern mit Punkt.
    "billomat": {"weight": 0.8, "set": {
        "font_pool": "sans", "logo": "wordmark", "logo_side": "left", "wordmark_lines": 1,
        "table_style": "borderless", "header_style": ["letterspaced", "smallcaps"],
        "totals_style": "boxed", "totals_side": "right", "pos_format": "dot",
        "meta_style": ["stacked", "pairs"], "meta_place": "top_right", "footer": "columns2"}},
    # FastBill: schmale Grotesk, invertierte Kopfzeile, Summenraster, Kopfdaten als
    # eine Zeile ueber der Tabelle.
    "fastbill": {"weight": 0.8, "set": {
        "font_pool": ["condensed", "sans"], "header_style": "inverted",
        "table_style": ["rules", "zebra"], "totals_style": ["grid", "table"],
        "meta_style": ["row", "grid"], "meta_place": "under_title", "logo": ["wordmark", "band"],
        "accent": ["#1f3864", "#0f4c81"], "size": [7.6, 8.0, 8.4], "footer": "line"}},
    # Zervant: fast nichts — kein Logo, keine Linien, viel Durchschuss, Kopfdaten
    # gestapelt. Die minimalistische Cloud-Rechnung.
    "zervant": {"weight": 0.7, "set": {
        "font_pool": "sans", "logo": "none", "sender_place": "right", "table_style": "borderless",
        "header_style": "plain", "leading": [1.5, 1.62, 1.72], "cell_pad_y": [2.4, 3.2],
        "meta_style": "stacked", "meta_place": "top_right", "totals_style": "block",
        "footer": ["none", "line"], "title_style": ["plain", "letter"], "bg_art": ""}},
    # orgaMAX: Serifen im Satz, Linientabelle, gerahmter Kopf, dreispaltige Fusszeile
    # mit Registerangaben — die klassische Windows-Warenwirtschaft.
    "orgamax": {"weight": 0.8, "set": {
        "font_pool": "serif", "meta_style": "boxed", "meta_place": "top_right",
        "table_style": "rules", "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "footer": "columns3", "footer_heads": True,
        "logo": ["mark", "none"], "pos_format": ["plain", "dot"]}},
    # WISO / Buhl: nuechterne Grotesk, Gittertabelle, Datumszeile statt Datumsfeld,
    # Summen als Tabelle, Seitenzahl unten mittig.
    "wiso": {"weight": 0.8, "set": {
        "font_pool": "sans", "meta_style": ["dateline", "pairs"], "table_style": "grid",
        "header_style": ["bold", "boxed"], "totals_style": "table", "pageno_place": "bc",
        "logo": ["none", "mark"], "footer": ["bank", "columns2"], "accent": "#3d3d3d"}},
    # Sage: gruener Akzent, Kopfzeile als Zeilenraster, Positionsnummern in
    # Zehnerschritten (10, 20, 30) — das ERP-Erbe.
    "sage": {"weight": 0.8, "set": {
        "font_pool": "sans", "accent": ["#1f5c2e", "#2f4f2f"], "meta_style": ["row", "grid"],
        "table_style": ["rules", "headrule"], "header_style": "accent", "pos_format": "step",
        "totals_style": ["table", "panel"], "logo": ["wordmark", "band"], "footer": "columns3"}},
    # Odoo: violetter Akzent, eine dicke Kopflinie, Kaeuferblock rechts oben,
    # Summen rechts mit fetter Endzeile, Positionsnummern fehlen.
    "odoo": {"weight": 0.9, "set": {
        "font_pool": "sans", "accent": ["#5a3d7a", "#4b2e83"], "table_style": "headrule",
        "header_style": ["bold", "accent"], "address_corner": "right", "meta_style": "grid",
        "meta_place": "under_title", "grid_cols": [3, 4], "totals_style": "block",
        "totals_side": "right", "logo": ["wordmark", "mark"], "footer": ["line", "columns2"],
        "title_style": ["plain", "letter"]}},
    # Xero: englische Rechnung, gesperrtes "TAX INVOICE", blaue Kopflinie,
    # Summen gross rechts, "Amount Due" als eigene Zeile.
    "xero": {"weight": 0.9, "set": {
        "lang": "en", "font_pool": "sans", "accent": ["#0f4c81", "#00566b"],
        "title_style": "letter", "title_align": ["left", "right"], "table_style": "headrule",
        "header_style": ["bold", "underline"], "totals_style": ["table", "block"],
        "prepaid": "balance", "meta_style": ["grid", "pairs"], "logo": ["wordmark", "mark"],
        "footer": ["line", "columns2"], "date_format": ["%d/%m/%Y", "%Y-%m-%d"]}},
    # QuickBooks: englisch, gruenes Band, "BALANCE DUE" in einem eigenen Kasten,
    # Positionsspalten Activity/Qty/Rate/Amount.
    "quickbooks": {"weight": 0.9, "set": {
        "lang": "en", "font_pool": "sans", "head_band": True, "accent": ["#1f5c2e", "#2f4f2f"],
        "table_style": ["rules", "zebra"], "header_style": ["inverted", "bold"],
        "totals_style": ["block", "boxed"], "prepaid": "balance", "meta_style": "grid",
        "grid_cols": 3, "logo": ["wordmark", "none"], "footer": ["line", "none"],
        "date_format": ["%m/%d/%Y", "%d/%m/%Y"]}},
    # Stripe: englisch, hellgraue Linien, Kennzahlenraster ganz oben
    # (Invoice number / Date due / Amount due), sehr viel Weissraum.
    "stripe": {"weight": 0.9, "set": {
        "lang": "en", "font_pool": "sans", "logo": ["wordmark", "none"], "logo_side": "left",
        "meta_style": "grid", "meta_place": "under_title", "grid_cols": [3, 4], "grid_box": False,
        "table_style": "rules", "header_style": ["plain", "underline"], "rule_light": "#ddd",
        "totals_style": "block", "totals_side": "right", "prepaid": "balance",
        "footer": ["line", "none"], "leading": [1.4, 1.5], "accent": "#5a3d7a",
        "title_style": "plain", "sender_place": ["under", "none"]}},
    # PayPal-Beleg: blaues Band, "Sie haben bezahlt", winzige Tabelle ohne
    # MwSt-Spalte, Transaktionscode in der Fusszeile.
    "paypal": {"weight": 0.7, "set": {
        "font_pool": "sans", "accent": ["#0f4c81", "#123f6d"], "head_band": True,
        "table_style": "borderless", "header_style": ["plain", "bodyrow"],
        "totals_style": ["block", "sentence"], "pay_box": True, "footer_code": True,
        "meta_style": ["stacked", "pairs"], "logo": ["band", "wordmark"], "footer": "line",
        "title_style": "plain", "size": [7.4, 7.8]}},
    # SumUp: Kartenterminal-Beleg, schmale Rolle, zentrierter Kopf, Betrag gross.
    "sumup": {"weight": 0.7, "set": {
        "page_format": "receipt", "font_pool": ["mono", "condensed"], "table_style": "borderless",
        "header_style": "plain", "logo": ["none", "wordmark"], "no_header_row": True,
        "footer": ["line", "none"], "accent": "#000000", "title_style": ["plain", "letter"],
        "doc_note": [("pfand",), ()]}},
    # Invoice Ninja, modernes Muster: farbige Kopfleiste, Kundenblock links unter
    # dem Logo, Summen als Panel, Waehrung in jeder Zelle.
    "ninja_modern": {"weight": 0.8, "set": {
        "font_pool": "sans", "head_band": True, "logo_side": "left", "sender_place": "under",
        "meta_style": ["stacked", "grid"], "meta_place": "top_right",
        "cell_currency": ("post", "EUR"), "table_style": ["zebra", "rules"],
        "header_style": "inverted", "totals_style": "panel", "totals_shade": True,
        "footer": "line", "accent": ["#14505c", "#6b2d5c"]}},
    # Word-Eigenbau: Times, Tabulatorsatz ohne Linien, Summen als Satz, kein Logo,
    # Absender als Briefkopf ueber der Anschrift. Der Klassiker vom Kleinbetrieb.
    "word_diy": {"weight": 1.2, "set": {
        "font_pool": "serif", "logo": "none", "sender_place": "above", "table_style": "borderless",
        "header_style": ["plain", "underline"], "totals_style": ["sentence", "block"],
        "meta_style": ["stack", "pairs", "dateline"], "meta_place": "under_title",
        "footer": ["none", "line", "bank"], "bg_art": "", "title_style": ["plain", "underline"],
        "leading": [1.3, 1.45, 1.6], "cell_pad_y": [1.6, 2.4]}},
    # Excel-Eigenbau: alles in Zellen, Gitter ueberall, Arial, rechtsbuendige Zahlen,
    # Summenzeile als weitere Tabellenzeile. Zellenraster bis in den Kopf.
    "excel_diy": {"weight": 1.2, "set": {
        "font_pool": "sans", "table_style": "grid", "header_style": ["boxed", "bold"],
        "totals_style": ["table", "grid"], "meta_style": ["boxed", "grid"], "grid_box": True,
        "logo": "none", "sender_place": ["above", "under"], "cell_pad_x": [1.0, 1.6],
        "cell_pad_y": [0.8, 1.2], "footer": ["none", "line"], "rule_ink": "#333",
        "title_style": ["plain", "boxed"], "size": [8.0, 8.6, 9.2]}},
    # LibreOffice-Vorlage: Liberation-Schriften, schlichte Linien, Ueberschrift links,
    # Kopfdaten als Paare, Fusszeile mit Bankblock.
    "libreoffice": {"weight": 0.9, "set": {
        "font_pool": ["serif", "sans"], "table_style": ["rules", "grid"], "header_style": "bold",
        "meta_style": "pairs", "meta_place": "under_title", "totals_style": ["block", "table"],
        "logo": ["none", "mark"], "footer": "bank", "title_align": "left",
        "title_style": ["plain", "smallcaps"]}},
    # JTL-Wawi / Collmex: nuechterner Warenwirtschaftsausdruck, dicht, Artikelnummer
    # und Warengruppe, Kopfdaten als Zeilenraster, Druckcode in der Fusszeile.
    "jtl_wawi": {"weight": 0.9, "set": {
        "font_pool": ["sans", "condensed"], "size": [7.2, 7.6], "table_style": ["rules", "grid"],
        "header_style": ["bold", "bodyrow"], "meta_style": ["grid", "row"], "force_codes": True,
        "totals_style": ["table", "block"], "footer": ["columns3", "line"], "footer_code": True,
        "pos_format": ["pad", "step"], "logo": ["none", "wordmark"], "accent": "#3d3d3d"}},
}

# B) Shops und Marktplaetze ---------------------------------------------------
EXTRA_FAMILIES.update({
    # Amazon Marketplace, Verkaeuferrechnung: Kopfraster, Verkaeufer- und
    # Lieferadresse nebeneinander, winzige Fusszeile, Bestellnummer prominent.
    "amazon_market": {"weight": 1.0, "set": {
        "font_pool": ["condensed", "sans"], "meta_style": "grid", "grid_cols": [4, 5],
        "meta_place": "under_title", "table_style": ["rules", "borderless"],
        "header_style": ["bold", "underline"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "none"], "footer_size": [0.55, 0.6], "footer": "line",
        "size": [7.6, 8.0], "addr_heading": ["Rechnungsadresse", "Lieferadresse"],
        "meta_keys": ("order", "customer", "delivery", "due"), "title_style": "plain"}},
    # Otto: Versandhandel, Farbband, Bestellnummer und Kundennummer im Kopf,
    # Rueckgabehinweis in der Fusszeile.
    "otto": {"weight": 0.7, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#7b1f1f", "#a33a12"],
        "meta_style": ["grid", "row"], "table_style": ["zebra", "rules"],
        "header_style": ["inverted", "bold"], "totals_style": ["block", "panel"],
        "logo": "wordmark", "footer": ["columns2", "line"], "addr_customer_no": True}},
    # Zalando: sehr klein gesetzte Grotesk, linienlos, Groessenspalte,
    # Retourenschein-Ton, Summen schlicht rechts.
    "zalando": {"weight": 0.7, "set": {
        "font_pool": "sans", "table_style": "borderless", "header_style": ["plain", "smallcaps"],
        "size": [7.0, 7.4], "totals_style": "block", "totals_side": "right",
        "logo": ["wordmark", "none"], "meta_style": ["stacked", "grid"], "footer": "line",
        "accent": "#000000", "order_pick": [18, 19]}},
    # eBay-Verkaeuferbeleg: Artikelnummer als lange Ziffernfolge, Kopf als eine
    # Zeile, Bezahlhinweis in einem Kasten.
    "ebay": {"weight": 0.7, "set": {
        "font_pool": "sans", "meta_style": "line", "meta_place": "under_title",
        "table_style": ["rules", "grid"], "header_style": "bold", "pay_box": True,
        "totals_style": ["block", "table"], "logo": ["wordmark", "mark"],
        "footer": ["line", "columns2"], "accent": ["#0f4c81", "#1f5c2e"]}},
    # Etsy: verspielt, Wortmarke mittig mit Schwung, linienlose Tabelle,
    # englische Schluessel, handgemachter Ton.
    "etsy": {"weight": 0.7, "set": {
        "lang": "en", "font_pool": "sans", "logo": "wordmark2", "logo_side": "center",
        "sender_place": "under", "table_style": "borderless", "header_style": "smallcaps",
        "totals_style": ["block", "sentence"], "meta_style": ["stacked", "pairs"],
        "footer": ["line", "none"], "accent": ["#c8451e", "#883c1e"], "title_align": "center"}},
    # Shopify-Bestellbestaetigung: Kopfband in Akzentfarbe, Summen als
    # Schluessel-/Wertzeilen, Versandkostenzeile, Zahlungsart im Kasten.
    "shopify": {"weight": 0.9, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#1b6b5a", "#14505c"],
        "table_style": ["borderless", "rules"], "header_style": ["accent", "smallcaps"],
        "totals_style": ["block", "grid"], "totals_side": "right", "pay_box": True,
        "meta_style": ["stacked", "grid"], "logo": ["wordmark", "band"], "footer": "line",
        "bg_art": ["", "circle"]}},
    # WooCommerce-PDF-Plugin: Gittertabelle, Kopfdaten als Paare rechts,
    # "Rechnungsnummer" mit Doppelpunkt, graue Summenzeilen.
    "woocommerce": {"weight": 0.9, "set": {
        "font_pool": "sans", "table_style": ["grid", "zebra"], "header_style": ["inverted", "boxed"],
        "meta_colon": "glued", "meta_style": "pairs", "meta_place": "top_right",
        "totals_style": ["table", "panel"], "totals_shade": True, "logo": ["wordmark", "mark"],
        "footer": ["columns2", "line"], "accent": ["#5a3d7a", "#4b2e83"]}},
    # Shopware: Kopfzeile mit Akzentlinie, Positionsnummern, Lieferadresse als
    # Fliesssatz, Summen als Tabelle rechts.
    "shopware": {"weight": 0.8, "set": {
        "font_pool": "sans", "table_style": "headrule", "header_style": ["bold", "accent"],
        "delivery_sentence": ["Die Ware wird geliefert an:", "Lieferung an:"],
        "meta_style": ["grid", "boxed"], "totals_style": "table", "totals_side": "right",
        "logo": ["wordmark", "mark"], "footer": "columns3", "accent": ["#0f6fc6", "#25406b"]}},
    # JTL-Shop: dicht, Artikelnummer und EAN nebeneinander, Warengruppe,
    # Positionsnummern gepolstert.
    "jtl_shop": {"weight": 0.7, "set": {
        "font_pool": ["sans", "condensed"], "order_pick": [6], "force_codes": True,
        "table_style": ["rules", "grid"], "header_style": "bold", "pos_format": "pad",
        "totals_style": ["table", "block"], "meta_style": ["boxed", "grid"],
        "footer": ["columns3", "line"], "size": [7.2, 7.6]}},
    # Magento: englische Schluessel, zweizeilige Spaltenkoepfe, Summen rechts,
    # "Order #" und "Invoice #" im Kopf.
    "magento": {"weight": 0.7, "set": {
        "lang": "en", "font_pool": "sans", "header_two_line": True,
        "table_style": ["rules", "zebra"], "header_style": ["bold", "underline"],
        "meta_style": ["grid", "pairs"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "none"], "footer": ["line", "columns2"], "accent": "#8a5a1f"}},
    # Conrad Electronic: sehr dichte Tabelle, Artikelnummer fuehrend, roter Akzent,
    # Positionsnummern, Warengruppen- und Steuercodespalte.
    "conrad": {"weight": 0.8, "set": {
        "font_pool": ["condensed", "sans"], "size": [6.9, 7.3], "order_pick": [0, 9],
        "force_codes": True, "table_style": ["rules", "vrules"], "header_style": ["bold", "boxed"],
        "accent": ["#a33a12", "#7b1f1f"], "totals_style": ["table", "grid"],
        "meta_style": ["row", "grid"], "logo": ["wordmark", "band"], "footer": "columns4",
        "footer_size": [0.55, 0.58], "footer_heads": True, "decor_barcode": ["br", "bl"]}},
    # Reichelt Elektronik: Nadeldrucker-Anmutung, dicktengleich, EAN- und
    # Artikelnummernspalte, keine Linien, Summen als Block.
    "reichelt": {"weight": 0.8, "set": {
        "font_pool": "mono", "size": [6.9, 7.2], "order_pick": [6], "table_style": "borderless",
        "header_style": ["plain", "underline"], "leading": [1.1, 1.18],
        "cell_pad_x": 1.0, "cell_pad_y": 0.6, "totals_style": "block", "logo": "none",
        "sender_place": "above", "meta_style": ["row", "grid"], "footer": ["line", "none"],
        "accent": "#000000", "dotmatrix": True}},
    # Wuerth: rotes Band ueber die volle Breite, grosses Logo, Lieferschein-Anmutung,
    # Artikelnummern mit Punkten, Fusszeile vierspaltig.
    "wuerth": {"weight": 0.8, "set": {
        "font_pool": "sans", "head_band": True, "giant_logo": True, "accent": ["#a33a12", "#7b1f1f"],
        "logo": ["band", "wordmark"], "table_style": ["rules", "grid"],
        "header_style": ["inverted", "bold"], "totals_style": ["table", "block"],
        "meta_style": ["boxed", "grid"], "footer": ["columns4", "columns3"], "footer_heads": True}},
    # Baumarkt (Hornbach / OBI / Bauhaus): Grossformatbeleg mit Kassenanmutung,
    # Warengruppen, Steuerschluessel A/B, Pfand- und Rabattzeilen.
    "baumarkt": {"weight": 0.8, "set": {
        "font_pool": ["condensed", "mono"], "force_codes": True, "order_pick": [14, 13],
        "table_style": ["borderless", "dotted"], "header_style": ["plain", "bodyrow"],
        "totals_style": ["block", "table"], "logo": ["wordmark", "band"],
        "meta_style": ["line", "row"], "footer": ["line", "address"], "accent": ["#1f5c2e", "#883c1e"],
        "doc_note": [("pfand",), ()]}},
    # IKEA: blau-gelbes Band, achtstellige Artikelnummern, grosse Ueberschrift,
    # Abholhinweis, Summen schlicht.
    "ikea": {"weight": 0.7, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#0f4c81", "#123f6d"],
        "logo": "band", "table_style": ["borderless", "rules"], "header_style": ["bold", "plain"],
        "title_em": [2.2, 2.6, 2.9], "totals_style": ["block", "panel"],
        "meta_style": ["grid", "stacked"], "footer": ["line", "columns2"], "pay_box": True}},
    # Apple / Google-Beleg: schmal, zentriert, sehr viel Weissraum, englische und
    # deutsche Schluessel gemischt, keine MwSt-Spalte, Bestellnummer gross.
    "appstore": {"weight": 0.8, "set": {
        "font_pool": "sans", "page_format": ["a4", "a5"], "logo": ["wordmark", "mark"],
        "logo_side": "center", "sender_place": ["under", "none"], "address_corner": "center",
        "title_align": "center", "table_style": "borderless", "header_style": ["plain", "smallcaps"],
        "totals_style": ["block", "sentence"], "totals_side": "full", "leading": [1.4, 1.55],
        "meta_style": ["stacked", "grid"], "footer": ["line", "none"], "accent": "#555555"}},
})

# C) Lebensmittel- und Getraenkehandel ----------------------------------------
#    Die Kernkundschaft der App sind Gastronomie und Einzelhandel; hier liegt der
#    Schwerpunkt entsprechend dicht.
EXTRA_FAMILIES.update({
    # Metro / C+C Grossmarkt, Rechnung (nicht Bon): sehr dichte Tabelle,
    # Warengruppe und Steuerschluessel, Pfandzeilen, Kundennummer im Kopf.
    "metro": {"weight": 2.0, "set": {
        "font_pool": ["condensed", "mono"], "size": [6.9, 7.2, 7.6], "force_codes": True,
        "order_pick": [13, 14], "table_style": ["rules", "borderless"],
        "header_style": ["bold", "bodyrow"], "cell_pad_x": 1.2, "cell_pad_y": 0.7,
        "totals_style": ["table", "grid"], "meta_style": ["row", "grid"], "addr_customer_no": True,
        "logo": ["wordmark", "band"], "footer": ["columns3", "line"], "footer_code": True,
        "accent": ["#1f3864", "#7b1f1f"], "doc_note": [("pfand",), ()]}},
    # Selgros: wie Metro, aber Gittertabelle und gerahmter Kopfblock; Positionen
    # in Zehnerschritten.
    "selgros": {"weight": 0.8, "set": {
        "font_pool": ["condensed", "sans"], "force_codes": True, "table_style": "grid",
        "header_style": ["boxed", "bold"], "pos_format": "step", "meta_style": "boxed",
        "meta_place": "top_right", "totals_style": "table", "logo": ["wordmark", "mark"],
        "footer": "columns3", "size": [7.0, 7.4]}},
    # Transgourmet: Belieferungsgrosshandel, Lieferschein-Anmutung, Chargen- und
    # MHD-Zeilen unter der Position, Tourennummer im Kopf.
    "transgourmet": {"weight": 0.9, "set": {
        "font_pool": ["sans", "condensed"], "second_row_details": True, "detail_bold": False,
        "info_rows": True, "table_style": ["rules", "borderless"], "header_style": "bold",
        "meta_style": ["grid", "row"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "band"], "footer": ["columns3", "columns4"], "accent": "#1f5c2e"}},
    # Chefs Culinar: Lieferschein und Rechnung in einem Dokument, Chargen und MHD,
    # Warengruppen als Zwischenueberschriften, Pfandzeilen.
    "chefsculinar": {"weight": 1.0, "set": {
        "font_pool": ["condensed", "sans"], "second_row_details": True, "group_headings": True,
        "sections": "goods", "table_style": ["rules", "dotted"], "header_style": ["bold", "underline"],
        "meta_style": ["row", "grid"], "totals_style": ["table", "grid"], "force_codes": True,
        "logo": ["wordmark", "mark"], "footer": "columns4", "footer_heads": True,
        "doc_note": [("pfand",), ("eigentum",), ()], "size": [7.0, 7.4]}},
    # Edeka Foodservice: Regionalgrosshandel, Gittertabelle, Kundennummer und
    # Tour im Kopfraster, Pfand- und Leergutzeilen im Summenblock.
    "edeka_fs": {"weight": 0.8, "set": {
        "font_pool": "sans", "table_style": "grid", "header_style": ["bold", "inverted"],
        "meta_style": "grid", "grid_cols": [4, 5], "addr_customer_no": True,
        "totals_style": ["table", "panel"], "logo": ["wordmark", "band"], "footer": "columns3",
        "accent": ["#1f5c2e", "#7b1f1f"], "doc_note": [("pfand",), ()]}},
    # Rewe / Kaufland Grosskundenrechnung: Sammelrechnung ueber mehrere Liefertage,
    # Tagesueberschriften, Uebertrag auf jeder Seite.
    "rewe_sammel": {"weight": 0.9, "set": {
        "font_pool": ["sans", "condensed"], "group_headings": True, "sections": "days",
        "table_style": ["rules", "zebra"], "header_style": "bold", "meta_style": ["grid", "row"],
        "totals_style": ["table", "block"], "logo": ["wordmark", "band"], "footer": "columns3",
        "carry_label": ["\u00dcbertrag", "Zwischensumme"], "size": [7.2, 7.6]}},
    # Lidl / Aldi Kassenbon: 80-mm-Rolle, dicktengleich, Steuerschluessel A/B,
    # keine Artikelnummern, Summe fett, EC-Zeile im Fuss.
    "discount_bon": {"weight": 1.0, "set": {
        "page_format": "receipt", "font_pool": "mono", "table_style": "borderless",
        "header_style": "plain", "no_header_row": True, "logo": ["none", "wordmark"],
        "footer": ["line", "none"], "accent": "#000000", "title_style": ["plain", "letter"],
        "pay_box": True, "size": [6.8, 7.4]}},
    # Grossmarkt-Thermobon: schmale Rolle, verwaschener Druck, Warengruppenziffern,
    # Uhrzeit und Kassennummer im Kopf.
    "grossmarkt_bon": {"weight": 0.8, "set": {
        "page_format": "receipt", "font_pool": ["mono", "condensed"], "force_codes": True,
        "table_style": "borderless", "header_style": "plain", "no_header_row": True,
        "logo": "none", "footer": "line", "accent": "#000000", "decor_barcode": ["bl", ""],
        "size": [6.6, 7.0]}},
    # Getraenkemarkt: Lieferschein und Rechnung in einem, Kastenmengen,
    # Pfandzeilen mit negativem Betrag, Leergutruecknahme im Summenblock.
    "getraenke": {"weight": 1.1, "set": {
        "font_pool": ["sans", "condensed"], "table_style": ["rules", "grid"],
        "header_style": ["bold", "boxed"], "meta_style": ["boxed", "grid"],
        "totals_style": ["table", "block"], "logo": ["wordmark", "mark"],
        "footer": ["columns2", "bank"], "doc_note": [("pfand",), ("pfand", "eigentum"), ()],
        "caption": ["Lieferpositionen", "Positionen"], "second_row_details": True}},
    # Brauerei (Frankenbraeu-Typ): zweizeilige Bildwortmarke LINKS neben dem
    # Absender, Gebindenamen wie "24 x 0,33 l MW", Pfandzeilen, Serifen im Satz,
    # Fusszeile mit Braustaette und Registergericht.
    "brauerei": {"weight": 1.0, "set": {
        "font_pool": ["serif", "sans"], "logo": "wordmark2", "logo_side": "left",
        "sender_place": "beside", "show_tagline": True, "wordmark_lines": 2,
        "table_style": ["rules", "borderless"], "header_style": ["bold", "smallcaps"],
        "meta_style": ["pairs", "boxed"], "meta_place": "top_right",
        "totals_style": ["table", "block"], "footer": ["columns3", "bank"], "footer_heads": True,
        "doc_note": [("pfand",), ("pfand", "eigentum")], "accent": ["#7b1f1f", "#8a5a1f"]}},
    # Baeckerei-Sammelrechnung: viele kleine Positionen, Tagesueberschriften,
    # zweistellige Mengen, schmale Tabelle, Summen ganz unten.
    "baeckerei": {"weight": 1.6, "set": {
        "font_pool": ["serif", "sans"], "group_headings": True, "sections": "days",
        "order_pick": [0, 0, 0, 1, 26],
        "table_style": ["rules", "dotted"], "header_style": ["plain", "underline"],
        "narrow_name": False, "totals_bottom": True, "totals_style": ["block", "table"],
        "meta_style": ["stack", "pairs"], "logo": ["wordmark", "mark"], "footer": ["bank", "line"],
        "size": [7.6, 8.2]}},
    # Metzgerei-Sammelrechnung: Kilogrammmengen mit drei Nachkommastellen,
    # Chargennummern, Preis je 100 g, Tagesueberschriften.
    "metzgerei": {"weight": 0.9, "set": {
        "font_pool": ["serif", "sans"], "qty_style": "d3", "group_headings": True,
        "sections": "days", "second_row_details": True, "table_style": ["rules", "grid"],
        "header_style": "bold", "totals_style": ["table", "block"], "meta_style": ["pairs", "boxed"],
        "logo": ["wordmark", "mark"], "footer": ["columns2", "bank"]}},
    # Wochenmarkt-Quittung: Handschrift, A5 oder Bon, nur Menge/Bezeichnung/Betrag,
    # "Betrag dankend erhalten" statt Bankverbindung.
    "wochenmarkt": {"weight": 0.7, "set": {
        "hand": True, "page_format": ["a5", "receipt"], "table_style": ["borderless", "dotted"],
        "header_style": "plain", "no_header_row": True, "logo": "none", "sender_place": "above",
        "totals_style": ["block", "sentence"], "meta_style": ["dateline", "stack"],
        "footer": ["line", "none"], "title_style": ["plain", "underline"], "size": [9.0, 10.0]}},
    # Kaffeeroesterei: englische Schluessel auf deutscher Rechnung, grosse
    # Wortmarke mittig, linienlose Tabelle, Herkunftszeilen unter der Position.
    "kaffee": {"weight": 0.7, "set": {
        "font_pool": ["serif", "sans"], "logo": "wordmark", "logo_side": "center",
        "show_tagline": True, "table_style": "borderless", "header_style": "smallcaps",
        "second_row_details": True, "totals_style": ["block", "sentence"],
        "meta_style": ["stacked", "dateline"], "footer": ["line", "bank"], "leading": [1.4, 1.55]}},
    # Obst- und Gemuesegrosshandel: Kisten und Steigen als Einheit, Tagespreise,
    # Lieferscheincharakter, Handelsklassen in der Detailzeile.
    "obstgemuese": {"weight": 0.8, "set": {
        "font_pool": ["condensed", "sans"], "second_row_details": True, "info_rows": True,
        "table_style": ["rules", "dotted"], "header_style": ["bold", "plain"],
        "meta_style": ["row", "grid"], "totals_style": ["block", "table"],
        "logo": ["none", "wordmark"], "footer": ["line", "columns2"], "size": [7.2, 7.8]}},
    # Fischgrosshandel: Kilogramm mit drei Stellen, Fangdatum und Charge in der
    # Detailzeile, Kuehlkettenhinweis in der Fusszeile.
    "fisch": {"weight": 0.7, "set": {
        "font_pool": "sans", "qty_style": "d3", "second_row_details": True, "detail_bold": True,
        "table_style": ["rules", "grid"], "header_style": "bold", "totals_style": "table",
        "meta_style": ["boxed", "grid"], "logo": ["wordmark", "mark"], "footer": "columns3",
        "accent": ["#14505c", "#00566b"]}},
    # Weinhandel / Spirituosen: Flaschen und Kartons, Gebindegroesse im Namen,
    # gesperrte Ueberschrift, Serifen, Jahrgangszeile.
    "weinhandel": {"weight": 0.9, "set": {
        "font_pool": "serif", "title_style": ["letter", "smallcaps"], "table_style": ["rules", "dotted"],
        "header_style": ["smallcaps", "plain"], "second_row_details": True,
        "totals_style": ["table", "block"], "meta_style": ["pairs", "boxed"],
        "logo": ["wordmark2", "wordmark"], "show_tagline": True, "footer": ["columns3", "bank"],
        "accent": ["#7b1f1f", "#6b2d5c"]}},
    # Tiefkuehl- und Convenience-Lieferant: Gittertabelle, Temperaturzone in der
    # Detailzeile, Tourennummer, Warengruppenspalte.
    "tiefkuehl": {"weight": 0.7, "set": {
        "font_pool": ["sans", "condensed"], "force_codes": True, "second_row_details": True,
        "table_style": "grid", "header_style": ["inverted", "boxed"], "info_rows": True,
        "totals_style": ["table", "grid"], "meta_style": ["grid", "row"],
        "logo": ["wordmark", "band"], "footer": "columns4", "accent": ["#0f6fc6", "#14505c"]}},
})

# D) Dienstleistung, Versorger, Verkehr ---------------------------------------
EXTRA_FAMILIES.update({
    # Telekom: Magenta-Band, Kundennummer und Rechnungsnummer im Kopfraster,
    # Abrechnungszeitraum, Summen als Panel, vierspaltige Fusszeile.
    "telekom": {"weight": 0.9, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#a33a12", "#6b2d5c"],
        "period_block": "range", "meta_style": "grid", "grid_cols": [3, 4],
        "addr_customer_no": True, "table_style": ["rules", "borderless"],
        "header_style": ["bold", "plain"], "totals_style": ["panel", "block"],
        "logo": ["wordmark", "band"], "footer": ["columns4", "columns3"], "footer_heads": True}},
    # Vodafone: rotes Band, Vertragsnummer, Zeitraum, Positionen als Leistungen
    # ohne Artikelnummern, Lastschrifthinweis.
    "vodafone": {"weight": 0.8, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#7b1f1f", "#a33a12"],
        "period_block": "range", "meta_style": ["grid", "stacked"], "table_style": "borderless",
        "header_style": ["plain", "smallcaps"], "totals_style": ["block", "table"],
        "pay_box": True, "logo": ["band", "wordmark"], "footer": "columns3"}},
    # 1&1: nuechtern, Zeitraum und Vertragskonto im Kopf, Positionen mit
    # Leistungszeitraum in der Detailzeile, Summen rechts.
    "einsundeins": {"weight": 0.7, "set": {
        "font_pool": "sans", "period_block": "range", "second_row_details": True,
        "meta_style": ["grid", "pairs"], "table_style": ["rules", "zebra"],
        "header_style": "bold", "totals_style": "table", "logo": ["wordmark", "mark"],
        "footer": ["columns3", "line"], "accent": ["#0f4c81", "#25406b"]}},
    # Stadtwerke Strom/Gas: Abrechnungszeitraum, Zaehlerstaende, geleistete
    # Abschlaege, Guthaben oder Nachzahlung als "Faelliger Betrag" -> amountDue.
    "stadtwerke": {"weight": 1.2, "set": {
        "font_pool": ["sans", "serif"], "period_block": "meter", "prepaid": "abschlag",
        "meta_style": ["grid", "boxed"], "addr_customer_no": True,
        "table_style": ["rules", "grid"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "totals_bottom": True, "logo": ["wordmark", "mark"],
        "footer": ["columns4", "columns3"], "footer_heads": True, "accent": ["#1b6b5a", "#0f4c81"],
        "caption": ["Aufstellung", "Leistungen"]}},
    # Gasversorger / Fernwaerme: wie oben, aber Jahresabrechnung mit
    # Verbrauchszeitraum und Abschlagsplan; Summen ganz unten.
    "gasversorger": {"weight": 0.8, "set": {
        "font_pool": "sans", "period_block": "meter", "prepaid": "abschlag",
        "meta_style": ["boxed", "grid"], "table_style": ["rules", "borderless"],
        "header_style": "bold", "totals_bottom": True, "totals_style": ["block", "panel"],
        "logo": ["wordmark", "band"], "footer": "columns3", "accent": ["#8a5a1f", "#883c1e"]}},
    # DHL: gelbes Band, Sendungsnummern in der Detailzeile, Positionsnummern,
    # Kundennummer prominent, Zeitraum der Abrechnungsperiode.
    "dhl": {"weight": 0.8, "set": {
        "font_pool": "sans", "head_band": True, "accent": ["#8a5a1f", "#7a6a1f"],
        "period_block": "range", "second_row_details": True, "addr_customer_no": True,
        "table_style": ["rules", "zebra"], "header_style": ["bold", "inverted"],
        "meta_style": ["grid", "row"], "totals_style": ["table", "block"],
        "logo": ["band", "wordmark"], "footer": ["columns3", "line"]}},
    # UPS / DPD: Sammelrechnung ueber viele Sendungen, sehr dichte Tabelle,
    # Sendungsnummer als Artikelnummer, englische Schluessel bei UPS.
    "paketdienst": {"weight": 0.8, "set": {
        "font_pool": ["condensed", "sans"], "size": [6.9, 7.3], "period_block": "range",
        "table_style": ["rules", "borderless"], "header_style": ["bold", "bodyrow"],
        "cell_pad_y": 0.6, "meta_style": ["row", "grid"], "totals_style": ["table", "grid"],
        "logo": ["wordmark", "band"], "footer": ["columns3", "line"], "footer_code": True,
        "accent": ["#8a5a1f", "#7b1f1f"]}},
    # Versicherung: keine Artikelnummern, eine bis drei Beitragszeilen,
    # Versicherungsschein-Nummer, Zeitraum, Versicherungsteuer statt MwSt.
    "versicherung": {"weight": 0.9, "set": {
        "font_pool": ["serif", "sans"], "period_block": "policy", "table_style": ["rules", "borderless"],
        "header_style": ["plain", "underline"], "meta_style": ["boxed", "pairs"],
        "meta_place": "top_right", "totals_style": ["block", "table"],
        "doc_note": [("versicherung",), ("versicherung", "eigentum")], "logo": ["mark", "wordmark"],
        "footer": ["columns3", "bank"], "accent": ["#1f3864", "#25406b"], "leading": [1.35, 1.5]}},
    # Miete / Nebenkostenabrechnung: Kaltmiete, Nebenkosten, Heizung als Positionen,
    # Abrechnungszeitraum, umsatzsteuerfrei nach Paragraf 4, Vorauszahlungen abgesetzt.
    "miete": {"weight": 0.9, "set": {
        "font_pool": ["serif", "sans"], "period_block": "rent", "prepaid": "abschlag",
        "doc_note": [("miete",)], "table_style": ["rules", "borderless"],
        "header_style": ["plain", "bold"], "meta_style": ["stack", "pairs"],
        "totals_style": ["block", "table"], "logo": ["none", "mark"], "sender_place": "above",
        "footer": ["bank", "line"], "title_style": ["plain", "underline"]}},
    # Kfz-Werkstatt: Abschnitte Lohn und Material, Arbeitswerte als Einheit,
    # Kennzeichen und Fahrgestellnummer im Kopf, Altteilpfand.
    "kfz": {"weight": 1.0, "set": {
        "font_pool": ["sans", "condensed"], "group_headings": True, "sections": "trade",
        "info_rows": True, "table_style": ["rules", "grid"], "header_style": ["bold", "boxed"],
        "meta_style": ["boxed", "grid"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "columns4"], "footer_heads": True,
        "doc_note": [("eigentum",), ()], "accent": ["#1f3864", "#3d3d3d"]}},
    # Handwerkerrechnung: Lohn- und Materialabschnitte, Paragraf 13b bei
    # Bauleistungen, Paragraf-35a-Ausweis der Arbeitskosten, Aufmass in der Detailzeile.
    "handwerker": {"weight": 1.2, "set": {
        "font_pool": ["serif", "sans"], "group_headings": True, "sections": "trade",
        "second_row_details": True, "table_style": ["rules", "borderless"],
        "header_style": ["bold", "underline"], "meta_style": ["pairs", "boxed", "stack"],
        "totals_style": ["block", "table"], "totals_bottom": True,
        "doc_note": [("s35a",), ("b13b_bau",), ("s35a", "eigentum"), ("b13b_bau", "s35a")],
        "logo": ["wordmark", "mark", "none"], "footer": ["bank", "columns3"]}},
    # Hotel: Uebernachtungszeilen je Nacht, Kurtaxe, Fruehstueck mit anderem
    # Steuersatz, An- und Abreisedatum im Kopf, Zimmernummer.
    "hotel": {"weight": 1.0, "set": {
        "font_pool": ["serif", "sans"], "period_block": "stay", "group_headings": True,
        "sections": "nights", "table_style": ["rules", "dotted"], "header_style": ["smallcaps", "plain"],
        "meta_style": ["boxed", "pairs"], "totals_style": ["table", "block"],
        "logo": ["wordmark2", "wordmark"], "show_tagline": True, "footer": ["columns3", "bank"],
        "accent": ["#6b2d5c", "#8a5a1f"], "title_style": ["plain", "smallcaps"]}},
    # Bewirtungsbeleg: Restaurantrechnung mit den Feldern Anlass und Teilnehmer
    # unter dem Betrag, Trinkgeldzeile, oft als Bon.
    "bewirtung": {"weight": 0.9, "set": {
        "page_format": ["a5", "receipt", "a4"], "font_pool": ["mono", "condensed", "sans"],
        "host_fields": True, "doc_note": [("bewirtung",)], "table_style": "borderless",
        "header_style": "plain", "no_header_row": True, "logo": ["none", "wordmark"],
        "totals_style": ["block", "sentence"], "footer": ["line", "none"],
        "title_text": ["Bewirtungsbeleg", "Rechnung", "Bewirtungsrechnung"]}},
    # Taxi- und Parkhausquittung: winziger Bon, Uhrzeiten, Kennzeichen,
    # ein bis drei Zeilen, Betrag gross.
    "taxi": {"weight": 0.8, "set": {
        "page_format": "receipt", "font_pool": ["mono", "condensed"], "table_style": "borderless",
        "header_style": "plain", "no_header_row": True, "logo": "none",
        "totals_style": ["block", "sentence"], "footer": ["line", "none"],
        "title_text": ["Quittung", "Taxiquittung", "Fahrtquittung", "Parkschein"],
        "accent": "#000000", "size": [7.0, 7.8]}},
    # Tankstelle: Bon mit Liter, Preis je Liter als Preisbasis, Kilometerstand,
    # Kartenzahlung, Steuerschluessel.
    "tankstelle": {"weight": 0.8, "set": {
        "page_format": ["receipt", "a5"], "font_pool": "mono", "force_codes": True,
        "table_style": "borderless", "header_style": "plain", "no_header_row": True,
        "qty_style": "d2", "price_decimals": 3, "logo": ["none", "wordmark"],
        "totals_style": "block", "pay_box": True, "footer": "line", "accent": "#000000"}},
    # Bahn / Flixbus: Fahrschein-Beleg, sieben Prozent, Strecke in der Detailzeile,
    # Auftragsnummer als Buchstaben-Ziffern-Code, englische und deutsche Schluessel.
    "bahn": {"weight": 0.8, "set": {
        "font_pool": "sans", "second_row_details": True, "meta_style": ["grid", "stacked"],
        "table_style": ["borderless", "rules"], "header_style": ["plain", "smallcaps"],
        "totals_style": ["block", "table"], "logo": ["wordmark", "band"], "head_band": True,
        "footer": ["line", "columns2"], "accent": ["#7b1f1f", "#1f5c2e"],
        "title_text": ["Rechnung", "Beleg", "Buchungsbest\u00e4tigung"]}},
    # Fluglinie: englischer E-Ticket-Beleg, Passagier- und Flugzeilen,
    # Steuern und Gebuehren als eigene Zuschlagszeilen, Buchungscode.
    "airline": {"weight": 0.7, "set": {
        "lang": "en", "font_pool": ["sans", "condensed"], "second_row_details": True,
        "meta_style": ["grid", "row"], "table_style": ["rules", "borderless"],
        "header_style": ["bold", "underline"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "band"], "footer": ["line", "columns2"],
        "date_format": ["%d %B %Y", "%Y-%m-%d"], "title_style": "letter"}},
    # Arztrechnung nach GOAE: Ziffer und Faktor als Spalten, keine Umsatzsteuer,
    # Diagnose in der Detailzeile, Serifen, sehr formaler Kopf.
    "arzt": {"weight": 0.8, "set": {
        "font_pool": "serif", "doc_note": [("arzt",)], "second_row_details": True,
        "table_style": ["rules", "grid"], "header_style": ["plain", "underline"],
        "meta_style": ["pairs", "stack"], "totals_style": ["block", "table"],
        "logo": ["none", "mark"], "sender_place": "above", "footer": ["bank", "line"],
        "title_style": ["plain", "letter"], "leading": [1.35, 1.5]}},
    # Apotheke: PZN als Artikelnummer, Rezeptzuzahlung, sieben und neunzehn Prozent
    # gemischt, sehr dichter Bon oder A5-Beleg.
    "apotheke": {"weight": 0.8, "set": {
        "page_format": ["a5", "a4", "receipt"], "font_pool": ["condensed", "mono"],
        "table_style": ["borderless", "dotted"], "header_style": ["plain", "bodyrow"],
        "totals_style": ["block", "table"], "logo": ["none", "mark"],
        "meta_style": ["line", "stack"], "footer": ["line", "address"], "size": [7.0, 7.6]}},
    # Reinigung, Sicherheit, Wartung: Dienstleistungsrechnung ueber einen Zeitraum,
    # Stundensaetze, Objektnummer, Leistungsnachweis als Detailzeile.
    "dienstleister": {"weight": 0.8, "set": {
        "font_pool": ["sans", "serif"], "period_block": "range", "second_row_details": True,
        "table_style": ["rules", "borderless"], "header_style": "bold",
        "meta_style": ["boxed", "grid"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "bank"]}},
})

# E) Belegarten ----------------------------------------------------------------
#    Familien mit `weight: 0` werden nie zufaellig gezogen: sie haengen an einer
#    Belegart, die `generate.py` je *Rechnung* zieht, weil sie die Zahlen in
#    `expected.json` veraendert.
EXTRA_FAMILIES.update({
    # Gutschrift / Rechnungskorrektur: Ueberschrift gesperrt, Bezug auf die
    # urspruengliche Rechnung im Kopf, durchgehend negative Betraege, Hinweis
    # "Der Betrag wird Ihrem Konto gutgeschrieben".
    "gutschrift": {"weight": 1.0, "set": {
        "font_pool": ["sans", "serif"], "title_style": ["letter", "boxed", "smallcaps"],
        "doc_note": [("gutschrift",)], "minus_style": ["lead", "trail"],
        "meta_style": ["boxed", "grid", "pairs"], "meta_keys": ("order2", "customer", "delivery"),
        "table_style": ["rules", "grid"], "header_style": "bold",
        "totals_style": ["table", "block"], "logo": ["wordmark", "mark"], "footer": "columns3"}},
    # Kleinunternehmer nach Paragraf 19: NIRGENDS Umsatzsteuer — keine MwSt-Spalte,
    # keine Steuerzeile im Summenblock, stattdessen der Hinweissatz. Netto und
    # Brutto sind derselbe Betrag, und genau so steht es auf dem Beleg.
    "kleinunternehmer": {"weight": 0, "doctype": "kleinunternehmer", "set": {
        "font_pool": ["serif", "sans"], "doc_note": [("kleinunternehmer",)],
        "logo": ["none", "wordmark", "mark"], "sender_place": ["above", "under"],
        "table_style": ["rules", "borderless", "grid"], "header_style": ["plain", "bold"],
        "totals_style": ["block", "table", "sentence"], "meta_style": ["stack", "pairs", "grid"],
        "footer": ["bank", "line", "columns2"]}},
    # Reverse Charge: Leistung an einen Unternehmer im EU-Ausland, keine
    # Umsatzsteuer, USt-IdNr. des Empfaengers im Kopf, Paragraf 13b bzw. Art. 196.
    "reverse_charge": {"weight": 0, "doctype": "reverse_charge", "set": {
        "font_pool": ["sans", "serif"], "doc_note": [("reverse_charge",), ("innergemein",),
                                                      ("reverse_charge", "innergemein")],
        "meta_style": ["boxed", "grid"], "meta_keys": ("customer", "order", "taxno", "delivery"),
        "table_style": ["rules", "grid"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "logo": ["wordmark", "mark"], "footer": "columns3"}},
    # Schweizer Rechnung: CHF, 8,1 % bzw. 2,6 % MWST, CHE-Nummer, QR-Rechnung am
    # Blattfuss mit Empfangsschein und Zahlteil.
    "swiss": {"weight": 0, "doctype": "swiss", "set": {
        "currency": "CHF", "waehrung_text": "CHF", "qr_bill": True, "multi_row": "none",
        "doc_note": [("swiss",), ()], "font_pool": ["sans", "serif"],
        "table_style": ["rules", "borderless"], "header_style": ["bold", "plain"],
        "meta_style": ["pairs", "grid"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "line"],
        "date_format": ["%d.%m.%Y", "%d.%m.%y"], "group_sep": ["."]}},
    # Abschlagsrechnung: eine Teilleistung wird angefordert, unten stehen die
    # bereits geleisteten Zahlungen und der faellige Restbetrag (-> amountDue).
    "abschlag": {"weight": 1.0, "set": {
        "prepaid": "anzahlung", "doc_note": [("abschlag",), ()],
        "title_text": ["Abschlagsrechnung", "Anzahlungsrechnung", "Teilrechnung",
                       "1. Abschlagsrechnung"],
        "font_pool": ["sans", "serif"], "table_style": ["rules", "grid"],
        "header_style": "bold", "meta_style": ["boxed", "pairs", "grid"],
        "totals_style": ["table", "block"], "logo": ["wordmark", "mark"], "footer": "columns3"}},
    # Schlussrechnung: alle Abschlaege werden abgezogen, der Restbetrag ist
    # deutlich kleiner als die Bruttosumme — genau der Fall, in dem der
    # Rechnungsbetrag NICHT der zu zahlende Betrag ist.
    "schlussrechnung": {"weight": 1.0, "set": {
        "prepaid": "abschlag", "doc_note": [("abschlag",), ("abschlag", "eigentum")],
        "title_text": ["Schlussrechnung", "Schlussrechnung Nr.", "Endabrechnung"],
        "font_pool": ["sans", "serif"], "table_style": ["rules", "borderless"],
        "header_style": ["bold", "underline"], "meta_style": ["boxed", "grid"],
        "totals_style": ["table", "block"], "totals_bottom": True,
        "logo": ["wordmark", "mark"], "footer": ["columns3", "bank"]}},
    # Proforma: sieht aus wie eine Rechnung, ist keine. Ueberschrift und Hinweis
    # sagen es, sonst aendert sich nichts — der haerteste Fall fuer eine
    # Belegartenerkennung.
    "proforma": {"weight": 0.8, "set": {
        "title_text": ["Proforma-Rechnung", "PROFORMA-RECHNUNG", "Proformarechnung",
                       "Pro-forma Invoice"],
        "doc_note": [("proforma",)], "title_style": ["letter", "boxed", "plain"],
        "font_pool": ["sans", "serif"], "table_style": ["rules", "grid"],
        "header_style": "bold", "meta_style": ["boxed", "grid"],
        "totals_style": ["table", "block"], "logo": ["wordmark", "mark"], "footer": "columns3"}},
    # Lieferschein und Rechnung in einem Dokument: Ueberschrift nennt beides,
    # Lieferscheinnummer und Rechnungsnummer stehen nebeneinander im Kopf.
    "lieferschein_rechnung": {"weight": 0.9, "set": {
        "title_text": ["Rechnung / Lieferschein", "Lieferschein und Rechnung",
                       "Rechnung-Lieferschein"],
        "meta_keys": ("delivery_note", "customer", "order", "delivery"),
        "caption": ["Lieferpositionen", "Positionen"], "font_pool": ["sans", "condensed"],
        "table_style": ["rules", "grid"], "header_style": ["bold", "boxed"],
        "meta_style": ["boxed", "grid", "row"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "columns2"], "retline": True}},
    # Sammelrechnung ueber viele Seiten: kleiner Satz, viele Positionen,
    # Uebertragszeile auf jeder Seite, Seitenzahl oben rechts.
    "sammelrechnung": {"weight": 0.9, "set": {
        "font_pool": ["condensed", "sans"], "size": [6.9, 7.2], "cell_pad_y": [0.5, 0.8],
        "group_headings": True, "sections": "days", "table_style": ["rules", "dotted"],
        "header_style": ["bold", "bodyrow"], "pageno_place": "tr", "pageno_form": ["von", "slash"],
        "carry_label": ["Übertrag", "Übertrag Seite", "Übertrag netto"],
        "totals_style": ["table", "block"], "meta_style": ["row", "grid"], "footer": "columns3"}},
    # Oesterreichische Rechnung: UID ATU, Firmenbuchnummer und Gerichtsstand in
    # der Fusszeile, "Faktura" als Ueberschrift, 20/13/10 Prozent.
    "austrian": {"weight": 1.0, "set": {
        "font_pool": ["serif", "sans"], "title_text": ["Faktura", "Rechnung", "RECHNUNG",
                                                        "Rechnung / Faktura"],
        "meta_keys": ("customer", "order", "taxno", "delivery"), "footer": "columns3",
        "footer_heads": True, "table_style": ["rules", "grid"], "header_style": "bold",
        "meta_style": ["boxed", "pairs"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "mark"], "date_format": ["%d.%m.%Y", "%d. %B %Y"]}},
    # Englischsprachige Rechnung an einen deutschen Kaeufer: Invoice / Total / VAT,
    # Datum als 14/07/2026 oder 2026-07-14, "Amount Due" als eigene Zeile.
    "english": {"weight": 1.1, "set": {
        "lang": "en", "font_pool": ["sans", "serif"], "prepaid": ["balance", ""],
        "table_style": ["rules", "grid", "borderless"], "header_style": ["bold", "underline"],
        "meta_style": ["grid", "pairs", "stacked"], "totals_style": ["table", "block"],
        "logo": ["wordmark", "mark", "none"], "footer": ["columns3", "line"],
        "date_format": ["%d/%m/%Y", "%Y-%m-%d", "%d %B %Y"], "doc_note": [("en_vat",), ()]}},
    # Niederlaendischer Lieferant: Factuur, Btw, Te betalen.
    "dutch": {"weight": 0.6, "set": {
        "lang": "nl", "font_pool": "sans", "table_style": ["rules", "borderless"],
        "header_style": ["bold", "plain"], "meta_style": ["grid", "pairs"],
        "totals_style": ["table", "block"], "logo": ["wordmark", "mark"],
        "footer": ["columns3", "line"], "date_format": ["%d-%m-%Y", "%d.%m.%Y"]}},
    # Italienischer Lieferant: Fattura, Imponibile, IVA, Totale documento.
    "italian": {"weight": 0.6, "set": {
        "lang": "it", "font_pool": ["serif", "sans"], "table_style": ["rules", "grid"],
        "header_style": ["bold", "smallcaps"], "meta_style": ["boxed", "grid"],
        "totals_style": ["table", "grid"], "logo": ["wordmark", "mark"],
        "footer": ["columns3", "line"], "date_format": ["%d/%m/%Y", "%d.%m.%Y"]}},
    # Polnischer Lieferant: Faktura, Wartość netto, Do zapłaty.
    "polish": {"weight": 0.6, "set": {
        "lang": "pl", "font_pool": "sans", "table_style": ["grid", "rules"],
        "header_style": ["boxed", "bold"], "meta_style": ["grid", "boxed"],
        "totals_style": ["grid", "table"], "logo": ["wordmark", "none"],
        "footer": ["columns3", "line"], "date_format": ["%Y-%m-%d", "%d.%m.%Y"]}},
})

# F) E-Rechnungs-Viewer --------------------------------------------------------
#    Vier von acht echten Spirituosen-Fehlern sehen so aus. Jeder Wert steht
#    hinter einem Schluessel, der Kaeuferblock steht VOR dem Verkaeuferblock,
#    die Abschnitte stehen in Kaesten, und eine Position ist ein Block aus drei
#    Zeilen statt einer Tabellenzeile.
EXTRA_FAMILIES.update({
    # XRechnung-Visualisierung (KoSIT-Stylesheet): graue Abschnittskaesten,
    # "Rechnungsempfaenger" vor "Rechnungsersteller", Artikelkennung mit Schema,
    # Preiseinheit-Spalte, zwei Gesamtsummenzeilen, "Faelliger Betrag".
    "xrechnung": {"weight": 2.2, "set": {
        "einvoice": "xrechnung", "buyer_first": True, "prepaid": ["", "anzahlung"],
        "font_pool": "sans",
        "size": [7.2, 7.6, 8.0], "accent": ["#3d3d3d", "#25406b", "#1f3864"],
        "logo": "none", "sender_place": "none", "meta_colon": "glued",
        "doc_note": [("xrechnung",), ()], "footer": ["line", "none"],
        "title_style": ["plain", "letter"], "title_align": "left",
        "page_format": ["a4", "a4", "letter"], "decor_barcode": "", "bg_art": ""}},
    # ZUGFeRD-Viewer: dasselbe Prinzip, aber mit Rahmen statt Flaechen und der
    # Profilangabe (EN 16931 / EXTENDED) im Kopf.
    "zugferd": {"weight": 1.4, "set": {
        "einvoice": "zugferd", "buyer_first": True, "prepaid": ["", "anzahlung"],
        "font_pool": ["sans", "condensed"],
        "size": [7.0, 7.4], "accent": ["#1b6b5a", "#0f4c81"], "logo": "none",
        "sender_place": "none", "meta_colon": "glued", "doc_note": [("xrechnung",), ()],
        "footer": ["line", "none"], "title_style": "plain", "page_format": "a4"}},
    # Portalausdruck (Leitweg-ID, Pruefbericht): jede Angabe als Formularfeld mit
    # Unterstrich, Abschnitte mit Nummern, sehr viele leere Felder.
    "einvoice_portal": {"weight": 1.0, "set": {
        "einvoice": "portal", "buyer_first": True, "form_fields": True,
        "prepaid": ["", "abschlag"], "font_pool": "sans",
        "size": [7.0, 7.4], "logo": "none", "sender_place": "none", "meta_colon": "glued",
        "footer": ["line", "none"], "title_style": ["plain", "letter"], "page_format": "a4",
        "accent": ["#555555", "#3d3d3d"]}},
    # Peppol BIS Billing 3.0, englische Visualisierung: Seller / Buyer,
    # "Item identification", "Scheme 0088 (GLN)", "Amount due for payment".
    "peppol": {"weight": 0.9, "set": {
        "einvoice": "peppol", "buyer_first": True, "lang": "en",
        "prepaid": ["", "balance"], "font_pool": "sans",
        "size": [7.0, 7.4], "logo": "none", "sender_place": "none", "meta_colon": "glued",
        "footer": ["line", "none"], "title_style": ["letter", "plain"], "page_format": "a4",
        "accent": ["#0f4c81", "#4b2e83"]}},
})

# G) Struktur jenseits der Programmnamen ---------------------------------------
EXTRA_FAMILIES.update({
    # Zweispaltige Positionsliste: zwei schmale Tabellen nebeneinander, wie sie
    # Kataloge und Speisekarten-Rechnungen drucken. Die Lesereihenfolge der OCR
    # springt dabei zwischen den Spalten.
    "twocol": {"weight": 1.0, "set": {
        "item_form": "twocol", "font_pool": ["sans", "condensed"], "size": [7.0, 7.6],
        "table_style": ["borderless", "dotted", "rules"], "header_style": ["plain", "smallcaps"],
        "totals_style": ["block", "table"], "meta_style": ["grid", "pairs"],
        "logo": ["wordmark", "mark", "none"], "footer": ["line", "columns2"]}},
    # Position als Schluessel-Wert-Block statt als Tabellenzeile: "Bezeichnung: ...",
    # "Menge: ...", "Einzelpreis: ...". Kommt aus Formulargeneratoren und aus jedem
    # Viewer, der XML flach ausgibt.
    "kv_items": {"weight": 1.1, "set": {
        "item_form": "kv", "font_pool": ["sans", "serif"], "meta_colon": "glued",
        "table_style": ["borderless", "rules"], "header_style": "plain", "no_header_row": True,
        "totals_style": ["block", "table"], "meta_style": ["stacked", "pairs"],
        "logo": ["none", "wordmark"], "footer": ["line", "columns2"]}},
    # Querformat: die breite Tabelle mit allen Spalten, wie sie aus einer
    # Warenwirtschaft quer gedruckt kommt.
    "landscape": {"weight": 1.0, "set": {
        "page_format": "a4_land", "force_codes": True, "font_pool": ["sans", "condensed"],
        "table_style": ["rules", "grid", "vrules"], "header_style": ["bold", "boxed"],
        "meta_style": ["row", "grid"], "totals_style": ["table", "grid"],
        "footer": ["columns4", "columns5", "line"], "size": [7.2, 7.8]}},
    # Seitenleiste: ein farbiger Streifen mit Anschrift und Kontakt laeuft senkrecht
    # am Blattrand, der Satz beginnt erst daneben.
    "sidebar": {"weight": 1.0, "set": {
        "sidebar": ["left", "right"], "font_pool": "sans", "logo": ["mark", "wordmark"],
        "sender_place": "none", "table_style": ["borderless", "rules"],
        "header_style": ["accent", "smallcaps"], "totals_style": ["block", "panel"],
        "meta_style": ["stacked", "grid"], "footer": ["none", "line"],
        "accent": ["#1f3864", "#1b6b5a", "#6b2d5c"]}},
    # Nadeldrucker auf Endlospapier: dicktengleich, gesperrt, blasse Schrift,
    # keine Linien, alles in Grossbuchstaben, Lochrandanmutung.
    "dotmatrix": {"weight": 1.1, "set": {
        "dotmatrix": True, "font_pool": "mono", "size": [7.4, 7.8, 8.2],
        "leading": [1.1, 1.18, 1.26], "table_style": "borderless", "header_style": "plain",
        "uppercase_headers": True, "logo": "none", "sender_place": "above",
        "meta_style": ["row", "line", "grid"], "totals_style": ["block", "table"],
        "footer": ["line", "none"], "accent": "#000000", "ink": ["#333", "#3a3a3a", "#4a4a4a"],
        "title_style": ["letter", "plain"], "pos_format": ["pad", "step"]}},
    # Invertierter Satz: dunkle Baender ueber Kopf, Spaltenkopf und Summen.
    "inverted": {"weight": 0.9, "set": {
        "head_band": True, "header_style": "inverted", "totals_shade": True,
        "totals_style": ["panel", "boxed"], "font_pool": "sans", "logo": ["band", "wordmark"],
        "table_style": ["zebra", "borderless"], "meta_style": ["grid", "stacked"],
        "footer": ["line", "columns2"], "accent": ["#000000", "#1f3864", "#7b1f1f"]}},
    # Riesenlogo: eine Bildmarke ueber die halbe Blattbreite, der Satz beginnt
    # erst im unteren Drittel.
    "giantlogo": {"weight": 0.9, "set": {
        "giant_logo": True, "logo": ["mark", "wordmark2"], "logo_side": ["left", "center"],
        "font_pool": ["sans", "serif"], "table_style": ["borderless", "rules"],
        "header_style": ["smallcaps", "plain"], "totals_style": ["block", "table"],
        "meta_style": ["stacked", "pairs"], "footer": ["line", "columns2"],
        "title_align": ["left", "center"]}},
    # Ganz ohne Linien, dafuer mit einer grossen blassen Form hinter dem Satz —
    # der Strauss-Typ: die Tabelle ist nur noch eine Anordnung im Weissraum.
    "nolines": {"weight": 1.0, "set": {
        "table_style": "borderless", "header_style": ["plain", "smallcaps", "letterspaced"],
        "bg_art": ["circle", "rings", "blob", "stripes", "grid"], "bg_place": ["page", "table"],
        "bg_opacity": [0.08, 0.12, 0.16], "font_pool": ["sans", "serif"],
        "totals_style": ["block", "sentence"], "meta_style": ["stacked", "grid"],
        "logo": ["wordmark2", "mark"], "footer": ["none", "line"], "leading": [1.35, 1.5, 1.62]}},
    # Nordfoto-Formularsatz, zweite Auspraegung: Doppelpunkt als eigene Spalte,
    # leere Formularfelder, gesperrte Ueberschrift mit der Nummer, Summen ganz
    # unten, fuenfspaltige Fusszeile in 5 pt.
    "nordform": {"weight": 1.1, "set": {
        "meta_colon": "column", "meta_style": "boxed", "meta_place": "top_right",
        "meta_empty_key": ["Kd-UStIdNr.", "Ihre USt-IdNr.", "Ihr Zeichen"],
        "title_number": True, "title_spaced_key": True, "title_style": "letter",
        "font_pool": ["sans", "condensed"], "logo": ["wordmark2", "wordmark"],
        "sender_place": "none", "head_contact": "block", "info_rows": True,
        "table_style": ["borderless", "rules"], "totals_bottom": True,
        "footer": ["columns5", "columns4"], "footer_size": [0.55, 0.58], "footer_heads": True,
        "decor_barcode": ["br", "bl"], "pay_box": True}},
    # Doppelpunktspalte ohne Formularrahmen, dazu eine gesperrte Ueberschrift:
    # die Schluesselspalte steht weit links, der Wert weit rechts daneben.
    "colonform": {"weight": 0.9, "set": {
        "meta_colon": "column", "meta_style": ["pairs", "stack"], "meta_place": "under_title",
        "title_style": "letter", "font_pool": ["sans", "serif"],
        "table_style": ["rules", "borderless"], "header_style": ["letterspaced", "smallcaps"],
        "totals_style": ["block", "table"], "logo": ["none", "wordmark"],
        "footer": ["line", "bank"]}},
    # Schmales Blatt: A5 oder B5, die Rechnung eines Kleinstbetriebs auf halbem
    # Bogen. Eigene Familie, damit das Format nicht nur aus PAGE_MIX faellt.
    "halbbogen": {"weight": 0.9, "set": {
        "page_format": ["a5", "b5"], "font_pool": ["serif", "sans"], "size": [7.6, 8.2],
        "table_style": ["rules", "borderless", "dotted"], "header_style": ["plain", "bold"],
        "meta_style": ["stack", "pairs", "stacked"], "totals_style": ["block", "table"],
        "logo": ["none", "wordmark", "mark"], "footer": ["line", "bank", "none"]}},
})


# ============================================================== v12: Gastronomie
#
# v11 hat 113 Familien gebaut, aber aus der Sicht der Rechnungen, die dieser
# Nutzer wirklich bucht, war das eine schmale Auswahl: ein Gastronomiebetrieb
# kauft bei Winzern, Getraenkefachgrosshaendlern, Metzgern, Fischhaendlern,
# Baeckern, Kaesehaendlern, Roestereien, Importeuren, Mietgeschirrverleihern und
# Waeschereien ein, und jede dieser Branchen druckt ihre eigene Tabelle. Die
# vierzig Familien hier sind alle nach einem solchen Lieferantentyp modelliert
# und bedienen die Fehlerbilder aus v12/PLAN.md:
#
#   1 Weinspalten-Tausch  -> weingut, weinhandel2, sektkellerei, winzergeno
#   3 Einheit im Preiskopf -> weingut, metzgerei2, fisch2, kaese_affineur,
#                             kaffee_roesterei2, wurstwaren, muehle, olivenoel ...
#   4 Steuersaetze je Zeile -> cash_carry, selgros2, grosskueche, convenience
#   6 Mengenzuverlaessigkeit -> alle mit `qty_unit_glue`, Einheit-vor-Menge-Folgen
#                               (order_pick 23/24/31) und nackten Mengen
#
# Die Gewichte liegen bei 0,6-1,0; zusammen rund 30. `layout.FAMILIES["free"]`
# steht deshalb auf 52 statt 40 und haelt die freie Ziehung ueber 25 %.
V12_FAMILIES = {
    # --------------------------------------------------------------- Wein, Sekt
    # Weingut / Erzeugerabfuellung. Der Beleg, an dem v11 gescheitert ist: die
    # Artikelnummer steht RECHTS der Bezeichnung, der Name beginnt mit dem
    # Jahrgang ("2024 Iphoefer Kronsberg Silvaner trocken 0,75 l"), die SKU ist
    # selbst jahrgangskodiert ("2024-S-01"), die Preisspalte heisst "Preis je Fl",
    # gesetzt ist in einer Serife mit sehr breiter Namensspalte.
    "weingut": {"weight": 1.0, "set": {
        "font_pool": "serif", "order_pick": [20, 21, 22], "price_header_unit": "je",
        "narrow_name": False, "name_width": [64.0, 70.0, 74.0],
        "table_style": ["rules", "dotted", "borderless"],
        "header_style": ["smallcaps", "plain", "underline"],
        "totals_style": ["table", "block"], "totals_side": "right",
        "meta_style": ["pairs", "boxed", "stack"], "meta_place": ["top_right", "under_title"],
        "logo": ["wordmark2", "wordmark", "mark"], "footer": ["bank", "columns3"],
        "title_style": ["letter", "smallcaps", "plain"], "pos_format": ["plain", "dot"],
        "qty_style": ["trim", "d2"], "bg_art": "", "accent": ["#5a1f2d", "#3d3d3d", "#1f3864"]}},
    # Weinhandel, zweite Auspraegung: wie `weinhandel`, aber die Art.-Nr. steht
    # rechts und die Kopfzeile ist zweizeilig.
    "weinhandel2": {"weight": 0.9, "set": {
        "font_pool": ["serif", "sans"], "order_pick": [21, 22, 20],
        "price_header_unit": ["je", "cur"], "header_two_line": True,
        "table_style": ["rules", "grid"], "header_style": ["bold", "smallcaps"],
        "totals_style": ["table", "block"], "meta_style": ["boxed", "pairs"],
        "logo": ["wordmark", "wordmark2"], "footer": ["columns3", "bank"],
        "name_width": [58.0, 66.0], "narrow_name": False}},
    # Sektkellerei: Flaschen und Kartons, Jahrgang im Namen, Serife, Kapitaelchen.
    "sektkellerei": {"weight": 0.7, "set": {
        "font_pool": "serif", "order_pick": [20, 2, 21], "price_header_unit": ["je", ""],
        "table_style": ["dotted", "rules"], "header_style": ["smallcaps", "letterspaced"],
        "totals_style": ["block", "table"], "meta_style": ["pairs", "stack"],
        "logo": ["wordmark2", "mark"], "footer": ["bank", "columns2"],
        "title_style": ["letter", "smallcaps"]}},
    # Winzergenossenschaft: nuechterner Warenwirtschaftsausdruck ueber Weine,
    # Mitgliedsnummer im Kopf, Art.-Nr. rechts, Gebindespalte.
    "winzergeno": {"weight": 0.7, "set": {
        "font_pool": ["sans", "condensed"], "order_pick": [22, 29],
        "price_header_unit": ["je", "ep"], "force_codes": True,
        "table_style": ["rules", "grid"], "header_style": ["bold", "boxed"],
        "totals_style": ["table", "grid"], "meta_style": ["grid", "row"],
        "logo": ["mark", "none"], "footer": ["columns3", "columns4"]}},
    # Spirituosen-Fachgrosshandel (der Brueckner-Typ): Barbedarf, lange Namen mit
    # Volumenangabe, Art.-Nr. mal links mal rechts, englische Markennamen.
    "spirituosen": {"weight": 0.9, "set": {
        "font_pool": ["sans", "serif"], "order_pick": [20, 0, 22],
        "price_header_unit": ["", "je"], "name_width": [56.0, 64.0],
        "table_style": ["rules", "borderless"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "meta_style": ["boxed", "pairs"],
        "logo": ["wordmark", "wordmark2"], "footer": ["bank", "columns3"],
        "name_gtin": True}},
    # ------------------------------------------------------ Getraenke, Leergut
    # Getraenkefachgrosshandel: Pfand- und Leergutzeilen, Kastenmengen, Tour und
    # Fahrer im Kopf, Steuerschluessel je Zeile.
    "getraenke_fgh": {"weight": 1.0, "set": {
        "doc_note": [("pfand",), ("pfand", "eigentum")],
        "font_pool": ["sans", "condensed"], "order_pick": [23, 24, 13],
        "force_codes": True, "qty_unit_glue": True,
        "table_style": ["rules", "grid"], "header_style": ["bold", "inverted"],
        "totals_style": ["table", "panel"], "meta_style": ["grid", "boxed"],
        "logo": ["wordmark", "band"], "footer": ["columns3", "columns4"]}},
    # Leergutabrechnung: ueberwiegend negative Positionsbetraege, Gebindenamen,
    # keine Artikelnummern, "Rueckgabe" als Abschnitt.
    "leergut": {"weight": 0.7, "set": {
        "doc_note": [("pfand",)], "font_pool": ["condensed", "sans"],
        "order_pick": [3, 2, 20, 25], "table_style": ["borderless", "rules"],
        "header_style": ["plain", "bold"], "totals_style": ["block", "table"],
        "meta_style": ["row", "grid"], "logo": ["none", "wordmark"], "footer": ["line", "bank"]}},
    # Brauerei, Gebindeabrechnung: Faesser und Kaesten, "24 x 0,33 l MW",
    # Pfandzeilen, Preis je Hektoliter.
    "brauerei2": {"weight": 0.8, "set": {
        "doc_note": [("pfand",), ("pfand", "eigentum")], 
        "font_pool": ["serif", "sans"], "order_pick": [23, 31, 0],
        "price_header_unit": ["", "je"], "table_style": ["rules", "dotted"],
        "header_style": ["smallcaps", "bold"], "totals_style": ["table", "block"],
        "meta_style": ["pairs", "boxed"], "logo": ["wordmark2", "mark"],
        "footer": ["columns3", "bank"]}},
    # Mineralbrunnen: Kastenlieferung an die Gastronomie, Leergutsaldo,
    # Liefertour, sehr schmale Tabelle.
    "mineralbrunnen": {"weight": 0.7, "set": {
        "doc_note": [("pfand",)], "font_pool": ["sans", "condensed"],
        "order_pick": [24, 30], "qty_unit_glue": True,
        "table_style": ["rules", "borderless"], "header_style": ["bold", "plain"],
        "totals_style": ["table", "block"], "meta_style": ["grid", "row"],
        "logo": ["mark", "wordmark"], "footer": ["columns2", "line"]}},
    # Getraenke auf Kommission: gelieferte und zurueckgenommene Mengen in einer
    # Tabelle, die Differenz wird berechnet.
    "getraenke_kommission": {"weight": 0.6, "set": {
        "doc_note": [("pfand",), ()], "font_pool": ["condensed", "sans"],
        "order_pick": [25, 26], "force_codes": True,
        "table_style": ["grid", "rules"], "header_style": ["boxed", "bold"],
        "totals_style": ["grid", "table"], "meta_style": ["grid", "boxed"],
        "logo": ["none", "wordmark"], "footer": ["columns3", "line"]}},
    # -------------------------------------------------------- Fleisch und Fisch
    # Metzgerei, zweite Auspraegung — der Beleg mit dem zweizeiligen Preiskopf
    # "Preis je" / "kg bzw. Stueck" und dem ebenfalls zweizeiligen "Betrag".
    # Kilogrammmengen mit drei Nachkommastellen, Tagesueberschriften.
    "metzgerei2": {"weight": 1.0, "set": {
        "price_header_unit": "two", "qty_style": "d3", "sections": "days",
        "font_pool": ["serif", "sans"], "order_pick": [0, 20, 27],
        "table_style": ["rules", "grid"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "meta_style": ["pairs", "boxed"],
        "logo": ["wordmark", "mark"], "footer": ["bank", "columns3"],
        "second_row_details": True}},
    # Wurst- und Fleischwarenfabrik: Chargennummern, MHD, Preis je kg, dichte
    # Tabelle, Warengruppenspalte.
    "wurstwaren": {"weight": 0.8, "set": {
        "price_header_unit": ["je", "ep"], "qty_style": ["d3", "d2"], "force_codes": True,
        "font_pool": ["condensed", "sans"], "order_pick": [13, 0],
        "table_style": ["rules", "dotted"], "header_style": ["bold", "bodyrow"],
        "totals_style": ["table", "grid"], "meta_style": ["grid", "row"],
        "logo": ["wordmark", "none"], "footer": ["columns4", "columns3"],
        "second_row_details": True}},
    # Gefluegelhof: Direktvermarkter, wenige Positionen, Preis je kg, Serife.
    "gefluegel": {"weight": 0.6, "set": {
        "price_header_unit": ["je", "pro"], "qty_style": ["d3", "trim"],
        "font_pool": "serif", "order_pick": [3, 20], "table_style": ["dotted", "borderless"],
        "header_style": ["plain", "smallcaps"], "totals_style": ["block", "sentence"],
        "meta_style": ["stack", "dateline"], "logo": ["mark", "none"], "footer": ["line", "bank"]}},
    # Wildhandel: Saison, Zerlegehinweis in der Detailzeile, Preis je kg.
    "wildhandel": {"weight": 0.6, "set": {
        "price_header_unit": "je", "qty_style": "d3", "font_pool": ["serif", "sans"],
        "order_pick": [21, 2], "table_style": ["rules", "dotted"],
        "header_style": ["smallcaps", "bold"], "totals_style": ["block", "table"],
        "meta_style": ["pairs", "stack"], "logo": ["wordmark2", "mark"],
        "footer": ["bank", "columns2"], "second_row_details": True}},
    # Fischgrosshandel, zweite Auspraegung: Gewichtsspalten, drei Nachkommastellen,
    # Fanggebiet und Fangdatum in der Detailzeile, "EP/kg" als Preiskopf.
    "fisch2": {"weight": 0.9, "set": {
        "price_header_unit": ["ep", "je"], "qty_style": "d3", "force_codes": True,
        "font_pool": ["sans", "condensed"], "order_pick": [24, 13, 0],
        "table_style": ["grid", "rules"], "header_style": ["bold", "boxed"],
        "totals_style": ["table", "grid"], "meta_style": ["grid", "boxed"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "columns4"],
        "second_row_details": True}},
    # ----------------------------------------------------- Backwaren, Molkerei
    # Baeckerei-Tageslieferschein: sehr viele kleine Positionen, Tagesspalten,
    # nackte Mengen, schmale Tabelle, Summen ganz unten.
    "baeckerei_ls": {"weight": 1.2, "set": {
        "sections": "days", "font_pool": ["condensed", "sans"],
        "order_pick": [8, 28, 26], "size": [7.0, 7.4, 7.8],
        "table_style": ["rules", "dotted"], "header_style": ["plain", "bodyrow"],
        "totals_style": ["block", "table"], "totals_bottom": True,
        "meta_style": ["row", "grid"], "logo": ["none", "wordmark"],
        "footer": ["line", "columns2"], "cell_pad_y": [0.6, 0.9]}},
    # Konditorei / Patisserie: wenige, teure Positionen, lange Namen, Serife.
    "konditorei": {"weight": 0.6, "set": {
        "font_pool": "serif", "order_pick": [20, 3], "name_width": [60.0, 68.0],
        "narrow_name": False, "table_style": ["borderless", "dotted"],
        "header_style": ["smallcaps", "plain"], "totals_style": ["block", "sentence"],
        "meta_style": ["stack", "dateline"], "logo": ["wordmark2", "mark"],
        "footer": ["line", "bank"], "bg_art": ""}},
    # Muehle / Backmittel: Saecke und Paletten, Preis je 100 kg, Lieferschein-Ton.
    "muehle": {"weight": 0.7, "set": {
        "price_header_unit": ["je", "pro"], "basis_inline": True,
        "font_pool": ["sans", "condensed"], "order_pick": [7, 23],
        "table_style": ["rules", "grid"], "header_style": ["bold", "underline"],
        "totals_style": ["table", "block"], "meta_style": ["boxed", "grid"],
        "logo": ["mark", "wordmark"], "footer": ["columns3", "bank"]}},
    # Molkerei / Kaeserei: Kilogramm und Stueck gemischt, 7 % und 19 % in einer
    # Tabelle, Steuerschluessel je Zeile.
    "molkerei": {"weight": 0.8, "set": {
        "price_header_unit": ["je", ""], "force_codes": True,
        "font_pool": ["sans", "condensed"], "order_pick": [27, 13],
        "table_style": ["rules", "zebra"], "header_style": ["bold", "inverted"],
        "totals_style": ["table", "grid"], "meta_style": ["grid", "row"],
        "logo": ["wordmark", "band"], "footer": ["columns3", "columns4"]}},
    # Kaese-Affineur: franzoesische Artikelnamen auf deutscher Rechnung, Preis je
    # kg, Reifegrad in der Detailzeile, Serife.
    "kaese_affineur": {"weight": 0.7, "set": {
        "price_header_unit": ["je", "ep"], "qty_style": "d3", "font_pool": "serif",
        "order_pick": [21, 20], "name_width": [58.0, 66.0], "narrow_name": False,
        "table_style": ["dotted", "rules"], "header_style": ["smallcaps", "plain"],
        "totals_style": ["block", "table"], "meta_style": ["pairs", "stack"],
        "logo": ["wordmark2", "mark"], "footer": ["bank", "columns2"],
        "second_row_details": True}},
    # Eierhof / Regionalvermarkter: wenige Positionen, Hoefliste, Handschrift-nahe
    # Anmutung, keine Artikelnummern.
    "eierhof": {"weight": 0.6, "set": {
        "font_pool": ["serif", "sans"], "order_pick": [3, 8, 20], "table_style": "borderless",
        "header_style": ["plain", "underline"], "totals_style": ["sentence", "block"],
        "meta_style": ["dateline", "stack"], "logo": ["none", "mark"],
        "footer": ["line", "address"], "page_format": ["a4", "a5"]}},
    # ------------------------------------------------- Obst, Gemuese, Grossmarkt
    # Grossmarkt-Abholbeleg auf A4 im Bonstil: dicktengleich, keine Linien,
    # Warengruppenziffern, Uhrzeit und Standnummer im Kopf, Summen als Block.
    "grossmarkt_a4": {"weight": 0.8, "set": {
        "font_pool": "mono", "size": [7.2, 7.6, 8.0], "order_pick": [26, 8, 12],
        "force_codes": True, "table_style": "borderless", "header_style": ["plain", "bodyrow"],
        "totals_style": ["block", "table"], "totals_side": ["full", "left"],
        "meta_style": ["row", "line"], "logo": ["none", "wordmark"],
        "footer": ["line", "none"], "uppercase_headers": True}},
    # Bio-Grosshandel: Zertifikatsnummer (DE-OEKO-006) in der Fusszeile, Herkunft
    # in der Detailzeile, Kilogramm mit drei Stellen.
    "bio_grosshandel": {"weight": 0.7, "set": {
        "price_header_unit": ["je", ""], "qty_style": ["d3", "d2"],
        "font_pool": ["sans", "serif"], "order_pick": [24, 2],
        "table_style": ["rules", "dotted"], "header_style": ["plain", "bold"],
        "totals_style": ["table", "block"], "meta_style": ["pairs", "grid"],
        "logo": ["mark", "wordmark"], "footer": ["columns3", "bank"],
        "second_row_details": True, "accent": ["#2f4f2f", "#1b6b5a"]}},
    # Kraeuter und Gewuerze: kleine Mengen, Gramm, Preis je 100 g, Preisbasis.
    "kraeuter": {"weight": 0.6, "set": {
        "price_header_unit": ["je", "cur"], "basis_inline": True,
        "font_pool": ["serif", "sans"], "order_pick": [7, 20],
        "table_style": ["dotted", "rules"], "header_style": ["smallcaps", "plain"],
        "totals_style": ["block", "table"], "meta_style": ["stack", "pairs"],
        "logo": ["wordmark2", "none"], "footer": ["bank", "line"]}},
    # ---------------------------------------------------- Kaffee, Tee, Feinkost
    # Kaffeeroesterei, zweite Auspraegung: Preis je kg, Herkunft und Roestdatum
    # unter der Position, englische Schluessel gemischt.
    "kaffee_roesterei2": {"weight": 0.8, "set": {
        "price_header_unit": ["je", "ep"], "qty_style": ["d3", "trim"],
        "font_pool": ["serif", "sans"], "order_pick": [21, 20],
        "table_style": "borderless", "header_style": ["smallcaps", "letterspaced"],
        "totals_style": ["block", "sentence"], "meta_style": ["stacked", "dateline"],
        "logo": "wordmark", "footer": ["line", "columns3"],
        "second_row_details": True, "name_width": [58.0, 66.0], "narrow_name": False}},
    # Teehandel: Gramm und Kilogramm gemischt, lange Sortennamen, Preisbasis.
    "tee_handel": {"weight": 0.6, "set": {
        "price_header_unit": ["je", "cur"], "font_pool": ["serif", "sans"],
        "order_pick": [7, 22], "table_style": ["dotted", "borderless"],
        "header_style": ["smallcaps", "plain"], "totals_style": ["block", "table"],
        "meta_style": ["stack", "pairs"], "logo": ["wordmark2", "mark"],
        "footer": ["bank", "line"]}},
    # Feinkost-Import: italienische und franzoesische Artikelnamen, Herkunftsland
    # als Spalte, Zollhinweis in der Fusszeile.
    "feinkost_import": {"weight": 0.8, "set": {
        "doc_note": [("innergemein",), ()], "font_pool": ["serif", "sans"],
        "order_pick": [18, 19, 21], "name_width": [58.0, 68.0], "narrow_name": False,
        "table_style": ["rules", "grid"], "header_style": ["bold", "smallcaps"],
        "totals_style": ["table", "block"], "meta_style": ["boxed", "pairs"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "columns4"]}},
    # Olivenoel und Suedfruechte: Kanister und Kartons, Preis je Liter, spanische
    # oder griechische Namen.
    "olivenoel": {"weight": 0.6, "set": {
        "price_header_unit": ["je", "pro"], "font_pool": ["serif", "sans"],
        "order_pick": [20, 24], "table_style": ["rules", "dotted"],
        "header_style": ["smallcaps", "bold"], "totals_style": ["block", "table"],
        "meta_style": ["pairs", "stack"], "logo": ["wordmark2", "mark"],
        "footer": ["bank", "columns2"]}},
    # Nudel- und Teigwarenmanufaktur: Kartons, Preis je kg, Serife, kleine Tabelle.
    "nudelmanufaktur": {"weight": 0.6, "set": {
        "price_header_unit": ["je", ""], "font_pool": "serif", "order_pick": [20, 3],
        "table_style": ["dotted", "borderless"], "header_style": ["plain", "smallcaps"],
        "totals_style": ["block", "sentence"], "meta_style": ["stack", "dateline"],
        "logo": ["wordmark2", "none"], "footer": ["line", "bank"]}},
    # ------------------------------------------------- Tiefkuehl und Convenience
    # Speiseeis und TK-Desserts: Temperaturzone, Liefertour, Kartons, Stueckzahlen.
    "eis_grosshandel": {"weight": 0.7, "set": {
        "force_codes": True, "font_pool": ["sans", "condensed"], "order_pick": [23, 13],
        "qty_unit_glue": True, "table_style": ["grid", "rules"],
        "header_style": ["inverted", "boxed"], "totals_style": ["table", "grid"],
        "meta_style": ["grid", "row"], "logo": ["wordmark", "band"],
        "footer": ["columns3", "columns4"], "second_row_details": True}},
    # Frischeservice / Convenience: gemischte Steuersaetze in einer Tabelle,
    # Steuerbuchstabe je Position, Tourennummer.
    "convenience": {"weight": 0.8, "set": {
        "force_codes": True, "font_pool": ["condensed", "sans"],
        "order_pick": [27, 29, 13], "size": [7.2, 7.6],
        "table_style": ["rules", "zebra"], "header_style": ["bold", "bodyrow"],
        "totals_style": ["table", "grid"], "meta_style": ["grid", "row"],
        "logo": ["wordmark", "none"], "footer": ["columns4", "columns3"]}},
    # Grosskuechen-Lieferservice: sehr viele Positionen, Warengruppen als
    # Zwischenueberschriften, Chargen, gemischte Saetze.
    "grosskueche": {"weight": 0.8, "set": {
        "sections": "goods", "force_codes": True,
        "font_pool": ["condensed", "sans"], "order_pick": [13, 27], "size": [6.9, 7.3],
        "table_style": ["rules", "dotted"], "header_style": ["bold", "bodyrow"],
        "totals_style": ["table", "block"], "meta_style": ["grid", "row"],
        "logo": ["wordmark", "band"], "footer": ["columns4", "columns5"],
        "cell_pad_y": [0.6, 1.0]}},
    # -------------------------------------------------------- Cash & Carry, SB
    # Cash-&-Carry-Abholbeleg: Steuerbuchstabe A/B hinter jedem Betrag,
    # Artikelcodes, Kassennummer und Uhrzeit, dicktengleich auf A4.
    "cash_carry": {"weight": 0.9, "set": {
        "force_codes": True, "font_pool": ["mono", "condensed"],
        "order_pick": [12, 10, 26], "size": [7.0, 7.4],
        "table_style": "borderless", "header_style": ["plain", "bodyrow"],
        "totals_style": ["block", "table"], "meta_style": ["row", "line"],
        "logo": ["none", "wordmark"], "footer": ["line", "columns2"],
        "uppercase_headers": True, "pos_format": ["pad", "plain"]}},
    # Selgros / Metro, zweite Auspraegung: Gittertabelle, Steuerbuchstabe je
    # Position, Positionsnummern in Zehnerschritten, Kundennummer gross im Kopf.
    "selgros2": {"weight": 0.8, "set": {
        "force_codes": True, "font_pool": ["condensed", "sans"],
        "order_pick": [13, 14], "pos_format": "step", "table_style": "grid",
        "header_style": ["boxed", "inverted"], "totals_style": ["table", "panel"],
        "meta_style": "boxed", "logo": ["wordmark", "band"], "footer": ["columns4", "columns3"]}},
    # Gastro-Bestellportal: Sammelbeleg ueber mehrere Lieferanten, Bestellnummer
    # prominent, Positionen ohne Artikelnummer, Provisionszeile im Summenblock.
    "bestell_portal": {"weight": 0.7, "set": {
        "font_pool": "sans", "order_pick": [28, 25, 22], "table_style": ["zebra", "borderless"],
        "header_style": ["inverted", "accent"], "totals_style": ["panel", "grid"],
        "meta_style": ["grid", "stacked"], "logo": ["wordmark", "band"],
        "footer": ["line", "columns2"], "head_band": True}},
    # ------------------------------------------------ Ausstattung und Dienste
    # Gastronomiebedarf: Porzellan, Glaeser, Besteck; Stueckzahlen, Bruchersatz,
    # lange Artikelnummern.
    "gastro_bedarf": {"weight": 0.8, "set": {
        "force_codes": True, "font_pool": ["sans", "condensed"], "order_pick": [0, 20],
        "table_style": ["rules", "grid"], "header_style": ["bold", "boxed"],
        "totals_style": ["table", "block"], "meta_style": ["boxed", "grid"],
        "logo": ["wordmark", "mark"], "footer": ["columns3", "columns4"],
        "name_gtin": True}},
    # Kuechentechnik: Wartungsvertrag und Ersatzteile in einem Beleg, Abschnitte
    # Lohn und Material, Arbeitswerte als Einheit.
    "kuechentechnik": {"weight": 0.7, "set": {
        "sections": "trade", "period_block": ["range", ""], "font_pool": ["sans", "serif"],
        "order_pick": [0, 23], "table_style": ["rules", "grid"],
        "header_style": ["bold", "underline"], "totals_style": ["table", "block"],
        "meta_style": ["boxed", "pairs"], "logo": ["mark", "wordmark"],
        "footer": ["columns3", "bank"]}},
    # Schankanlagen-Wartung: Reinigungsintervalle als Positionen, Objektnummer,
    # Zeitraum, wenige Zeilen.
    "schankanlage": {"weight": 0.6, "set": {
        "period_block": "range", "font_pool": ["sans", "condensed"], "order_pick": [3, 28, 20],
        "table_style": ["rules", "borderless"], "header_style": ["bold", "plain"],
        "totals_style": ["block", "table"], "meta_style": ["boxed", "grid"],
        "logo": ["mark", "none"], "footer": ["columns2", "bank"]}},
    # Mietgeschirr und Eventausstattung: Mietdauer als Zeitraum, Stueckzahlen,
    # Rueckgabezeilen, Kaution im Summenblock.
    "mietgeschirr": {"weight": 0.8, "set": {
        "period_block": "range", "font_pool": ["sans", "serif"], "order_pick": [26, 25, 8],
        "table_style": ["rules", "dotted"], "header_style": ["plain", "bold"],
        "totals_style": ["table", "block"], "meta_style": ["pairs", "boxed"],
        "logo": ["wordmark", "mark"], "footer": ["bank", "columns3"]}},
    # Zelt- und Moebelvermietung: Auf- und Abbau als eigene Positionen, Zeitraum,
    # Kaution, wenige Zeilen mit grossen Betraegen.
    "zelt_event": {"weight": 0.6, "set": {
        "period_block": "range", "prepaid": ["anzahlung", ""], "font_pool": ["sans", "serif"],
        "order_pick": [8, 20, 22], "table_style": ["rules", "borderless"],
        "header_style": ["bold", "smallcaps"], "totals_style": ["block", "table"],
        "meta_style": ["pairs", "stack"], "logo": ["wordmark", "mark"],
        "footer": ["bank", "columns2"]}},
    # Waescherei / Textilservice: Stueckzahlen je Artikel (Tischdecke, Serviette,
    # Kochjacke), Abrechnungszeitraum, nackte Mengen ohne Einheit.
    "waescherei": {"weight": 0.8, "set": {
        "period_block": "range", "font_pool": ["sans", "condensed"], "order_pick": [8, 26, 20],
        "qty_unit_glue": True, "table_style": ["rules", "zebra"],
        "header_style": ["bold", "bodyrow"], "totals_style": ["table", "block"],
        "meta_style": ["grid", "row"], "logo": ["wordmark", "none"],
        "footer": ["columns3", "line"]}},
    # Berufskleidung im Leasing: monatliche Pauschale je Traeger, Zeitraum,
    # Vertragsnummer, gleichfoermige Positionen.
    "berufskleidung": {"weight": 0.6, "set": {
        "period_block": "range", "font_pool": "sans", "order_pick": [28, 8, 20],
        "table_style": ["rules", "borderless"], "header_style": ["plain", "bold"],
        "totals_style": ["table", "panel"], "meta_style": ["grid", "boxed"],
        "logo": ["wordmark", "band"], "footer": ["columns3", "columns2"]}},
    # Hygiene- und Reinigungsmittel: Kanister und Gebinde, Gefahrstoffhinweis in
    # der Fusszeile, Preis je Liter, Artikelnummern mit Punkten.
    "hygiene_service": {"weight": 0.7, "set": {
        "price_header_unit": ["je", "cur"], "force_codes": True,
        "font_pool": ["sans", "condensed"], "order_pick": [0, 24],
        "table_style": ["rules", "grid"], "header_style": ["bold", "inverted"],
        "totals_style": ["table", "block"], "meta_style": ["grid", "boxed"],
        "logo": ["wordmark", "band"], "footer": ["columns4", "columns3"]}},
}
EXTRA_FAMILIES.update(V12_FAMILIES)


FAMILY_DOCTYPE = {n: f["doctype"] for n, f in EXTRA_FAMILIES.items() if f.get("doctype")}
# Umkehrung: welche Familien eine Belegart bedienen duerfen.
DOCTYPE_FAMILIES = {}
for _n, _d in FAMILY_DOCTYPE.items():
    DOCTYPE_FAMILIES.setdefault(_d, []).append(_n)


def merge(families):
    """Die neuen Familien in `layout.FAMILIES` legen. `layout` ruft das beim Import."""
    for name, spec in EXTRA_FAMILIES.items():
        families[name] = {"weight": spec["weight"], "set": spec["set"]}
    return families


# --------------------------------------------------------- Sprache der Vorlage

def apply_lang(spec, rng):
    """Schluessel, Spaltenkoepfe und Summenbeschriftungen in der Sprache der Familie.

    Getauscht werden nur Woerter. Klassen, Struktur und Zahlen bleiben, wie sie
    sind — eine englische Rechnung ist keine andere Rechnung, sie ist dieselbe
    mit anderen Beschriftungen.
    """
    lang = spec.get("lang", "de")
    if lang == "de":
        return spec
    table = LANG[lang]
    spec["meta_number"] = rng.choice(table["number"])
    spec["meta_date"] = rng.choice(table["date"])
    spec["meta_customer"] = rng.choice(table["customer"])
    spec["meta_order"] = rng.choice(table["order"])
    spec["meta_delivery"] = rng.choice(table["delivery"])
    spec["meta_due"] = rng.choice(table["due"])
    spec["total_net"] = rng.choice(table["net"])
    spec["total_gross"] = rng.choice(table["gross"])
    spec["total_vat"] = rng.choice(table["vat"])
    spec["sentence_net"] = rng.choice(table["net"])
    spec["sentence_gross"] = rng.choice(table["gross"])
    spec["goods_label"] = rng.choice(table["subtotal"])
    # Nur die Spalten ueberschreiben, fuer die es eine Uebersetzung gibt: die
    # Spaltenliste waechst (v11 hat `brutto` dazubekommen), und ein Ersetzen statt
    # eines Ergaenzens liesse `spec["headers"]` genau diese Spalte fehlen.
    spec["headers"] = dict(spec["headers"],
                           **{c: rng.choice(v) for c, v in table["headers"].items()
                              if c in spec["headers"]})
    # Zweizeilige Koepfe gibt es in der fremden Sprache nicht — die Tabelle dafuer
    # fuehrt nur deutsche Paare.
    spec["header_two_line"] = False
    spec["dateline_word"] = ""
    # Fremde Schluessel haben keine Entsprechung in META_LABELS; die Ablenker
    # bleiben deshalb weg, statt halb uebersetzt dazustehen.
    spec["meta_keys"] = [k for k in spec["meta_keys"]
                         if k in ("customer", "order", "delivery", "due")]
    spec["meta_empty_key"] = ""
    return spec


def lang_title(spec, kind):
    """Ueberschrift und Ueberschriftschluessel in der Sprache der Familie."""
    table = LANG[spec["lang"]]
    return table["title"][kind], table["title"][kind] + " No."


# ------------------------------------------------------- Abschnittsueberschriften
#
# `blocks.item_rows` druckt `line["group"]` als Zwischenueberschrift. Welche
# Ueberschrift das ist, kommt sonst aus der Warengruppe; eine Handwerkerrechnung
# gliedert aber nach Lohn und Material, eine Sammelrechnung nach Tagen und eine
# Hotelrechnung nach Leistungsart. Der Text ist durchweg `O` und veraendert keine
# Zahl — nur die Zeichenkette in `render_lines` wird ersetzt.
SECTION_TEXTS = {
    "trade": ["Lohnkosten", "Materialkosten", "Fahrtkosten", "Geräte und Gerüst",
              "Entsorgung"],
    "nights": ["Übernachtung", "Verpflegung", "Kurtaxe und Abgaben", "Sonstige Leistungen",
               "Minibar"],
    "goods": ["Trockensortiment", "Kühlware", "Tiefkühl", "Getränke",
              "Non-Food", "Frischware"],
}


def apply_sections(spec, meta, rng):
    """Die Zwischenueberschriften der Belegart ueber die der Warengruppe legen."""
    kind = spec.get("sections")
    if not kind:
        return meta
    lines = meta["render_lines"]
    if not lines:
        return meta
    if kind == "days":
        # Eine Sammelrechnung laeuft ueber mehrere Liefertage; die Ueberschrift ist
        # das Datum. Es ist ein Datum und *nicht* das Rechnungsdatum — genau der
        # Ablenker, an dem eine Zusammensetzung scheitert, die das erste Datum nimmt.
        import datetime as _dt
        base = _dt.date.fromisoformat(meta["delivery"])
        days = max(1, min(6, 1 + len(lines) // 4))
        stamps = [(base - _dt.timedelta(days=d)).strftime("%d.%m.%Y") for d in range(days)][::-1]
        texts = [rng.choice(["Lieferung am ", "Liefertag ", "", "Tag "]) + s for s in stamps]
    else:
        pool = SECTION_TEXTS[kind]
        count = max(2, min(len(pool), 2 + len(lines) // 5))
        texts = pool[:count]
    per = max(1, (len(lines) + len(texts) - 1) // len(texts))
    for i, line in enumerate(lines):
        line["group"] = texts[min(i // per, len(texts) - 1)]
    return meta


# --------------------------------------------------------------- Neue Bausteine

def _h(text):
    """Deterministischer Streuwert. `_h()` ist fuer Zeichenketten je Prozess
    zufaellig (PYTHONHASHSEED) — damit waere der Korpus nicht mehr reproduzierbar,
    und genau das ist die Zusage von `--seed`."""
    return zlib.crc32(str(text).encode("utf-8"))


def _w(text, field="O", line=0):
    return B.words(text, field, line)


def _kv(key, kf, value, vf, line=0, colon=True):
    """Schluessel mit geklebtem Doppelpunkt, Wert daneben. Der geklebte Doppelpunkt
    ist Absicht: als eigenes Token faellt er bei langen Schluesseln unter die
    IoU-Schwelle von `align.py` und zaehlt als ungesehen."""
    key = key + ":" if colon else key
    return (f'<td class=k>{_w(key, kf, line)}</td>'
            f'<td class=v>{_w(value, vf, line) if str(value).strip() else ""}</td>')


def notes(spec, invoice, meta):
    """Rechtshinweise als Fliesssatz. Alles `O` — es sind Saetze, keine Schluessel,
    und genau deshalb der haerteste Ablenker, den eine Rechnung zu bieten hat:
    "Umsatzsteuer", "Betrag" und eine Paragrafenziffer mitten im Satz."""
    keys = [k for k in spec.get("doc_note", []) if k in NOTES]
    if not keys:
        return ""
    body = "".join(f'<div class=noteline>{_w(NOTES[k][_h(k + spec["id"]) % len(NOTES[k])])}</div>'
                   for k in keys)
    # CONVENTIONS 3.9: Fliesstext nach dem Summenblock traegt die Rolle `footer`.
    return f'<div class="notes" data-role="footer">{body}</div>'


PERIOD_TEXTS = {
    "range": ("Abrechnungszeitraum", "Leistungszeitraum", "Abrechnungsperiode"),
    "meter": ("Abrechnungszeitraum", "Verbrauchszeitraum", "Ablesezeitraum"),
    "policy": ("Versicherungsperiode", "Beitragszeitraum", "Versicherungsjahr"),
    "rent": ("Abrechnungszeitraum", "Nutzungszeitraum"),
    "stay": ("Aufenthalt", "An- und Abreise", "Zeitraum"),
}


def period_block(spec, invoice, meta):
    """Zeitraumzeilen: "Abrechnungszeitraum 01.01.2025 - 31.12.2025".

    Zwei Daten in einer Zeile, keines davon das Rechnungsdatum. Auf Strom-, Gas-,
    Miet- und Hotelrechnungen steht das *ueber* der Tabelle, und eine
    Zusammensetzung, die das erste Datum der Seite nimmt, liest es als
    Rechnungsdatum.
    """
    kind = spec.get("period_block")
    if not kind:
        return ""
    import datetime as _dt
    end = _dt.date.fromisoformat(meta["delivery"])
    span = {"range": 30, "meter": 365, "policy": 365, "rent": 365, "stay": 3}[kind]
    start = end - _dt.timedelta(days=span)
    label = PERIOD_TEXTS[kind][_h(spec["id"]) % len(PERIOD_TEXTS[kind])]
    # Die beiden Daten eines Leistungs-/Abrechnungszeitraums sind `deliveryDate`
    # (CONVENTIONS 1): Daten, die ausdruecklich nicht das Rechnungsdatum sind.
    span_html = (f'{_w(start.strftime("%d.%m.%Y"), "deliveryDate")} {_w("–")} '
                 f'{_w(end.strftime("%d.%m.%Y"), "deliveryDate")}')
    rows = [(label, None)]
    if kind == "meter":
        # Zaehlernummer und zwei Zaehlerstaende: lange Ziffernfolgen ohne jede
        # Beschriftung aus unserem Satz.
        no = f'{abs(_h(spec["id"])) % 90000000 + 10000000}'
        lo = abs(_h(spec["id"] + "lo")) % 40000 + 10000
        rows.append(("Zählernummer", no))
        rows.append(("Zählerstand alt / neu", f"{lo} / {lo + 2740}"))
    elif kind == "stay":
        rows.append(("Zimmer / Personen", f'{abs(_h(spec["id"])) % 400 + 100} / 2'))
    elif kind == "policy":
        rows.append(("Versicherungsschein-Nr.",
                     f'VS-{abs(_h(spec["id"])) % 9000000 + 1000000}'))
    elif kind == "rent":
        rows.append(("Mietobjekt / Einheit", f'WE {abs(_h(spec["id"])) % 40 + 1}'))
    body = ""
    for k, v in rows:
        value = span_html if v is None else _w(v, "O")
        body += f'<tr><td class=k>{_w(k + ":", "otherLabel")}</td><td class=v>{value}</td></tr>'
    return f'<table class="periodbox">{body}</table>'


PREPAID_LABELS = {
    "anzahlung": (["abzüglich Anzahlung", "Anzahlung vom", "Geleistete Anzahlung"],
                  ["Restbetrag", "Fälliger Betrag", "Noch zu zahlen", "Zahlbetrag"]),
    "abschlag": (["Geleistete Abschlagszahlungen", "abzüglich Abschläge",
                  "Bereits gezahlte Abschläge"],
                 ["Fälliger Betrag", "Nachzahlung", "Restbetrag", "Zu zahlen"]),
    "guthaben": (["Guthaben aus Vorperiode", "abzüglich Guthaben"],
                 ["Fälliger Betrag", "Auszahlungsbetrag", "Restbetrag"]),
    "balance": (["Paid to date", "Less payments received", "Amount paid"],
                ["Amount Due", "Balance Due", "Total Due"]),
}


def prepaid_rows(spec, invoice, meta):
    """Vorauszahlungen und der Betrag, der danach noch offen ist.

    Das ist der Fall, in dem der *Rechnungsbetrag* nicht der *zu zahlende Betrag*
    ist: Abschlagsrechnung, Schlussrechnung, Jahresabrechnung des Versorgers,
    englische Rechnung mit "Amount Due". Die Vorauszahlung bleibt `O`, ihre
    Beschriftung `otherLabel`, und der Restbetrag ist `amountDue` — eine eigene
    Klasse, weil er sonst als `grossTotal` gelesen wird und die Summe kapert.
    """
    kind = spec.get("prepaid")
    # Eine Gutschrift fordert nichts ein: "bereits gezahlt" und "faelliger Betrag"
    # ergeben darauf keinen Sinn, und gerechnet kaeme ein groesserer negativer
    # Betrag heraus als die Bruttosumme selbst.
    if not kind or meta["kind"] in ("delivery_note", "credit"):
        return []
    gross = invoice["grossTotal"]
    paid = abs(meta["paid"])
    if not paid or paid >= abs(gross):
        paid = (abs(gross) // 3 // 100) * 100
    if not paid:
        return []
    if not paid:
        return []
    due = gross - paid
    pre, post = PREPAID_LABELS[kind]
    i = _h(spec["id"] + kind)
    cur = spec.get("currency", "EUR")
    sign = "– " if kind != "balance" else ""
    return [([(pre[i % len(pre)], "otherLabel", 0)],
             sign + B.cents(spec, abs(paid)) + " " + cur, "discount"),
            ([(post[(i // 7) % len(post)], "otherLabel", 0)],
             B.cents(spec, due) + " " + cur, "amountDue")]


def prepaid_block(spec, invoice, meta):
    rows = prepaid_rows(spec, invoice, meta)
    if not rows:
        return ""
    shade = " shaded" if spec["totals_shade"] else ""
    body = "".join(f'<tr class="{"grand" if f == "amountDue" else ""}">'
                   f'<td class=k>{B.cell(k)}</td><td class=v>{_w(v, f)}</td></tr>'
                   for k, v, f in rows)
    return (f'<table class="totals duebox side-{spec["totals_side"]}{shade}" '
            f'data-role="total">{body}</table>')


def qr_bill(spec, invoice, meta):
    """Schweizer QR-Rechnung am Blattfuss: Empfangsschein links, Zahlteil rechts,
    dazwischen das QR-Feld. Der Betrag steht hier ein zweites Mal — und bleibt
    `O`, sonst haette die Seite zwei Bruttosummen und die Zusammensetzung
    naehme die falsche. Die IBAN ist `bankId`."""
    if not spec.get("qr_bill"):
        return ""
    sup = meta["supplier"]
    cus = meta["customer"]
    amount = B.cents(spec, invoice["grossTotal"])
    left = "".join(f'<div class=qrl>{_w(k, "otherLabel")} {_w(v, f)}</div>' for k, v, f in (
        ("Konto / Zahlbar an", sup["iban"], "bankId"),
        ("", sup["name"], "O"),
        ("Zahlbar durch", cus["name"], "O"),
        ("Währung", "CHF", "O"),
        ("Betrag", amount, "O")))
    right = "".join(f'<div class=qrl>{_w(k, "otherLabel")} {_w(v, f)}</div>' for k, v, f in (
        ("Konto / Zahlbar an", sup["iban"], "bankId"),
        ("", f'{sup["name"]}, {sup["street"]}, {sup["zip"]} {sup["city"]}', "O"),
        ("Referenz", f'RF{_h(spec["id"]) % 90 + 10} {invoice["number"]}', "O"),
        ("Zahlbar durch", f'{cus["name"]}, {cus["zip"]} {cus["city"]}', "O")))
    return (f'<div class="qrbill" data-floor=1>'
            f'<div class=qrpart><div class=qrhead>{_w("Empfangsschein")}</div>{left}</div>'
            f'<div class=qrpart wide><div class=qrhead>{_w("Zahlteil")}</div>'
            f'<div class=qrsq></div>{right}'
            f'<div class=qrl>{_w("Währung", "otherLabel")} {_w("CHF")} '
            f'{_w("Betrag", "otherLabel")} {_w(amount)}</div></div></div>')


def host_fields(spec, invoice, meta):
    """Bewirtungsbeleg: Anlass, Teilnehmer, Ort und Unterschrift als leere
    Formularzeilen unter dem Betrag. Beschriftung `otherLabel`, Wert leer."""
    if not spec.get("host_fields"):
        return ""
    rows = ["Anlass der Bewirtung", "Bewirtete Personen", "Ort, Datum",
            "Unterschrift des Bewirtenden"]
    body = "".join(f'<div class=hostline>{_w(k, "otherLabel")} '
                   f'<span class=hostrule></span></div>' for k in rows)
    tip = (f'<div class=hostline>{_w("Trinkgeld", "otherLabel")} '
           f'{_w(B.cents(spec, max(100, abs(invoice["grossTotal"]) // 20 // 100 * 100)))} '
           f'{_w("EUR")}</div>')
    return f'<div class="hostbox">{tip}{body}</div>'


def sidebar(spec, meta):
    """Ein farbiger Streifen am Blattrand mit Anschrift und Kontakt. Der
    Lieferantenname darin ist `O` — beschriftet steht er im Briefkopf; steht er
    dort nicht, traegt ihn die Fusszeile (`footer_supplier`)."""
    side = spec.get("sidebar")
    if not side:
        return ""
    sup = meta["supplier"]
    lines = [(sup["name"], "O"), (sup["street"], "O"), (sup["mail"], "O")]
    body = "".join(f'<div class=sbl>{_w(t, f)}</div>' for t, f in lines)
    body += (f'<div class=sbl>{_w(sup["zip"], "postcode")} {_w(sup["city"])}</div>'
             f'<div class=sbl>{_w("Tel.", "otherLabel")} {_w(sup["phone"], "phone")}</div>')
    keys = (f'<div class=sbl>{_w("UID", "otherLabel")} {_w(sup["vatId"], "taxId")}</div>'
            f'<div class=sbl>{_w("IBAN", "otherLabel")} {_w(sup["iban"], "bankId")}</div>')
    return f'<div class="sidebar sb-{side}" data-floor=1>{body}{keys}</div>'


# ------------------------------------------------- Positionen in anderer Form

def twocol_items(spec, lines, meta):
    """Zwei schmale Positionstabellen nebeneinander. Die Lesereihenfolge der OCR
    springt dabei zwischen den Spalten — eine Zeile der linken und eine der
    rechten Tabelle liegen auf derselben Hoehe und landen in `group_rows` in
    *einer* Zeile. Genau das passiert auf echten zweispaltigen Belegen auch."""
    half = (len(lines) + 1) // 2
    out = ""
    for part, group in enumerate((lines[:half], lines[half:])):
        if not group:
            continue
        body = "".join(B.item_block(spec, line, i) for i, line in enumerate(group))
        out += (f'<table class="items half h{part}">{B.item_header(spec)}{body}</table>')
    return f'<div class=twocol>{out}</div>'


def kv_items(spec, lines, meta):
    """Eine Position als Schluessel-Wert-Block statt als Tabellenzeile.

    `blocks.item_cells` liefert die Zellen mitsamt ihren Klassen, also gelten hier
    dieselben Konventionen wie in der Tabelle: eine Freizeile druckt keine Null,
    eine nackte Menge druckt keine Einheit.
    """
    out = ""
    for index, line in enumerate(lines):
        cells = B.item_cells(spec, line)
        rows = ""
        for c in spec["columns"]:
            # Die Positionsnummer steht schon in der Blockueberschrift.
            if c == "pos":
                continue
            body = B.cell(cells[c])
            if not body:
                continue
            rows += (f'<tr><td class=k>{_w(spec["headers"][c] + ":", "otherLabel")}</td>'
                     f'<td class=v>{body}</td></tr>')
        zebra = " z1" if index % 2 else ""
        out += (f'<div class="kvitem{zebra}" data-role="line-item" data-l="{line["no"]}">'
                f'<div class=kvhead>{_w(spec["headers"]["pos"])} {_w(B.pos_text(spec, line["no"]))}'
                f'</div><table class=kvtab>{rows}</table></div>')
    return f'<div class=kvlist>{out}</div>'


# ------------------------------------------------------- E-Rechnungs-Viewer
#
# Vier von acht echten Spirituosen-Fehlern sehen genau so aus, und der Korpus
# kannte die Form bis v10 gar nicht. Die Merkmale, an denen sie haengt:
#   * jeder Wert steht hinter einem Schluessel ("Rechnungsnummer:", "Firmenname:"),
#   * der Kaeuferblock steht VOR dem Verkaeuferblock,
#   * Abschnittsueberschriften sitzen in Kaesten,
#   * der Lieferantenname bricht in einem schmalen Formularfeld ueber zwei Zeilen,
#   * eine Position ist ein Block aus drei bis vier Zeilen mit eigenen Schluesseln,
#   * "Preiseinheit" steht als eigene Spalte zwischen Preis und Prozentspalte,
#   * der Summenblock fuehrt ZWEI "Gesamtsumme"-Zeilen (netto und brutto).
EI = {
    "xrechnung": {
        "doc": "Rechnungsdaten", "buyer": "Rechnungsempfänger",
        "seller": "Rechnungsersteller", "items": "Rechnungspositionen",
        "totals": "Zusammenfassung der Beträge", "pay": "Zahlungsdaten",
        "number": "Rechnungsnummer", "date": "Rechnungsdatum", "kind": "Art der Rechnung",
        "kindval": "Handelsrechnung (380)", "currency": "Währung der Rechnung",
        "route": "Leitweg-ID", "company": "Firmenname", "name": "Name",
        "street": "Straße", "zip": "Postleitzahl", "city": "Ort",
        "country": "Länderkennung", "custno": "Kundennummer",
        "vatid": "Umsatzsteuer-Identifikationsnummer", "iban": "IBAN des Zahlungsempfängers",
        "pos": "Pos", "artinfo": "Artikelinformationen", "qty": "Menge",
        "basis": "Preiseinheit", "price": "Einzelpreis", "rate": "USt-Satz",
        "amount": "Gesamtpreis", "desc": "Bezeichnung", "artno": "Artikelnummer",
        "artid": "Artikelkennung", "scheme": "Schema der Artikelkennung",
        "sumpos": "Summe aller Positionen", "foreign": "Summe Fremdforderungen",
        "charge": "Summe Zuschläge", "net": "Gesamtsumme (netto)",
        "vat": "Umsatzsteuer", "gross": "Gesamtsumme (brutto)",
        "prepaid": "Vorauszahlung", "due": "Fälliger Betrag",
    },
    "zugferd": {
        "doc": "Rechnungskopf", "buyer": "Käufer", "seller": "Verkäufer",
        "items": "Positionen", "totals": "Beträge", "pay": "Zahlung",
        "number": "Rechnungsnummer", "date": "Rechnungsdatum", "kind": "Dokumententyp",
        "kindval": "Rechnung (380) – Profil EN 16931", "currency": "Belegwährung",
        "route": "Geschäftsprozess", "company": "Firmenname", "name": "Ansprechpartner",
        "street": "Straße und Hausnummer", "zip": "PLZ", "city": "Ort",
        "country": "Land", "custno": "Käuferreferenz",
        "vatid": "USt-IdNr.", "iban": "IBAN",
        "pos": "Pos", "artinfo": "Artikelangaben", "qty": "Menge",
        "basis": "Preisbasis", "price": "Einzelpreis netto", "rate": "USt %",
        "amount": "Positionsbetrag", "desc": "Artikelname", "artno": "Verkäufer-Artikelnummer",
        "artid": "Globale Artikelkennung", "scheme": "Schema der Artikelkennung",
        "sumpos": "Summe der Positionsbeträge", "foreign": "Summe Fremdforderungen",
        "charge": "Summe Zuschläge", "net": "Gesamtsumme (netto)",
        "vat": "Umsatzsteuerbetrag", "gross": "Gesamtsumme (brutto)",
        "prepaid": "Bereits gezahlter Betrag", "due": "Fälliger Betrag",
    },
    "portal": {
        "doc": "1. Dokumentenangaben", "buyer": "2. Rechnungsempfänger",
        "seller": "3. Rechnungssteller", "items": "4. Rechnungspositionen",
        "totals": "5. Betragsangaben", "pay": "6. Zahlungsangaben",
        "number": "Rechnungsnummer", "date": "Rechnungsdatum", "kind": "Rechnungsart",
        "kindval": "380 Handelsrechnung", "currency": "Rechnungswährung",
        "route": "Leitweg-ID", "company": "Firmenname", "name": "Abteilung / Name",
        "street": "Straße", "zip": "Postleitzahl", "city": "Ort",
        "country": "Land", "custno": "Kundennummer", "vatid": "Umsatzsteuer-ID",
        "iban": "IBAN", "pos": "Nr.", "artinfo": "Artikelinformationen", "qty": "Menge",
        "basis": "Preiseinheit", "price": "Nettopreis", "rate": "Steuersatz",
        "amount": "Nettobetrag", "desc": "Bezeichnung", "artno": "Artikelnummer",
        "artid": "Artikelkennung", "scheme": "Schema der Artikelkennung",
        "sumpos": "Summe aller Positionen", "foreign": "Summe Fremdforderungen",
        "charge": "Summe Zuschläge", "net": "Gesamtsumme (netto)",
        "vat": "Umsatzsteuer gesamt", "gross": "Gesamtsumme (brutto)",
        "prepaid": "Anzahlungsbetrag", "due": "Fälliger Betrag",
    },
    "peppol": {
        "doc": "Invoice details", "buyer": "Buyer", "seller": "Seller",
        "items": "Invoice lines", "totals": "Document totals", "pay": "Payment means",
        "number": "Invoice number", "date": "Issue date", "kind": "Invoice type code",
        "kindval": "380 Commercial invoice", "currency": "Document currency",
        "route": "Buyer reference", "company": "Company name", "name": "Contact name",
        "street": "Street name", "zip": "Post code", "city": "City",
        "country": "Country code", "custno": "Customer number",
        "vatid": "VAT identifier", "iban": "Payment account identifier",
        "pos": "ID", "artinfo": "Item information", "qty": "Quantity",
        "basis": "Base quantity", "price": "Net price", "rate": "VAT rate",
        "amount": "Line net amount", "desc": "Item name", "artno": "Seller item identifier",
        "artid": "Item standard identifier", "scheme": "Scheme identifier",
        "sumpos": "Sum of line net amounts", "foreign": "Sum of charges",
        "charge": "Charge amount", "net": "Invoice total without VAT",
        "vat": "Invoice total VAT amount", "gross": "Invoice total with VAT",
        "prepaid": "Paid amount", "due": "Amount due for payment",
    },
}


# --------------------------------------------------------------- v12: der echte
# KoSIT-Ausdruck. Fehlerbild 2 aus v12/PLAN.md, nachgemessen an den Scans von 2025:
#
#  * die Positionszeile druckt den Namen **nackt**, ohne den Schluessel
#    "Bezeichnung:", und darunter stehen kursive Unterzeilen
#    "Artikelnummer: 40070" / "Artikelkennung: 4009862310035" /
#    "Schema der Artikelkennung: 0160". v11 kannte nur die Schluesselform.
#  * der Summenblock druckt **"Gesamtsumme" nackt, zweimal** — erst netto, dann
#    brutto —, dazu "Summe aller Positionen", "Summe Umsatzsteuer",
#    "Summe Fremdforderungen 0,00" und "Faelliger Betrag".
#  * darunter ein eigener Kasten "Aufschluesselung der Umsatzsteuer auf Ebene der
#    Rechnung" mit "Umsatzsteuerkategorie: S", **noch einem** "Gesamtsumme",
#    "Umsatzsteuersatz 19,00%" und "Umsatzsteuerbetrag".
#
# Damit steht dasselbe Wort "Gesamtsumme" dreimal auf der Seite und traegt jedes
# Mal eine andere Klasse: `netLabel` (Nettosumme), `grossLabel` (Bruttosumme) und
# `otherLabel` (Bemessungsgrundlage in der Aufschluesselung, Betrag `O`). Genau
# das ist die Lektion von v12 — die Klasse haengt an der Stelle, nicht am Wort.
EI_V12 = {
    "xrechnung": {
        "bare": "Gesamtsumme", "sumvat": "Summe Umsatzsteuer",
        "breakdown": "Aufschlüsselung der Umsatzsteuer auf Ebene der Rechnung",
        "cat": "Umsatzsteuerkategorie", "catval": "S", "rate2": "Umsatzsteuersatz",
        "vatamt": "Umsatzsteuerbetrag", "overview": "Übersicht", "details": "Details",
        "extras": "Zusätze", "extrahead": "Zusätzliche Angaben", "attach": "Anlage",
        "remark": "Bemerkung", "contract": "Vertragsnummer", "project": "Projektnummer",
        "means": "Zahlungsmittel", "meansval": "Überweisung (58)", "bic": "BIC",
        "duedate": "Fälligkeitsdatum", "terms": "Zahlungsbedingungen"},
    "zugferd": {
        "bare": "Gesamtsumme", "sumvat": "Summe Umsatzsteuer",
        "breakdown": "Aufschlüsselung der Umsatzsteuer auf Ebene der Rechnung",
        "cat": "Umsatzsteuerkategorie", "catval": "S", "rate2": "Umsatzsteuersatz",
        "vatamt": "Umsatzsteuerbetrag", "overview": "Übersicht", "details": "Details",
        "extras": "Zusätze", "extrahead": "Weitere Angaben", "attach": "Anhang",
        "remark": "Freitext", "contract": "Vertragsreferenz", "project": "Projektreferenz",
        "means": "Zahlungsart", "meansval": "SEPA-Überweisung (58)", "bic": "BIC",
        "duedate": "Fälligkeit", "terms": "Zahlungsbedingung"},
    "portal": {
        "bare": "Gesamtsumme", "sumvat": "Summe Umsatzsteuer",
        "breakdown": "Aufschlüsselung der Umsatzsteuer auf Ebene der Rechnung",
        "cat": "Umsatzsteuerkategorie", "catval": "S", "rate2": "Umsatzsteuersatz",
        "vatamt": "Umsatzsteuerbetrag", "overview": "Übersicht", "details": "Details",
        "extras": "Zusätze", "extrahead": "7. Zusätzliche Angaben", "attach": "Anlage",
        "remark": "Bemerkung", "contract": "Vertragsnummer", "project": "Projektnummer",
        "means": "Zahlungsmittel", "meansval": "58 Überweisung", "bic": "BIC",
        "duedate": "Fälligkeitsdatum", "terms": "Zahlungsbedingungen"},
    "peppol": {
        "bare": "Total amount", "sumvat": "Invoice total VAT amount",
        "breakdown": "VAT breakdown on document level", "cat": "VAT category",
        "catval": "S", "rate2": "VAT rate", "vatamt": "VAT category tax amount",
        "overview": "Overview", "details": "Details", "extras": "Additional information",
        "extrahead": "Additional document information", "attach": "Attachment",
        "remark": "Note", "contract": "Contract reference", "project": "Project reference",
        "means": "Payment means type", "meansval": "58 Credit transfer", "bic": "BIC",
        "duedate": "Payment due date", "terms": "Payment terms"},
}
for _v, _extra in EI_V12.items():
    EI[_v].update(_extra)

# Der Drei-Seiten-Schnitt druckt die Gesamtbetraege auf die **Uebersicht**, so wie der
# echte KoSIT-Ausdruck. Moeglich, seit `validate.py` die Summen gegen jede Seite mit
# einer Region `role == "total"` prueft statt gegen `pages[-1]` (validate.py:218-221);
# vorher haette jede geschnittene Variation eine Beanstandung ergeben.
EI_SPLIT_TOTALS_ON_PAGE1 = True


# UN/ECE Rec 20/21 Codes, wie ein Viewer sie druckt. Der Code ersetzt den
# Einheitentext NICHT — er steht in der Preiseinheit-Zelle daneben. Druckt die
# Zeile gar keine Einheit (`unitText` null), steht dort auch kein Code: die
# Konvention "keine gedruckte Einheit heisst unitText null" gilt auch hier.
def _ei_box(spec, title, rows, cls=""):
    head = f'<div class=eihead>{_w(title)}</div>'
    body = "".join(f'<tr>{_kv(k, kf, v, vf)}</tr>' for k, kf, v, vf in rows if str(v).strip()
                   or kf == "otherLabel")
    return f'<div class="eibox {cls}">{head}<table class=eitab>{body}</table></div>'


def _ei_party(spec, T, party, name_field, extra):
    rows = [(T["company"], "otherLabel", party["name"], name_field),
            (T["street"], "otherLabel", party["street"], "O"),
            (T["zip"], "otherLabel", party["zip"], "postcode"),
            (T["city"], "otherLabel", party["city"], "O"),
            # Der Kundenblock fuehrt kein Land; die vierstellige PLZ ist oesterreichisch.
            (T["country"], "otherLabel",
             party.get("country") or ("AT" if len(str(party["zip"])) == 4 else "DE"), "O")]
    return rows + extra


def einvoice_items(spec, T, lines, meta):
    """Eine Position als Block aus drei bis vier Zeilen.

    Die erste Zeile traegt Menge, Preiseinheit, Preis, Satz und Betrag, die
    Folgezeilen die Artikelangaben, jede hinter ihrem eigenen Schluessel. Alle
    Schluessel sind `otherLabel`, alle Zusatzwerte `O` — nur die Bezeichnung, die
    Artikelnummer und die Artikelkennung tragen ihre Klasse.
    """
    cols = [T["pos"], T["artinfo"], T["qty"], T["basis"], T["price"], T["rate"], T["amount"]]
    head = ("<thead data-role=\"column-header\"><tr>"
            + "".join(f'<th class="ec{i}">{_w(c)}</th>' for i, c in enumerate(cols))
            + "</tr></thead>")
    body = ""
    for index, line in enumerate(lines):
        no = line["no"]
        free = line.get("free")
        qty = [(B.quantity(spec, line["quantity"]), "quantity", no)]
        if line["unitText"]:
            qty.append((line["unitText"], "unit", no))
        basis = [(B.quantity(spec, line["priceBaseQty"]) if line["priceBaseQty"] else "1",
                  "priceBasis", no)]
        if line["unitText"]:
            # Der UN/ECE-Code als gedruckter Einheitentext. Nur wo die Zeile
            # ueberhaupt eine Einheit druckt — sonst verbietet die Konvention ihn.
            basis.append((line["unitCode"], "unit", no))
        price = [] if free else [(B.unit_price(spec, line["unitPrice"]), "unitPrice", no)]
        amount = [] if free else [(B.cents(spec, line["lineNet"]), "lineNet", no)]
        rate = [(money.pct(line["vat"]), "vat", no)]
        # v12: der echte KoSIT-Ausdruck setzt den Namen NACKT in die Zelle, ohne
        # den Schluessel "Bezeichnung:" davor. Das ist die Mehrheitsform
        # (`ei_bare`, >= 60 %); die Schluesselform von v11 bleibt die Minderheit.
        desc = ("" if spec.get("ei_bare")
                else _w(T["desc"] + ":", "otherLabel", no) + " ")
        first = (f'<td class=ec0>{_w(B.pos_text(spec, no), "O", no)}</td>'
                 f'<td class=ec1>{desc}'
                 f'{_w(line["name"], "name", no)}</td>'
                 f'<td class="ec2 num">{B.cell(qty)}</td>'
                 f'<td class="ec3 num">{B.cell(basis)}</td>'
                 f'<td class="ec4 num">{B.cell(price)}</td>'
                 f'<td class="ec5 num">{B.cell(rate)}</td>'
                 f'<td class="ec6 num">{B.cell(amount)}</td>')
        subs = []
        if line["sellerArticleId"]:
            subs.append((T["artno"], line["sellerArticleId"], "articleId"))
        if line["gtin"]:
            subs.append((T["artid"], line["gtin"], "gtin"))
            # "Schema der Artikelkennung: 0160" — GS1. Der Wert ist eine
            # vierstellige Zahl direkt unter einer dreizehnstelligen und bleibt `O`.
            subs.append((T["scheme"], "0160", "O"))
        elif line.get("variant"):
            subs.append((T["scheme"], line["variant"], "O"))
        # Die Unterzeilen stehen im echten Ausdruck KURSIV unter dem Namen.
        sub_cls = "wrap eisub" if spec.get("ei_bare") else "wrap"
        rest = "".join(f'<tr class="{sub_cls}"><td class=ec0></td><td class=ec1 colspan=6>'
                       f'{_w(k + ":", "otherLabel", no)} {_w(v, f, no)}</td></tr>'
                       for k, v, f in subs)
        body += (f'<tbody class="itembox" data-role="line-item" data-l="{no}">'
                 f'<tr class=item>{first}</tr>{rest}</tbody>')
    return f'<table class="items ei">{head}{body}</table>'


def einvoice_totals(spec, T, invoice, meta):
    """Zwei "Gesamtsumme"-Zeilen (netto und brutto), "Summe aller Positionen"
    darueber, "Summe Fremdforderungen 0,00" dazwischen und "Faelliger Betrag"
    darunter. Genau diese Reihenfolge druckt der Viewer, und genau darin liegt
    die Falle: der *erste* Betrag der Seite ist nicht der Nettobetrag."""
    cur = spec.get("currency", "EUR")
    # CONVENTIONS 3.3: `subtotal` nur, wenn Zuschlagszeilen folgen. Ohne Zuschlaege
    # ist "Summe aller Positionen" derselbe Betrag wie die Nettosumme darunter.
    rows = [((T["sumpos"], "otherLabel"), B.cents(spec, meta["goods_net"]),
             "subtotal" if meta["charges"] else "O")]
    for c in meta["charges"]:
        rows.append(((spec["charge_labels"][c["kind"]], "otherLabel"),
                     B.cents(spec, c["amount"]), "charge"))
    rows.append(((T["foreign"], "otherLabel"), B.cents(spec, 0), "O"))
    # v12: die nackte Form druckt ZWEIMAL "Gesamtsumme" — erst netto, dann brutto,
    # dazwischen "Summe Umsatzsteuer". Der erste Betrag der Seite ist damit nicht
    # der Nettobetrag, und zwei gleich beschriftete Zeilen unterscheiden sich nur
    # noch durch ihre Stelle im Block. Die alte Form "(netto)"/"(brutto)" bleibt
    # als Minderheit stehen.
    bare = spec.get("ei_bare")
    net_key, gross_key = (T["bare"], T["bare"]) if bare else (T["net"], T["gross"])
    rows.append(((net_key, "netLabel"), B.cents(spec, invoice["netTotal"]), "netTotal"))
    if bare:
        total_tax = sum(b["tax"] for b in invoice["vatBreakdown"])
        rows.append(((T["sumvat"], "vatLabel"), B.cents(spec, total_tax), "O"))
    else:
        for b in invoice["vatBreakdown"]:
            rows.append(((f'{T["vat"]} {money.pct(b["vat"])} %', "vatLabel"),
                         B.cents(spec, b["tax"]), "O"))
    rows.append(((gross_key, "grossLabel"), B.cents(spec, invoice["grossTotal"]),
                 "grossTotal"))
    due = prepaid_rows(spec, invoice, meta)
    if due:
        rows.append(((T["prepaid"], "otherLabel"), due[0][1], "discount"))
        rows.append(((T["due"], "otherLabel"), due[1][1], "amountDue"))
    else:
        # CONVENTIONS: `amountDue` nur, wenn eine Anzahlungszeile existiert UND der
        # Betrag vom Bruttobetrag abweicht. Sonst wiederholt "Faelliger Betrag" nur
        # die Bruttosumme, die eine Zeile darueber steht, und bleibt `O`.
        rows.append(((T["due"], "otherLabel"), B.cents(spec, invoice["grossTotal"]) + " " + cur,
                     "O"))
    body = ""
    for (key, kf), value, vf in rows:
        # Der MwSt-*Satz* steht im Schluessel und traegt dort seine Klasse; der
        # Betrag daneben bleibt `O`, wie ueberall im Summenblock.
        if kf == "vatLabel" and key.endswith("%"):
            # Nur die Form "Umsatzsteuer 19,00 %": dort traegt der Satz im
            # Schluessel seine eigene Klasse. "Summe Umsatzsteuer" ist ein
            # gewoehnlicher Schluessel und bekommt wie jeder andere den
            # geklebten Doppelpunkt.
            parts = key.rsplit(" ", 2)
            keyhtml = (f'{_w(parts[0], "vatLabel")} {_w(parts[1], "vat")} '
                       f'{_w(parts[2], "O")}') if len(parts) == 3 else _w(key, "vatLabel")
        else:
            keyhtml = _w(key + ":", kf)
        grand = " grand" if vf in ("grossTotal", "amountDue") else ""
        body += (f'<tr class="{grand}"><td class=k>{keyhtml}</td>'
                 f'<td class=v>{_w(value, vf)}</td></tr>')
    return (f'<div class="eibox eitotals"><div class=eihead>{_w(T["totals"])}</div>'
            f'<table class="eitab totals" data-role="total">{body}</table></div>')


def einvoice_breakdown(spec, T, invoice, meta):
    """`Aufschluesselung der Umsatzsteuer auf Ebene der Rechnung`.

    Je Steuersatz ein Viererblock: `Umsatzsteuerkategorie: S`, `Gesamtsumme` mit
    der Bemessungsgrundlage, `Umsatzsteuersatz 19,00%`, `Umsatzsteuerbetrag`.

    Die Klassen, und warum sie so sind:

    * `Umsatzsteuerkategorie` ist ein fremder Schluessel -> `otherLabel`, das `S`
      daneben `O`.
    * `Gesamtsumme` heisst hier genauso wie die Nettosumme eine Zeile hoeher, ist
      aber die Bemessungsgrundlage *eines Satzes* und nicht der Nettobetrag der
      Rechnung. CONVENTIONS 3.3 laesst `subtotal` nur zu, wenn Zuschlagszeilen
      folgen — hier folgt keine. Also Schluessel `otherLabel`, Betrag `O`. Das
      dritte "Gesamtsumme" der Seite ist damit das einzige, das *nichts* ist, und
      genau daran soll das Modell lernen, dass die Stelle entscheidet.
    * Der *Satz* ist `vat`, das Prozentzeichen `O`, der Betrag `O` — wie ueberall
      im Summenblock (CONVENTIONS, "Der MwSt-Betrag bleibt O, der Satz vat").
    """
    breakdown = invoice.get("vatBreakdown") or []
    if not breakdown:
        return ""
    body = ""
    for b in breakdown:
        base = b.get("net", b.get("base", 0))
        pct = money.de(b["vat"], money.BP // 100, 2)
        for key, kf, value, vf, glue in (
                (T["cat"], "otherLabel", T["catval"], "O", False),
                (T["bare"], "otherLabel", B.cents(spec, base), "O", False),
                (T["rate2"], "otherLabel", pct, "vat", True),
                (T["vatamt"], "vatLabel", B.cents(spec, b["tax"]), "O", False)):
            value_html = _w(value, vf)
            if glue:
                # "19,00%" ohne Leerzeichen — die Form, die das Modell auf einem
                # echten Beleg als Einzelpreis gelesen hat.
                value_html = (f'<span class=w data-f="{vf}" data-l="0">{B.esc(value)}</span>'
                              f'<span class=w data-f="O" data-l="0">%</span>')
            body += (f'<tr><td class=k>{_w(key + ":", kf)}</td>'
                     f'<td class=v>{value_html}</td></tr>')
    return (f'<div class="eibox eitotals eibreak"><div class=eihead>'
            f'{_w(T["breakdown"])}</div>'
            f'<table class="eitab totals">{body}</table></div>')


def einvoice_pay(spec, T, invoice, meta):
    """Der Kasten `Zahlungsdaten`: IBAN, BIC, Zahlungsmittel, Faelligkeit."""
    sup = meta["supplier"]
    rows = [(T["means"], "otherLabel", T["meansval"], "O"),
            (T["iban"], "otherLabel", sup["iban"], "bankId"),
            (T["bic"], "otherLabel", sup.get("bic") or "", "bankId"),
            (T["duedate"], "otherLabel", meta.get("due_text") or "", "dueDate"),
            (T["terms"], "otherLabel", spec.get("footer_terms") or "", "O")]
    return _ei_box(spec, T["pay"], rows, "eipay")


def einvoice_extras(spec, T, invoice, meta):
    """Der Kasten `Zusaetzliche Angaben` auf der dritten Seite: Bemerkung,
    Anlage, Vertrags- und Projektnummer. Reine Ablenker — Schluessel
    `otherLabel`, Werte `O`."""
    ident = _h(spec["id"])
    rows = [(T["remark"], "otherLabel", spec.get("thanks_note") or "", "O"),
            (T["attach"], "otherLabel", f'anhang-{ident % 9000 + 1000}.pdf', "O"),
            (T["contract"], "otherLabel", f'V-{ident % 900000 + 100000}', "O"),
            (T["project"], "otherLabel", f'P{ident % 9000 + 1000}-{ident % 90 + 10}', "O")]
    return _ei_box(spec, T["extrahead"], rows, "eiextra")


def _ei_pagehead(spec, T, invoice, section):
    """Kopfzeile der zweiten und dritten Seite: Abschnittsname und die
    Rechnungsnummer. Ohne sie traegt die Seite keine Rechnungsnummer, und
    `validate.py` besteht auf genau einem Nummernlauf je Seite."""
    return (f'<div class=eipagehead>{_w(section)} '
            f'<span class=eipn>{_w(T["number"] + ":", "numberLabel")} '
            f'{_w(invoice["number"], "invoiceNumber")}</span></div>')


def einvoice_page(spec, invoice, meta, rng, page_lines, index, total, carry):
    """Der ganze Ausdruck eines E-Rechnungs-Viewers. `render.page_html` ruft ihn
    statt des normalen Seitenaufbaus, sobald `spec["einvoice"]` gesetzt ist.

    Zwei Formen:

    * **ungeschnitten** (die Mehrheit): Kopf, Parteien, Positionen und — auf der
      letzten Seite — Summen und Aufschluesselung auf denselben Seiten.
    * **`ei_split`**: der Drei-Seiten-Schnitt des echten KoSIT-Ausdrucks,
      `Uebersicht` / `Details` / `Zusaetze`. `render.document` haengt dafuer eine
      leere Positionsgruppe vorn und hinten an, die erste und die letzte Seite
      tragen also keine Positionen.

      Abweichung vom Vorbild, bewusst und protokolliert: der Gesamtbetragskasten
      steht beim Schnitt auf der **letzten** Seite, nicht auf der Uebersicht.
      `validate.py` prueft `netTotal`/`grossTotal` gegen `truth["pages"][-1]`
      (Bitte 1 in v12/STATUS-families.md); solange das so ist, waere jede
      geschnittene Variation eine Beanstandung. `EI_SPLIT_TOTALS_ON_PAGE1`
      schaltet es um, sobald die Pruefung steht.
    """
    T = EI[spec["einvoice"]]
    sup, cus = meta["supplier"], meta["customer"]
    split = bool(spec.get("ei_split"))
    title = spec["title"] or "Rechnung"
    is_delivery = meta["kind"] == "delivery_note"

    def head_boxes():
        out = []
        doc_rows = [(T["number"], "numberLabel", invoice["number"], "invoiceNumber"),
                    (T["date"], "dateLabel", invoice["date_text"], "invoiceDate"),
                    (T["kind"], "otherLabel", T["kindval"], "O"),
                    (T["currency"], "otherLabel", spec.get("currency", "EUR"), "O"),
                    (T["route"], "otherLabel",
                     f'991-{_h(spec["id"]) % 90000 + 10000}-'
                     f'{_h(spec["id"] + "r") % 90 + 10}', "O")]
        if meta["order"]:
            doc_rows.append((spec["meta_order"], "otherLabel", meta["order"], "orderNumber"))
        out.append(_ei_box(spec, T["doc"], doc_rows, "eidoc"))
        # Kaeufer VOR Verkaeufer: die Normalform jedes Viewers und der Grund, warum
        # ein Tagger, der "der erste Firmenname oben ist der Lieferant" gelernt hat,
        # hier den Kunden liefert.
        buyer = _ei_box(spec, T["buyer"], _ei_party(spec, T, cus, "buyer", [
            (T["custno"], "otherLabel", cus["number"], "customerNumber"),
            (T["name"], "otherLabel", meta["extras"]["clerk"], "O")]), "eibuyer")
        seller = _ei_box(spec, T["seller"], _ei_party(spec, T, sup, "supplier", [
            (T["vatid"], "otherLabel", sup["vatId"], "taxId"),
            (T["iban"], "otherLabel", sup["iban"], "bankId"),
            (T["name"], "otherLabel", meta["owner_line"].replace("Inh. ", ""), "O")]),
            "eiseller")
        out.append(f'<div class=eirow>{buyer}{seller}</div>' if spec["buyer_first"]
                   else f'<div class=eirow>{seller}{buyer}</div>')
        out.append(period_block(spec, invoice, meta))
        return out

    def items_box():
        return (f'<div class="eibox eiitems"><div class=eihead>{_w(T["items"])}</div>'
                f'{einvoice_items(spec, T, page_lines, meta)}</div>')

    def totals_boxes():
        if is_delivery:
            return []
        return [einvoice_totals(spec, T, invoice, meta),
                einvoice_breakdown(spec, T, invoice, meta)]

    def carry_line():
        return (f'<div class=carry data-role="carry">{_w(spec["carry_label"])} '
                f'{_w(B.cents(spec, carry))}</div>')

    parts = []
    if not split:
        parts.append(f'<div class="eititle a-{spec["title_align"]}">{_w(title)}</div>')
        parts += head_boxes()
        parts.append(items_box())
        if index + 1 < total:
            parts.append(carry_line())
        else:
            parts += totals_boxes()
            if not is_delivery:
                parts.append(einvoice_pay(spec, T, invoice, meta))
            parts.append(notes(spec, invoice, meta))
    elif index == 0:
        # Seite 1: Uebersicht. Parteien, Rechnungsdaten, Zahlungsdaten — und,
        # sobald `validate.py` es zulaesst, auch die Gesamtbetraege.
        parts.append(f'<div class="eititle a-{spec["title_align"]}">{_w(title)} '
                     f'<span class=eisection>{_w(T["overview"])}</span></div>')
        parts += head_boxes()
        if EI_SPLIT_TOTALS_ON_PAGE1:
            parts += totals_boxes()
        if not is_delivery:
            parts.append(einvoice_pay(spec, T, invoice, meta))
    elif index + 1 < total:
        # Seiten 2..n-1: Details, also nur die Positionen.
        parts.append(_ei_pagehead(spec, T, invoice, T["details"]))
        parts.append(items_box())
        if index + 2 < total:
            parts.append(carry_line())
    else:
        # Letzte Seite: Zusaetze.
        parts.append(_ei_pagehead(spec, T, invoice, T["extras"]))
        parts.append(einvoice_extras(spec, T, invoice, meta))
        if not EI_SPLIT_TOTALS_ON_PAGE1:
            parts += totals_boxes()
        parts.append(notes(spec, invoice, meta))
    parts.append(B.pageno(spec, index, total))
    parts.append(B.footer(spec, meta))
    return parts

# ------------------------------------------------------------------- Stylesheet

def css(spec):
    """Nur die Regeln der neuen Achsen. `render.css` haengt sie hinten an, damit
    sie die Grundregeln ueberschreiben koennen."""
    accent = spec["accent"]
    mx, my = spec["page_margin"]
    out = [f"""
.notes{{margin-top:2.5mm;font-size:0.86em;line-height:1.4}}
.noteline{{margin:0.6mm 0}}
.periodbox{{margin:2mm 0;border-collapse:collapse}}
.periodbox td{{padding:0.2mm 2.5mm 0.2mm 0;vertical-align:top}}
/* Kein `nowrap` auf dem Schluessel: auf einem schmalen Blatt ist
   "Zaehlerstand alt / neu" plus zwei Zahlen breiter als die Seite, und der
   Ueberlauf schneidet dann rechts die Betragsspalte der Tabelle ab. */
.periodbox td.k{{max-width:56mm}}
.periodbox td.v{{white-space:nowrap}}
table.totals.duebox{{margin-top:1.2mm}}
table.totals.duebox td.k{{white-space:normal}}
table.totals.duebox tr.grand td{{font-weight:700;border-top:1px solid {spec["rule_ink"]}}}
.hostbox{{margin-top:3mm;font-size:0.9em}}
.hostline{{margin:1.6mm 0;display:flex;align-items:flex-end;gap:2mm}}
.hostrule{{flex:1;border-bottom:0.6px solid #666;height:0.9em}}
.twocol{{display:flex;gap:5mm;align-items:flex-start}}
.twocol .items.half{{flex:1;min-width:0}}
.kvlist{{margin-top:1.5mm}}
.kvitem{{margin-bottom:2mm;padding-bottom:1mm}}
.kvitem.z1{{background:#f4f4f4}}
.kvhead{{font-weight:700;margin-bottom:0.4mm}}
table.kvtab{{border-collapse:collapse;width:100%}}
table.kvtab td{{padding:0.2mm 3mm 0.2mm 0;vertical-align:top}}
table.kvtab td.k{{white-space:nowrap;width:30%}}
"""]
    if spec.get("einvoice"):
        frame = ("border:0.8px solid #888" if spec["einvoice"] in ("zugferd", "peppol")
                 else "background:#f2f2f2")
        head = ("background:#e2e2e2" if spec["einvoice"] in ("xrechnung", "portal")
                else f"border-bottom:0.8px solid {accent}")
        out.append(f"""
.eititle{{font-size:1.5em;font-weight:700;margin-bottom:2.5mm;color:{accent}}}
.eibox{{{frame};margin-bottom:2.5mm;padding:1.6mm 2mm}}
.eihead{{{head};font-weight:700;margin:-1.6mm -2mm 1.4mm -2mm;padding:0.9mm 2mm;
 letter-spacing:0.02em;color:{accent}}}
.eirow{{display:flex;gap:4mm;align-items:stretch}}
.eirow .eibox{{flex:1;min-width:0}}
table.eitab{{border-collapse:collapse;width:100%}}
table.eitab td{{padding:0.25mm 2mm 0.25mm 0;vertical-align:top}}
/* Der Lieferantenname bricht im schmalen Formularfeld ueber zwei Zeilen — genau
   daran scheitert der Tagger auf den echten Belegen. */
table.eitab td.k{{white-space:nowrap;width:44%;color:#333}}
table.eitab td.v{{word-break:break-word}}
.eibox.eiitems{{padding-bottom:0.6mm}}
table.items.ei{{width:100%;border-collapse:collapse}}
table.items.ei th{{text-align:left;padding:0.6mm 1.6mm;border-bottom:1px solid #888;
 vertical-align:bottom}}
table.items.ei td{{padding:0.35mm 1.6mm;vertical-align:top}}
table.items.ei td.num,table.items.ei th.ec2,table.items.ei th.ec3,table.items.ei th.ec4,
table.items.ei th.ec5,table.items.ei th.ec6{{text-align:right;white-space:nowrap}}
table.items.ei th.ec2,table.items.ei th.ec3{{text-align:right}}
table.items.ei tbody.itembox{{border-bottom:0.6px solid #ccc}}
table.items.ei td.ec0{{width:6%}}
table.items.ei td.ec1{{width:40%}}
table.eitab.totals td.v,.eitotals td.v{{text-align:right;white-space:nowrap}}
.eitotals table td.k{{width:62%}}
.eitotals tr.grand td{{font-weight:700}}
/* v12: die Unterzeilen der Position stehen im echten Ausdruck kursiv. */
table.items.ei tr.eisub td{{font-style:italic;color:#2a2a2a}}
.eisection{{font-weight:400;font-size:0.62em;letter-spacing:0.08em;
  text-transform:uppercase;color:#555;margin-left:3mm}}
.eipagehead{{font-weight:700;font-size:1.05em;margin-bottom:2.2mm;
  border-bottom:1px solid #999;padding-bottom:0.8mm;color:{accent}}}
.eipagehead .eipn{{float:right;font-weight:400;font-size:0.78em;color:#333}}
.eibox.eibreak{{margin-top:2mm}}
.eibox.eibreak table.eitab td.k{{width:58%}}
""")
    if spec.get("sidebar"):
        side = spec["sidebar"]
        width = 26
        out.append(f"""
.sidebar{{position:absolute;top:0;bottom:0;{side}:0;width:{width}mm;
 background:{accent};color:#fff;padding:{my}mm 3mm;font-size:0.72em;line-height:1.5}}
.sbl{{margin-bottom:1.4mm;word-break:break-word}}
.page{{padding-{side}:{width + mx}mm}}
""")
    if spec.get("dotmatrix"):
        out.append("""
body,.page{letter-spacing:0.06em}
.items td,.items th{letter-spacing:0.06em}
.title{letter-spacing:0.16em}
""")
    if spec.get("giant_logo"):
        out.append("""
.logo .mark{width:48mm;height:48mm}
.logo .wm{font-size:3.4em;line-height:1.05}
.head{align-items:flex-start}
""")
    if spec.get("form_fields"):
        out.append("""
table.eitab td.v{border-bottom:0.6px dotted #999}
.eibox{border:0;background:none;border-bottom:0.8px solid #aaa}
""")
    if spec.get("qr_bill"):
        out.append(f"""
.qrbill{{position:absolute;left:0;right:0;bottom:0;border-top:1px dashed #555;
 display:flex;gap:4mm;padding:2.5mm {mx}mm;font-size:0.68em;line-height:1.35}}
.qrpart{{flex:1;min-width:0}}
.qrpart[wide]{{flex:2}}
.qrhead{{font-weight:700;margin-bottom:1mm}}
.qrl{{margin-bottom:0.5mm;word-break:break-word}}
.qrsq{{width:22mm;height:22mm;border:1.4mm solid #111;margin:1mm 0}}
""")
    return "".join(out)


def floor_extra(spec):
    """Wie viel Platz die neuen Bloecke am Blattfuss brauchen. `render.floor_mm`
    addiert das, sonst laeuft der Satz in die QR-Rechnung hinein."""
    return 48.0 if spec.get("qr_bill") else 0.0


# ------------------------------------------------------ Belegarten mit Zahlen

def _recompute(invoice, meta, rate_of):
    """Steuersaetze neu setzen und alle Summen daraus neu rechnen.

    Dieselbe Rechnung wie `content.totals` + `content.add_charges`, nur mit einer
    anderen Satzzuordnung. Sie steht hier und nicht dort, weil `content.py` in
    dieser Runde dem Labels-Agenten gehoert.
    """
    for line in meta["render_lines"]:
        line["vat"] = rate_of(line["vat"])
    for line in invoice["lines"]:
        line["vat"] = rate_of(line["vat"])
    by_rate = {}
    for line in meta["render_lines"]:
        by_rate[line["vat"]] = by_rate.get(line["vat"], 0) + line["lineNet"]
    breakdown, net, gross = [], 0, 0
    for rate in sorted(by_rate):
        base = by_rate[rate]
        tax = money.round_div(base * rate, money.BP)
        breakdown.append({"vat": rate, "net": base, "tax": tax})
        net += base
        gross += base + tax
    meta["goods_net"] = net
    for row in meta["charges"]:
        row["vat"] = rate_of(row["vat"])
        hit = next((b for b in breakdown if b["vat"] == row["vat"]), None)
        if hit is None:
            hit = {"vat": row["vat"], "net": 0, "tax": 0}
            breakdown.append(hit)
            breakdown.sort(key=lambda b: b["vat"])
        hit["net"] += row["amount"]
        before = hit["tax"]
        hit["tax"] = money.round_div(hit["net"] * hit["vat"], money.BP)
        net += row["amount"]
        gross += row["amount"] + (hit["tax"] - before)
    invoice["netTotal"] = net
    invoice["grossTotal"] = gross
    # Ein Satz von 0 % druckt keine Steuerzeile: `blocks.vat_rows` laeuft ueber
    # `vatBreakdown`, und eine leere Aufschluesselung heisst "keine Steuer auf
    # diesem Beleg". Genau so sieht eine Kleinunternehmerrechnung aus.
    invoice["vatBreakdown"] = [] if all(b["vat"] == 0 for b in breakdown) else breakdown
    # Die Zuschlagssumme muss zum neuen Nettobetrag passen, sonst schlaegt
    # `validate.py` zu Recht Alarm.
    meta["skonto"] = money.round_div(abs(gross) * meta["skonto_pct"] * 100, money.BP)
    meta["paid"] = (abs(gross) // 2 // 100) * 100
    # Der eingebaute Fehler wurde gerade weggerechnet.
    meta["defect"] = None
    return invoice, meta


# 19/7 (DE) und 20/13/10 (AT) auf die Schweizer Saetze 8,1 % und 2,6 %.
SWISS_RATES = {2000: 810, 1900: 810, 1300: 260, 1000: 260, 700: 260, 0: 0}


def adapt(invoice, meta, doctype):
    """Die Zahlen einer Rechnung auf ihre Belegart bringen.

    Laeuft in `generate.one()` **vor** `expected.json`, also einmal je Rechnung:
    alle Variationen einer Rechnung drucken dieselben Zahlen, und die Wahrheit
    bleibt layoutunabhaengig.
    """
    if doctype in ("kleinunternehmer", "reverse_charge"):
        invoice, meta = _recompute(invoice, meta, lambda r: 0)
        meta["no_vat"] = True
    elif doctype == "swiss":
        invoice, meta = _recompute(invoice, meta, lambda r: SWISS_RATES.get(r, 810))
        meta["currency"] = "CHF"
    meta["doctype"] = doctype
    return invoice, meta
