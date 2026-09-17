import random

LABELS = {
    "pos": ["Pos", "Pos.", "Nr.", "Position", "#", "Zeile"],
    "artikel": ["Art.-Nr.", "ArtNr", "Artikelnummer", "Art.Nr.", "Nr.", "Artikel-Nr.", "Art. Nr."],
    "gtin": ["EAN", "GTIN", "EAN-Code", "EAN/GTIN"],
    "name": ["Bezeichnung", "Artikelbezeichnung", "Artikel", "Warenbezeichnung", "Beschreibung",
             "Text", "Bezeichnung der Ware", "Produkt"],
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
]

FONTS = [
    ("Helvetica, Arial, sans-serif", 0),
    ("Arial, Helvetica, sans-serif", 0),
    ("'Trebuchet MS', Tahoma, sans-serif", 0),
    ("Verdana, Geneva, sans-serif", -0.5),
    ("Tahoma, Verdana, sans-serif", 0),
    ("'Lucida Grande', 'Lucida Sans Unicode', sans-serif", -0.3),
    ("Optima, Candara, sans-serif", 0.3),
    ("Futura, 'Century Gothic', sans-serif", -0.2),
    ("'Gill Sans', 'Gill Sans MT', Calibri, sans-serif", 0.3),
    ("Avenir, 'Avenir Next', sans-serif", 0),
    ("'Times New Roman', Times, serif", 0.5),
    ("Georgia, 'Times New Roman', serif", 0),
    ("Palatino, 'Palatino Linotype', serif", 0.2),
    ("Baskerville, Georgia, serif", 0.4),
    ("'Hoefler Text', Georgia, serif", 0.3),
    ("Charter, Georgia, serif", 0.2),
    ("'American Typewriter', 'Courier New', serif", 0),
    ("'Courier New', Courier, monospace", 0),
    ("Menlo, Consolas, monospace", -0.5),
    ("Didot, Georgia, serif", 0.5),
    ("Calibri, Candara, sans-serif", 0.2),
    ("Cambria, Georgia, serif", 0.1),
    ("Garamond, 'EB Garamond', serif", 0.7),
    ("Rockwell, 'Courier New', serif", 0.2),
    ("'Franklin Gothic Medium', Arial, sans-serif", 0),
    ("'Segoe UI', Tahoma, sans-serif", 0),
    ("'DejaVu Sans', Verdana, sans-serif", -0.4),
    ("'Liberation Serif', 'Times New Roman', serif", 0.4),
]

TITLES = {
    "invoice": ["RECHNUNG", "Rechnung", "Rechnung Nr.", "AUSGANGSRECHNUNG", "Faktura", "R E C H N U N G",
                "Rechnung / Faktura"],
    "delivery_note": ["LIEFERSCHEIN", "Lieferschein", "Lieferschein Nr.", "L I E F E R S C H E I N",
                      "Warenbegleitschein"],
}

# Überschriften, die in der Titelzeile selbst die Nummer beschriften dürfen
# ("Rechnung Nr. 2025/0123 vom 12.03.2025"). Gesperrt gesetzte Varianten sind
# hier bewusst nicht dabei: "R E C H N U N G" wären sieben Beschriftungstokens.
TITLE_KEYS = {
    "invoice": ["Rechnung Nr.", "Rechnung", "RECHNUNG Nr.", "Rechnung-Nr.", "Faktura Nr.",
                "RECHNUNG", "Rechnung Nummer"],
    "delivery_note": ["Lieferschein Nr.", "Lieferschein", "LIEFERSCHEIN Nr.", "Lieferschein-Nr."],
}

META_LABELS = {
    "number": ["Rechnungsnummer", "Rechnungs-Nr.", "Rechnung Nr.", "Beleg-Nr.", "Nr.", "RE-Nr.",
               "Dokumentnummer", "Lieferschein-Nr.", "Belegnummer"],
    "date": ["Rechnungsdatum", "Datum", "Belegdatum", "Rechnungs-Datum", "vom", "Ausstellungsdatum",
             "Datum der Rechnung"],
    "customer": ["Kundennummer", "Kd.-Nr.", "Kunden-Nr.", "Debitor", "Kd.Nr."],
    "delivery": ["Lieferdatum", "Liefertag", "Leistungsdatum", "Lieferung am"],
    "order": ["Bestellnummer", "Ihre Bestellung", "Auftrag", "Best.-Nr."],
    "due": ["Fällig am", "Zahlbar bis", "Fälligkeit", "Zahlungsziel"],
    # Ablenker: Kopfangaben, die aussehen wie die Rechnungsnummer oder das
    # Rechnungsdatum und keines von beidem sind. Ihre Werte bleiben `O`, ihre
    # Beschriftung ist `otherLabel` — daran hängt die Unterscheidung.
    "delivery_note": ["Lieferschein-Nr.", "Lieferschein", "LS-Nr.", "Lieferscheinnummer"],
    "order2": ["Auftrags-Nr.", "Auftragsnummer", "Auftrag Nr.", "Kommission", "Kommissions-Nr."],
    "taxno": ["Steuernummer", "St.-Nr.", "UID", "USt-IdNr.", "UID-Nr.", "ATU-Nr."],
    "clerk": ["Sachbearbeiter", "Bearbeiter", "Ihr Ansprechpartner", "Sachbearbeiterin", "Kontakt"],
    "ref": ["Ihr Zeichen", "Unser Zeichen", "Ihre Referenz", "Referenz", "Projekt"],
}

EXTRA_KEYS = ["delivery_note", "order2", "taxno", "clerk", "ref"]

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

TOTAL_EXTRA_LABELS = {
    "skonto": ["Skonto", "abzgl. Skonto", "Skontobetrag"],
    "paid": ["Bereits bezahlt", "Anzahlung", "Bereits beglichen", "Akonto"],
    "payuntil": ["Zahlbar bis", "Fällig am", "Zahlungsziel"],
}

TOTAL_LABELS = {
    "net": ["Nettobetrag", "Summe netto", "Zwischensumme", "Netto", "Gesamt netto", "Warenwert netto",
            "Nettosumme"],
    "gross": ["Bruttobetrag", "Gesamtbetrag", "Rechnungsbetrag", "Zu zahlen", "Endbetrag", "Gesamt brutto",
              "Bruttosumme", "Zahlbetrag"],
    "vat": ["MwSt", "USt", "Umsatzsteuer", "Mehrwertsteuer", "zzgl. MwSt"],
}

NUMERIC = {"menge", "preis", "rabatt", "mwst", "betrag"}
CENTERED = {"pos", "steuercode", "wg", "waehrung"}
PRICE_COLUMNS = {"preis", "betrag", "rabatt", "mwst", "basis", "waehrung", "steuercode"}

ACCENTS = ["#1f3864", "#7b1f1f", "#1f5c2e", "#3d3d3d", "#0f4c81", "#5a3d7a", "#8a5a1f", "#000000",
           "#14505c", "#6b2d5c", "#2f4f2f", "#883c1e", "#25406b", "#555555", "#7a6a1f", "#123f6d"]
INKS = ["#111", "#000", "#222", "#333", "#1a1a1a", "#3a3a3a", "#4a4a4a"]
TABLE_STYLES = ["grid", "rules", "zebra", "borderless", "rules", "zebra", "double", "headrule",
                "vrules", "dotted", "colshade"]
HEADER_STYLES = ["bold", "inverted", "underline", "plain", "boxed", "smallcaps", "accent",
                 "letterspaced"]
TITLE_STYLES = ["plain", "plain", "plain", "letter", "smallcaps", "underline", "boxed", "band", "none"]
TITLE_WEIGHTS = [16, 16, 16, 9, 8, 10, 8, 9, 8]
TOTALS_STYLES = ["block", "boxed", "table", "block", "table", "inline", "panel"]


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
                  "artikel", "mwst", "waehrung", "steuercode", "wg"}


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


def template(seed):
    rng = random.Random(seed)
    page_format = rng.choices(PAGE_MIX[0], weights=PAGE_MIX[1], k=1)[0]
    narrow_page = PAGE_FORMATS[page_format][0] < 180.0
    order = list(rng.choice(ORDERS))
    # Die drei Kollisionsspalten sind auf schmalen Blättern nachrangig — dort steht
    # der Platz der Bezeichnung zu. Ganz ausgeschlossen sind sie nicht, sonst lernt
    # das Modell "schmale Seite heißt keine Währungsspalte".
    rare = 0.35 if narrow_page else 1.0
    present = {"pos": rng.random() < 0.65, "artikel": rng.random() < 0.7, "gtin": rng.random() < 0.15,
               "name": True, "menge": True, "einheit": rng.random() < 0.6, "preis": True,
               "basis": rng.random() < 0.12, "rabatt": False, "mwst": rng.random() < 0.45,
               "betrag": True, "waehrung": rng.random() < 0.15 * rare,
               "steuercode": rng.random() < 0.13 * rare, "wg": rng.random() < 0.11 * rare}
    columns = [c for c in order if present.get(c)]
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
    font, tweak = rng.choice(FONTS)
    # Serifen im Fließsatz, Grotesk im Briefkopf (und umgekehrt) ist auf echten
    # Rechnungen die Regel, nicht die Ausnahme.
    head_font = font if rng.random() < 0.55 else rng.choice(FONTS)[0]
    narrow = rng.random() < 0.3
    logo = rng.choice(["none", "mark", "wordmark", "band", "mark", "wordmark", "wordmark"])
    # Ohne Absenderblock steht der Lieferantenname nur noch in der Wortmarke — und
    # wenn es auch die nicht gibt, muss ihn die Fußzeile tragen, sonst hat die
    # Seite keinen beschrifteten Lieferanten mehr.
    sender_place = rng.choice(["under", "under", "under", "beside", "above", "right", "none"])
    footer = rng.choice(["bank", "bank", "columns3", "columns2", "none", "line", "address"])
    footer_supplier = sender_place == "none" and logo != "wordmark"
    if footer_supplier:
        # Kein Absender und keine Wortmarke: dann steht der Lieferantenname nur
        # noch in der Fußzeile, und nur dort darf er dann beschriftet sein. Die
        # Spaltenfußzeilen drucken ihn gar nicht — also eine, die ihn druckt.
        footer = rng.choice(["line", "address"])

    meta_style = rng.choice(["pairs", "stack", "boxed", "stacked", "grid", "title", "dateline"]
                            + ([] if narrow_page else ["row"]))
    title_number = meta_style == "title" or (meta_style == "dateline" and rng.random() < 0.5)
    # "row" legt die Kopfdaten als eine Zeile nicht umbrechender Zellen an. Das
    # ist die breiteste Variante und passt auf A5 bei keiner Schriftgröße mehr;
    # `render.build` schrumpft dann bis an den Font-Boden und die Rechnungsnummer
    # wird trotzdem abgeschnitten.
    meta_table = meta_style if meta_style in ("pairs", "stack", "boxed", "row", "stacked", "grid") \
        else rng.choice(["pairs", "stack", "boxed"])
    places = ["under_title", "under_title", "top_right", "panel", "bottom", "split"]
    if meta_table in ("row", "grid"):
        places = ["under_title", "panel", "bottom"]
    meta_place = rng.choice(places)
    title_style = "plain" if title_number else rng.choices(TITLE_STYLES, TITLE_WEIGHTS, k=1)[0]
    if title_number:
        title_style = rng.choice(["plain", "plain", "underline", "letter", "smallcaps"])
    extras = [k for k in EXTRA_KEYS if rng.random() < 0.24]
    meta_keys = ["customer", "order", "delivery", "due"] + extras
    rng.shuffle(meta_keys)
    contacts = [k for k in ("tel", "fax", "mail", "web", "uid", "stnr") if rng.random() < 0.3][:4]
    decor_top_free = (not contacts and meta_place != "top_right" and sender_place != "right")
    return {
        "id": str(seed),
        "page_format": page_format,
        "columns": columns,
        "glue_unit": "einheit" not in columns,
        "headers": {c: rng.choice(LABELS[c]) for c in LABELS},
        "no_header_row": rng.random() < 0.12,
        "header_style": rng.choice(HEADER_STYLES),
        "align": {c: rng.choice(["right", "right", "right", "left", "center"]) if c in NUMERIC
                  else ("center" if c in CENTERED and rng.random() < 0.45 else "left")
                  for c in LABELS},
        "table_style": rng.choice(TABLE_STYLES),
        "font": font,
        "head_font": head_font,
        "size": round(rng.uniform(6.9, 11.4) + tweak, 2),
        "leading": round(rng.uniform(1.06, 1.72), 2),
        "cell_pad_x": round(rng.uniform(0.5, 4.0), 1),
        "cell_pad_y": round(rng.uniform(0.5, 5.2), 1),
        "narrow_name": narrow,
        "name_width": round(rng.uniform(26, 42) if narrow else rng.uniform(46, 74), 1),
        "logo": logo,
        "logo_side": rng.choice(["left", "left", "right", "center"]),
        "sender_place": sender_place,
        "wordmark_caps": rng.choice(["none", "none", "css", "literal"]),
        "wordmark_lines": rng.choice([1, 1, 2]),
        "show_tagline": rng.random() < 0.3,
        "show_owner": rng.random() < 0.25,
        "contacts": contacts,
        "contact_labels": {k: rng.choice(v) for k, v in CONTACT_LABELS.items()},
        "address_corner": rng.choice(["left", "left", "right", "center"]),
        "retline": rng.random() < 0.75,
        "addr_heading": rng.choice(ADDR_HEADINGS) if rng.random() < 0.28 else "",
        "meta_style": meta_style,
        "meta_table": meta_table,
        "meta_place": meta_place,
        "meta_side": rng.choice(["left", "right", "right"]),
        "meta_keys": meta_keys,
        "meta_extra_labels": {k: rng.choice(META_LABELS[k]) for k in EXTRA_KEYS},
        "meta_date_first": rng.random() < 0.25,
        "grid_cols": rng.randint(3, 3 if narrow_page else 5),
        "grid_box": rng.random() < 0.5,
        "dateline": meta_style == "dateline",
        "dateline_word": rng.choice(["", "", "am", "den"]),
        "title_number": title_number,
        "title_date": meta_style == "title",
        "title_date_key": rng.choice(["vom", "Datum", "vom", "am", "Datum:", "vom Datum"]),
        "title_sep": rng.choice(["", "", "·", "—", "|"]),
        "title_style": title_style,
        "title_align": rng.choice(["left", "left", "left", "center", "right"]),
        "totals_style": rng.choice(TOTALS_STYLES),
        "totals_side": rng.choice(["right", "right", "right", "full", "left"]),
        "total_extras": [k for k in ("skonto", "paid", "payuntil") if rng.random() < 0.18],
        "extra_labels": {k: rng.choice(v) for k, v in TOTAL_EXTRA_LABELS.items()},
        "gross_first": rng.random() < 0.18,
        "footer": footer,
        "footer_supplier": footer_supplier,
        "pageno_place": rng.choice(["tr", "tr", "bc", "bl"]),
        "pageno_form": rng.choice(["von", "von", "slash", "dash", "bare"]),
        "page_margin": (round(rng.uniform(9, 26), 1), round(rng.uniform(8, 24), 1)),
        "accent": rng.choice(ACCENTS),
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
        "meta_colon": rng.choices(["", "glued", "span"], [45, 35, 20], k=1)[0],
        "show_delivery": rng.random() < 0.55,
        "show_due": rng.random() < 0.45,
        "price_decimals": 2 if rng.random() < 0.8 else 3,
        "vat_percent_sign": rng.random() < 0.5,
        "total_net": rng.choice(TOTAL_LABELS["net"]),
        "total_gross": rng.choice(TOTAL_LABELS["gross"]),
        "total_vat": rng.choice(TOTAL_LABELS["vat"]),
        "decor_qr": (rng.choice(["bl", "tr"] if decor_top_free else ["bl"])
                     if rng.random() < 0.1 else ""),
        "decor_stamp": rng.random() < 0.08,
        "title": "",
        "title_key": "",
        "date_format": rng.choice(["%d.%m.%Y", "%d.%m.%Y", "%d. %B %Y", "%Y-%m-%d", "%d.%m.%y"]),
    }


def fit(spec, meta):
    columns = list(spec["columns"])
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
    out = dict(spec)
    out["columns"] = columns
    out["glue_unit"] = "einheit" not in columns
    return out
