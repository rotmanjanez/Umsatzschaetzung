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
]

TITLES = {
    "invoice": ["RECHNUNG", "Rechnung", "Rechnung Nr.", "AUSGANGSRECHNUNG", "Faktura", "R E C H N U N G",
                "Rechnung / Faktura"],
    "delivery_note": ["LIEFERSCHEIN", "Lieferschein", "Lieferschein Nr.", "L I E F E R S C H E I N",
                      "Warenbegleitschein"],
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
}

TOTAL_LABELS = {
    "net": ["Nettobetrag", "Summe netto", "Zwischensumme", "Netto", "Gesamt netto", "Warenwert netto",
            "Nettosumme"],
    "gross": ["Bruttobetrag", "Gesamtbetrag", "Rechnungsbetrag", "Zu zahlen", "Endbetrag", "Gesamt brutto",
              "Bruttosumme", "Zahlbetrag"],
    "vat": ["MwSt", "USt", "Umsatzsteuer", "Mehrwertsteuer", "zzgl. MwSt"],
}

NUMERIC = {"menge", "preis", "rabatt", "mwst", "betrag"}
PRICE_COLUMNS = {"preis", "betrag", "rabatt", "mwst", "basis"}


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
NARROW_COLUMNS = {"pos", "name", "menge", "einheit", "preis", "betrag", "rabatt"}


def template(seed):
    rng = random.Random(seed)
    page_format = rng.choices(PAGE_MIX[0], weights=PAGE_MIX[1], k=1)[0]
    order = list(rng.choice(ORDERS))
    present = {"pos": rng.random() < 0.65, "artikel": rng.random() < 0.7, "gtin": rng.random() < 0.15,
               "name": True, "menge": True, "einheit": rng.random() < 0.6, "preis": True,
               "basis": rng.random() < 0.12, "rabatt": False, "mwst": rng.random() < 0.45,
               "betrag": True}
    columns = [c for c in order if present.get(c)]
    # Ein schmales Blatt trägt keine zehn Spalten: auf A5 wird die Betragsspalte
    # so weit gequetscht, dass ihre Wörter aus der Seite laufen und beim Clippen
    # verloren gehen. Die Überlauf-Schleife in `render.build` fängt das nicht, sie
    # misst nur die Höhe. Also hier die optionalen Spalten streichen.
    narrow_page = PAGE_FORMATS[page_format][0] < 180.0
    if narrow_page:
        columns = [c for c in columns if c in NARROW_COLUMNS]
    font, tweak = rng.choice(FONTS)
    narrow = rng.random() < 0.3
    return {
        "id": str(seed),
        "page_format": page_format,
        "columns": columns,
        "glue_unit": not present["einheit"],
        "headers": {c: rng.choice(LABELS[c]) for c in LABELS},
        "no_header_row": rng.random() < 0.12,
        "header_style": rng.choice(["bold", "inverted", "underline", "plain", "boxed"]),
        "align": {c: rng.choice(["right", "right", "right", "left", "center"]) if c in NUMERIC
                  else ("center" if c == "pos" and rng.random() < 0.4 else "left") for c in LABELS},
        "table_style": rng.choice(["grid", "rules", "zebra", "borderless", "rules", "zebra"]),
        "font": font,
        "size": round(rng.uniform(7.4, 10.6) + tweak, 2),
        "leading": round(rng.uniform(1.15, 1.6), 2),
        "cell_pad_x": round(rng.uniform(0.8, 3.4), 1),
        "cell_pad_y": round(rng.uniform(0.8, 4.5), 1),
        "narrow_name": narrow,
        "name_width": round(rng.uniform(26, 42) if narrow else rng.uniform(46, 74), 1),
        "logo": rng.choice(["none", "mark", "wordmark", "band", "mark", "wordmark"]),
        "logo_side": rng.choice(["left", "right"]),
        "address_corner": rng.choice(["left", "right"]),
        # "row" legt die Kopfdaten als eine Zeile nicht umbrechender Zellen an. Das
        # ist die breiteste Variante und passt auf A5 bei keiner Schriftgröße mehr;
        # `render.build` schrumpft dann bis an den Font-Boden und die Rechnungsnummer
        # wird trotzdem abgeschnitten.
        "meta_style": rng.choice(["pairs", "stack", "boxed"] if narrow_page
                                 else ["pairs", "stack", "boxed", "row"]),
        "meta_side": rng.choice(["left", "right", "right"]),
        "totals_style": rng.choice(["block", "boxed", "table", "block", "table"]),
        "totals_side": rng.choice(["right", "right", "right", "full"]),
        "gross_first": rng.random() < 0.18,
        "footer": rng.choice(["bank", "bank", "columns3", "columns2", "none", "line"]),
        "page_margin": (round(rng.uniform(10, 24), 1), round(rng.uniform(9, 22), 1)),
        "accent": rng.choice(["#1f3864", "#7b1f1f", "#1f5c2e", "#3d3d3d", "#0f4c81", "#5a3d7a",
                              "#8a5a1f", "#000000"]),
        "rule_weight": round(rng.uniform(0.4, 1.4), 2),
        "carry_label": rng.choice(["Übertrag", "Übertrag Seite", "Zwischensumme", "Übertrag netto"]),
        "group_headings": rng.random() < 0.18,
        "second_row_details": rng.random() < 0.22,
        "uppercase_headers": rng.random() < 0.25,
        "meta_number": rng.choice(META_LABELS["number"]),
        "meta_date": rng.choice(META_LABELS["date"]),
        "meta_customer": rng.choice(META_LABELS["customer"]),
        "meta_order": rng.choice(META_LABELS["order"]),
        "meta_delivery": rng.choice(META_LABELS["delivery"]),
        "meta_due": rng.choice(META_LABELS["due"]),
        "meta_colon": rng.random() < 0.5,
        "show_delivery": rng.random() < 0.55,
        "show_due": rng.random() < 0.45,
        "price_decimals": 2 if rng.random() < 0.8 else 3,
        "vat_percent_sign": rng.random() < 0.5,
        "total_net": rng.choice(TOTAL_LABELS["net"]),
        "total_gross": rng.choice(TOTAL_LABELS["gross"]),
        "total_vat": rng.choice(TOTAL_LABELS["vat"]),
        "title": "",
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
