import html as _html
import money

FIELD_OF = {"name": "name", "menge": "quantity", "einheit": "unit", "preis": "unitPrice",
            "betrag": "lineNet", "mwst": "vat", "artikel": "articleId"}


NOWRAP = {"pos", "menge", "einheit", "preis", "basis", "rabatt", "mwst", "betrag", "artikel",
          "gtin", "waehrung", "steuercode", "wg"}


def nowrap(column):
    return " nowrap" if column in NOWRAP else ""


def esc(text):
    return _html.escape(str(text))


def words(text, field="O", line=0):
    out = []
    for token in str(text).split():
        out.append(f'<span class=w data-f="{field}" data-l="{line}">{esc(token)}</span>')
    return " ".join(out)


def cell(tokens):
    return " ".join(words(t, f, l) for t, f, l in tokens if str(t).strip())


def key_text(spec, key):
    return key + ":" if spec["meta_colon"] == "glued" else key


def colon(spec, field="O"):
    """Ein eigens gesetzter Doppelpunkt gehört zur Beschriftung, nicht zum Wert.

    Als eigenes Token steht er aber nur in einem Teil der Vorlagen. Gedruckt sieht
    beides gleich aus — zwischen den beiden Spans steht kein Leerzeichen —, die OCR
    liest also so oder so ein Wort "Rechnungsnummer:". Ein Wahrheitstoken ":", das
    niemand je sieht, verdirbt dann nur die Deckenrechnung in `align.py`: bei einer
    langen Beschriftung liegt seine IoU mit dem OCR-Wort unter der Schwelle und es
    zählt als ungesehen. Deshalb klebt der Doppelpunkt häufiger an seinem Schlüssel,
    als dass er daneben steht.
    """
    return words(":", field) if spec["meta_colon"] == "span" else ""


# --------------------------------------------------------------------- Briefkopf

def logo(spec, rng, meta):
    """Bildmarke, Wortmarke oder Farbband.

    Die Wortmarke trägt seit v9 den VOLLEN Lieferantennamen und ist als `supplier`
    beschriftet. Vorher druckte sie nur das erste Wort und ließ es unbeschriftet —
    das brachte dem Modell bei, dass die größte Schrift am Seitenkopf gerade *nicht*
    der Lieferant ist, und genau daran scheiterte es auf echten Briefköpfen.
    """
    if spec["logo"] == "none":
        return ""
    name = meta["supplier"]["name"]
    accent = spec["accent"]
    if spec["logo"] == "mark":
        initials = "".join(w[0] for w in name.split()[:2] if w[0].isalpha()).upper() or "A"
        shape = rng.choice([
            f'<circle cx=26 cy=26 r=24 fill="{accent}"/>',
            f'<rect x=2 y=2 width=48 height=48 rx=8 fill="{accent}"/>',
            f'<polygon points="26,2 50,46 2,46" fill="{accent}"/>',
            f'<rect x=2 y=2 width=48 height=48 fill="none" stroke="{accent}" stroke-width=4/>',
        ])
        return (f'<svg width=52 height=52 viewBox="0 0 52 52">{shape}'
                f'<text x=26 y=34 text-anchor=middle font-size=22 font-weight=700 '
                f'fill="{"#fff" if "none" not in shape else accent}">{esc(initials)}</text></svg>')
    if spec["logo"] == "wordmark":
        # `text-transform` ändert den DOM-Text nicht, die Sonde liest also weiter
        # gemischte Schreibung. Für die OCR-Ausrichtung ist das egal (align.py
        # vergleicht casefold), deshalb gibt es daneben die echte Versalform:
        # nur so unterscheiden sich auch die Tokens.
        text = name.upper() if spec["wordmark_caps"] == "literal" else name
        parts = text.split()
        if spec["wordmark_lines"] == 2 and len(parts) > 2:
            cut = len(parts) - (2 if len(parts) > 3 else 1)
            rows = [" ".join(parts[:cut]), " ".join(parts[cut:])]
        else:
            rows = [text]
        body = "<br>".join(words(r, "supplier") for r in rows)
        caps = " caps" if spec["wordmark_caps"] == "css" else ""
        tag = f'<div class=tagline>{words(meta["tagline"])}</div>' if spec["show_tagline"] else ""
        return (f'<div class="wordmark{caps}" style="color:{accent}">{body}'
                f'<span class=bar style="background:{accent}"></span></div>{tag}')
    return f'<div class=band style="background:{accent}"></div>'


def sender(spec, meta):
    if spec["sender_place"] == "none":
        return ""
    sup = meta["supplier"]
    rows = [words(sup["name"], "supplier"), words(sup["street"]),
            words(sup["zip"] + " " + sup["city"])]
    if spec["show_owner"]:
        # "Inh. Maria Frühwirth" sieht aus wie ein Lieferantenname und ist keiner.
        rows.append(words(meta["owner_line"]))
    cls = "sender right" if spec["sender_place"] == "right" else "sender"
    return f'<div class="{cls}">' + "<br>".join(rows) + "</div>"


def contacts(spec, meta):
    """Tel/Fax/Mail/Web/UID/Steuernummer neben dem Absender: Beschriftung
    `otherLabel`, Wert `O`. Die Telefonnummer war bisher der häufigste falsche
    Treffer für die Rechnungsnummer, weil der Briefkopf sie nie gedruckt hat."""
    sup = meta["supplier"]
    value = {"tel": sup["phone"], "fax": sup["fax"], "mail": sup["mail"], "web": sup["web"],
             "uid": sup["vatId"], "stnr": sup["taxNumber"]}
    return "".join(f'<div>{words(spec["contact_labels"][k], "otherLabel")} {words(value[k])}</div>'
                   for k in spec["contacts"])


def letterhead(spec, rng, meta):
    mark = logo(spec, rng, meta)
    block = sender(spec, meta)
    if spec["sender_place"] == "beside":
        main = f'<div class=beside><div>{mark}</div><div>{block}</div></div>'
    elif spec["sender_place"] == "above":
        main = block + mark
    else:
        main = mark + block
    cls = {"left": "head", "right": "head rev", "center": "head mid"}[spec["logo_side"]]
    return f'<div class="{cls}"><div class=lh>{main}</div><div class=hmeta>{contacts(spec, meta)}</div></div>'


def address(spec, meta):
    sup, cus = meta["supplier"], meta["customer"]
    line = f'{sup["name"]} · {sup["street"]} · {sup["zip"]} {sup["city"]}'
    ret = f'<div class=retline>{words(line)}</div>' if spec["retline"] else ""
    head = (f'<div class=aheading>{words(spec["addr_heading"], "otherLabel")}</div>'
            if spec["addr_heading"] else "")
    return (f'<div class=addr>{ret}{head}<div class=to>{words(cus["name"])}<br>'
            f'{words(cus["street"])}<br>{words(cus["zip"] + " " + cus["city"])}</div></div>')


# ------------------------------------------------------------------ Kopfdaten

def dateline(spec, invoice, meta):
    """"Wien, 12.03.2025" über der Überschrift — Ort und Bindewort sind `O`, eine
    Datumsbeschriftung gibt es hier gerade nicht."""
    if not spec["dateline"]:
        return ""
    city = words(meta["supplier"]["city"] + ",")
    joiner = words(spec["dateline_word"]) + " " if spec["dateline_word"] else ""
    return f'<div class=dateline>{city} {joiner}{words(invoice["date_text"], "invoiceDate")}</div>'


def title_line(spec, invoice, meta, index):
    if spec["title_number"]:
        # "Rechnung Nr. 2025/0123 vom 12.03.2025": die Überschrift selbst ist hier
        # die Beschriftung der Nummer, es gibt dazu keine Meta-Zeile mehr.
        bits = [words(spec["title_key"], "numberLabel"), words(invoice["number"], "invoiceNumber")]
        if spec["title_date"]:
            if spec["title_sep"]:
                bits.append(words(spec["title_sep"]))
            bits.append(words(spec["title_date_key"], "dateLabel"))
            bits.append(words(invoice["date_text"], "invoiceDate"))
    elif spec["title_style"] == "none":
        return ""
    else:
        bits = [words(spec["title"])]
    if index:
        bits.append(words(f"Seite {index + 1}"))
    return f'<div class="title t-{spec["title_style"]} a-{spec["title_align"]}">{" ".join(bits)}</div>'


def meta_items(spec, invoice, meta):
    """(Beschriftung, Klasse der Beschriftung, Wert, Klasse des Werts) je Kopfzeile.

    Die Klasse der Beschriftung ist das Neue: ohne sie hat das Modell keinen Anker,
    der "Rechnungsnummer" an das Wort daneben bindet, und kann sie nicht von
    "Kundennummer" unterscheiden. Alles, was nicht unsere Beschriftung ist, ist
    `otherLabel` — und sein Wert bleibt `O`.
    """
    ours = []
    if not spec["title_number"]:
        ours.append((spec["meta_number"], "numberLabel", invoice["number"], "invoiceNumber"))
    if not spec["title_date"] and not spec["dateline"]:
        ours.append((spec["meta_date"], "dateLabel", invoice["date_text"], "invoiceDate"))
    if spec["meta_date_first"]:
        ours.reverse()
    known = {
        "customer": (spec["meta_customer"], meta["customer"]["number"]),
        "order": (spec["meta_order"], meta["order"]),
        "delivery": (spec["meta_delivery"], meta["delivery_text"] if spec["show_delivery"] else None),
        "due": (spec["meta_due"], meta["due_text"]
                if spec["show_due"] and meta["kind"] == "invoice" else None),
    }
    rest = []
    for key in spec["meta_keys"]:
        label, value = known.get(key) or (spec["meta_extra_labels"].get(key),
                                          meta["extras"].get(key))
        if not label or not value or label == spec["meta_number"]:
            continue
        rest.append((label, "otherLabel", value, "O"))
    return ours, rest


def meta_block(spec, invoice, meta, part="all"):
    ours, rest = meta_items(spec, invoice, meta)
    items = {"all": ours + rest, "ours": ours, "rest": rest}[part]
    if not items:
        return ""
    style = spec["meta_table"]
    if part != "all" and style in ("row", "grid"):
        style = "pairs"
    if style == "grid":
        # Eine Zeile Beschriftungen, eine Zeile Werte darunter. Auf echten
        # Rechnungen die häufigste Kopfform überhaupt.
        picked = items[:spec["grid_cols"]]
        keys = "".join(f'<td>{words(k, kf)}</td>' for k, kf, _, _ in picked)
        vals = "".join(f'<td>{words(v, vf)}</td>' for _, _, v, vf in picked)
        box = " boxed" if spec["grid_box"] else ""
        return (f'<table class="meta grid{box}"><tr class=gk>{keys}</tr>'
                f'<tr class=gv>{vals}</tr></table>')
    if style == "row":
        # Höchstens fünf Angaben: eine Zeile nicht umbrechender Paare ist die
        # breiteste Kopfform überhaupt, und mit allen Ablenkern darin schrumpft
        # `render.build` bis an den Schriftboden und schneidet sie trotzdem ab.
        cells = "".join(f'<td class=k>{words(key_text(spec, k), kf)}{colon(spec, kf)}</td>'
                        f'<td class=v>{words(v, vf)}</td>' for k, kf, v, vf in items[:5])
        return f'<table class="meta row"><tr>{cells}</tr></table>'
    if style == "stacked":
        # Beschriftung auf eigener Zeile, Wert darunter. Höher als breit, deshalb
        # nur die ersten vier Angaben.
        body = "".join(f'<tr><td class=k>{words(key_text(spec, k), kf)}{colon(spec, kf)}</td></tr>'
                       f'<tr><td class=v>{words(v, vf)}</td></tr>' for k, kf, v, vf in items[:4])
        return f'<table class="meta stacked">{body}</table>'
    body = "".join(f'<tr><td class=k>{words(key_text(spec, k), kf)}{colon(spec, kf)}</td>'
                   f'<td class=v>{words(v, vf)}</td></tr>' for k, kf, v, vf in items)
    return f'<table class="meta {style}">{body}</table>'


# --------------------------------------------------------------- Positionen

def item_header(spec):
    if spec["no_header_row"]:
        return ""
    cells = ""
    for c in spec["columns"]:
        label = spec["headers"][c]
        if spec["uppercase_headers"]:
            label = label.upper()
        cells += f'<th class="c-{c} a-{spec["align"][c]}{nowrap(c)}">{words(label)}</th>'
    return f'<thead data-role="column-header"><tr>{cells}</tr></thead>'


def pos_text(spec, no):
    return {"plain": str(no), "dot": f"{no}.", "pad": f"{no:03d}", "step": f"{no * 10:04d}"}[
        spec["pos_format"]]


def item_cells(spec, line):
    no = line["no"]
    out = {}
    out["pos"] = [(pos_text(spec, no), "O", no)]
    out["artikel"] = [(line["sellerArticleId"] or "", "articleId", no)]
    out["gtin"] = [(line["gtin"] or "", "O", no)]
    out["name"] = [(line["name"], "name", no)]
    qty = [(money.qty(line["quantity"]), "quantity", no)]
    if spec["glue_unit"]:
        qty.append((line["unitText"], "unit", no))
    out["menge"] = qty
    out["einheit"] = [(line["unitText"], "unit", no)]
    price = [(money.price(line["unitPrice"], spec["price_decimals"]), "unitPrice", no)]
    amount = [(money.cents(line["lineNet"]), "lineNet", no)]
    if spec["cell_currency"]:
        # Das Währungszeichen in der Zelle ist ein eigenes Token und bleibt `O`;
        # die Zahl behält ihre Klasse. Es war bisher nirgends im Korpus zu sehen
        # und hat in der Auswertung reihenweise Summen gekapert.
        side, sign = spec["cell_currency"]
        mark = (sign, "O", no)
        price = [mark] + price if side == "pre" else price + [mark]
        amount = [mark] + amount if side == "pre" else amount + [mark]
    out["preis"] = price
    out["betrag"] = amount
    out["waehrung"] = [(spec["waehrung_text"], "O", no)]
    out["steuercode"] = [(line["taxCode"], "O", no)]
    out["wg"] = [(line["wg"], "O", no)]
    out["basis"] = [(line["priceBaseText"], "O", no)]
    out["rabatt"] = [(money.pct(line["discount"]) if line["discount"] else "", "O", no)]
    out["mwst"] = [(money.pct(line["vat"]) + (" %" if spec["vat_percent_sign"] else ""), "vat", no)]
    return out


def item_rows(spec, lines):
    body = ""
    heading = None
    for line in lines:
        if spec["group_headings"] and line.get("group") and line["group"] != heading:
            heading = line["group"]
            body += (f'<tr class=group data-role="group"><td colspan={len(spec["columns"])}>'
                     f'{words(heading)}</td></tr>')
        cells = item_cells(spec, line)
        tds = "".join(f'<td class="c-{c} a-{spec["align"][c]}{nowrap(c)}">{cell(cells[c])}</td>'
                      for c in spec["columns"])
        body += f'<tr class=item data-role="line-item" data-l="{line["no"]}">{tds}</tr>'
        if spec["second_row_details"] and line.get("detail"):
            body += (f'<tr class=detail data-role="continuation" data-l="{line["no"]}">'
                     f'<td colspan={len(spec["columns"])}>{words(line["detail"], "O", line["no"])}</td></tr>')
    return body


# ------------------------------------------------------------------- Summen

def totals_extras(spec, invoice, meta):
    """Skonto, Anzahlung, Zahlungsziel im Summenblock: Beschriftung `otherLabel`,
    Betrag `O`. Genau diese Zeilen stehen auf echten Rechnungen unter dem
    Rechnungsbetrag und sehen ihm zum Verwechseln ähnlich."""
    rows = []
    for kind in spec["total_extras"]:
        if kind == "skonto":
            rows.append(([(spec["extra_labels"]["skonto"], "otherLabel", 0),
                          (f'{meta["skonto_pct"]} % bis {meta["skonto_text"]}', "O", 0)],
                         money.cents(meta["skonto"]) + " €", "O"))
        elif kind == "paid":
            rows.append(([(spec["extra_labels"]["paid"], "otherLabel", 0)],
                         money.cents(meta["paid"]) + " €", "O"))
        elif kind == "payuntil":
            rows.append(([(spec["extra_labels"]["payuntil"], "otherLabel", 0)],
                         meta["due_text"], "O"))
    return rows


def totals_block(spec, invoice, meta):
    if meta["kind"] == "delivery_note":
        return ""
    net = ([(spec["total_net"], "netLabel", 0)], money.cents(invoice["netTotal"]) + " €", "netTotal")
    gross = ([(spec["total_gross"], "grossLabel", 0)], money.cents(invoice["grossTotal"]) + " €",
             "grossTotal")
    inline = spec["totals_style"] == "inline" and len(invoice["vatBreakdown"]) <= 2
    # Nur der Satz selbst ist `vat`, nicht die Beschriftung, nicht das "%", nicht
    # der Nettobetrag, auf den er sich bezieht. `assemble` greift auf den
    # Summenblock zurück, sobald die Positionstabelle keine MwSt-Spalte hat — und
    # das ist die Mehrheit der Rechnungen. Bleibt der Satz hier unbeschriftet,
    # kann kein Modell diesen Weg lernen, und der Fehler fällt erst am
    # vat-Wert der fertigen Rechnung auf. Die Beschriftung ist seit v9 `vatLabel`,
    # der Steuerbetrag bleibt `O`.
    vat_rows = [([(spec["total_vat"], "vatLabel", 0),
                  (money.pct(b["vat"]), "vat", 0),
                  ("%" if inline else "% von " + money.cents(b["net"]), "O", 0)],
                 money.cents(b["tax"]) + " €", "O") for b in invoice["vatBreakdown"]]
    rows = [gross] + vat_rows + [net] if spec["gross_first"] else [net] + vat_rows + [gross]
    if inline:
        cells = "".join(f'<td>{cell(k)} {words(v, f)}</td>' for k, v, f in rows)
        return (f'<table class="totals inline side-{spec["totals_side"]}" '
                f'data-role="total"><tr>{cells}</tr></table>')
    rows += totals_extras(spec, invoice, meta)
    body = "".join(f'<tr class="{"grand" if f == "grossTotal" else ""}">'
                   f'<td class=k>{cell(k)}</td><td class=v>{words(v, f)}</td></tr>'
                   for k, v, f in rows)
    style = "block" if spec["totals_style"] == "inline" else spec["totals_style"]
    return (f'<table class="totals {style} side-{spec["totals_side"]}" '
            f'data-role="total">{body}</table>')


# ------------------------------------------------------------------- Fußzeile

def footer(spec, meta):
    sup = meta["supplier"]
    if spec["footer"] == "none":
        return ""
    # Steht der Name sonst nirgends beschriftet (kein Absender, keine Wortmarke),
    # dann trägt ihn die Fußzeile — sonst hätte die Seite keinen Lieferanten.
    name = words(sup["name"], "supplier" if spec["footer_supplier"] else "O")
    if spec["footer"] == "line":
        return (f'<div class=foot data-role="footer">{name} {words("·")} '
                f'{words("UID", "otherLabel")} {words(sup["vatId"])} {words("·")} '
                f'{words(sup["mail"])}</div>')
    if spec["footer"] == "address":
        return (f'<div class=foot data-role="footer">{name} {words("·")} '
                f'{words(sup["street"] + " · " + sup["zip"] + " " + sup["city"])} {words("·")} '
                f'{words("Tel.", "otherLabel")} {words(sup["phone"])}</div>')
    bank = [words(sup["bank"]), f'{words("IBAN", "otherLabel")} {words(sup["iban"])}',
            f'{words("BIC", "otherLabel")} {words(sup["bic"])}']
    contact = [words(sup["street"]), words(sup["zip"] + " " + sup["city"]),
               f'{words("Tel.", "otherLabel")} {words(sup["phone"])}']
    legal = [words(sup["register"]), f'{words("UID", "otherLabel")} {words(sup["vatId"])}',
             words(sup["mail"])]
    blocks_ = {"bank": [bank], "columns2": [contact, bank], "columns3": [contact, bank, legal]}[
        spec["footer"]]
    cols = "".join(f'<div class=fcol>{"<br>".join(b)}</div>' for b in blocks_)
    return f'<div class="foot cols" data-role="footer">{cols}</div>'


def pageno(spec, index, total):
    if total < 2:
        return ""
    text = {"von": f"Seite {index + 1} von {total}", "slash": f"Seite {index + 1}/{total}",
            "dash": f"- {index + 1} -", "bare": f"{index + 1} / {total}"}[spec["pageno_form"]]
    return f'<div class="pageno p-{spec["pageno_place"]}">{words(text)}</div>'


# Wortlose Flächen: ein QR-ähnlicher Block und ein schräg gesetzter Stempelrahmen.
# Sie tragen kein `span.w`, stehen also in keiner Wahrheit — die OCR sieht sie
# trotzdem und liefert Rauschwörter. Genau das tun echte Scans auch.
def decor(spec, rng):
    out = ""
    if spec["decor_qr"]:
        n = 13
        cells = "".join(f'<rect x={x} y={y} width=1 height=1/>'
                        for x in range(n) for y in range(n) if rng.random() < 0.45)
        eyes = "".join(f'<rect x={x} y={y} width=3 height=3 fill="none" stroke="#111" stroke-width=1/>'
                       for x, y in ((0, 0), (n - 3, 0), (0, n - 3)))
        out += (f'<div class="decor qr q-{spec["decor_qr"]}"><svg viewBox="0 0 {n} {n}" '
                f'width="100%" height="100%">{cells}{eyes}</svg></div>')
    if spec["decor_stamp"]:
        out += f'<div class="decor stampbox" style="border-color:{spec["accent"]}"></div>'
    return out
