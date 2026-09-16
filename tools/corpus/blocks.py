import html as _html
import money

FIELD_OF = {"name": "name", "menge": "quantity", "einheit": "unit", "preis": "unitPrice",
            "betrag": "lineNet", "mwst": "vat", "artikel": "articleId"}


NOWRAP = {"pos", "menge", "einheit", "preis", "basis", "rabatt", "mwst", "betrag", "artikel", "gtin"}


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


def logo(spec, rng, name):
    if spec["logo"] == "none":
        return ""
    accent = spec["accent"]
    initials = "".join(w[0] for w in name.split()[:2] if w[0].isalpha()).upper() or "A"
    if spec["logo"] == "mark":
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
        return (f'<div class=wordmark style="color:{accent}">{words(name.split()[0])}'
                f'<span class=bar style="background:{accent}"></span></div>')
    return f'<div class=band style="background:{accent}"></div>'


def sender(spec, sup):
    return (f'<div class=sender>{words(sup["name"], "supplier")}<br>'
            f'{words(sup["street"])}<br>{words(sup["zip"] + " " + sup["city"])}</div>')


def address(sup, cus):
    return (f'<div class=addr><div class=retline>{words(sup["name"] + " · " + sup["street"] + " · " + sup["zip"] + " " + sup["city"])}</div>'
            f'<div class=to>{words(cus["name"])}<br>{words(cus["street"])}<br>'
            f'{words(cus["zip"] + " " + cus["city"])}</div></div>')


def meta_block(spec, invoice, meta):
    items = [(spec["meta_number"], invoice["number"], "invoiceNumber"),
             (spec["meta_date"], invoice["date_text"], "invoiceDate")]
    if meta["customer"]["number"]:
        items.append((spec["meta_customer"], meta["customer"]["number"], "O"))
    if meta["order"]:
        items.append((spec["meta_order"], meta["order"], "O"))
    if spec["show_delivery"]:
        items.append((spec["meta_delivery"], meta["delivery_text"], "O"))
    if spec["show_due"] and meta["kind"] == "invoice":
        items.append((spec["meta_due"], meta["due_text"], "O"))
    style = spec["meta_style"]
    if style == "row":
        cells = "".join(f'<td class=k>{words(k)}</td><td class=v>{words(v, f)}</td>' for k, v, f in items)
        return f'<table class="meta row"><tr>{cells}</tr></table>'
    body = "".join(f'<tr><td class=k>{words(k)}{words(":") if spec["meta_colon"] else ""}</td>'
                   f'<td class=v>{words(v, f)}</td></tr>' for k, v, f in items)
    return f'<table class="meta {style}">{body}</table>'


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


def item_cells(spec, line):
    no = line["no"]
    out = {}
    out["pos"] = [(no, "O", no)]
    out["artikel"] = [(line["sellerArticleId"] or "", "articleId", no)]
    out["gtin"] = [(line["gtin"] or "", "O", no)]
    out["name"] = [(line["name"], "name", no)]
    qty = [(money.qty(line["quantity"]), "quantity", no)]
    if spec["glue_unit"]:
        qty.append((line["unitText"], "unit", no))
    out["menge"] = qty
    out["einheit"] = [(line["unitText"], "unit", no)]
    out["preis"] = [(money.price(line["unitPrice"], spec["price_decimals"]), "unitPrice", no)]
    out["basis"] = [(line["priceBaseText"], "O", no)]
    out["rabatt"] = [(money.pct(line["discount"]) if line["discount"] else "", "O", no)]
    out["mwst"] = [(money.pct(line["vat"]) + (" %" if spec["vat_percent_sign"] else ""), "vat", no)]
    out["betrag"] = [(money.cents(line["lineNet"]), "lineNet", no)]
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


def totals_block(spec, invoice, meta):
    if meta["kind"] == "delivery_note":
        return ""
    rows = []
    net = (spec["total_net"], money.cents(invoice["netTotal"]) + " €", "netTotal")
    gross = (spec["total_gross"], money.cents(invoice["grossTotal"]) + " €", "grossTotal")
    # Nur der Satz selbst ist `vat`, nicht die Beschriftung, nicht das "%", nicht
    # der Nettobetrag, auf den er sich bezieht. `assemble` greift auf den
    # Summenblock zurück, sobald die Positionstabelle keine MwSt-Spalte hat — und
    # das ist die Mehrheit der Rechnungen. Bleibt der Satz hier unbeschriftet,
    # kann kein Modell diesen Weg lernen, und der Fehler fällt erst am
    # vat-Wert der fertigen Rechnung auf.
    vat_rows = [([(spec["total_vat"], "O", 0),
                  (money.pct(b["vat"]), "vat", 0),
                  ("% von " + money.cents(b["net"]), "O", 0)],
                 money.cents(b["tax"]) + " €", "O") for b in invoice["vatBreakdown"]]
    if spec["gross_first"]:
        rows = [gross] + vat_rows + [net]
    else:
        rows = [net] + vat_rows + [gross]
    body = "".join(f'<tr class="{"grand" if f == "grossTotal" else ""}">'
                   f'<td class=k>{cell(k) if isinstance(k, list) else words(k)}</td>'
                   f'<td class=v>{words(v, f)}</td></tr>'
                   for k, v, f in rows)
    return (f'<table class="totals {spec["totals_style"]} side-{spec["totals_side"]}" '
            f'data-role="total">{body}</table>')


def footer(spec, meta):
    sup = meta["supplier"]
    if spec["footer"] == "none":
        return ""
    if spec["footer"] == "line":
        return f'<div class=foot data-role="footer">{words(sup["name"] + " · " + sup["vatId"] + " · " + sup["mail"])}</div>'
    bank = f'{sup["bank"]}<br>IBAN {sup["iban"]}<br>BIC {sup["bic"]}'
    contact = f'{sup["street"]}<br>{sup["zip"]} {sup["city"]}<br>Tel. {sup["phone"]}'
    legal = f'{sup["register"]}<br>UID {sup["vatId"]}<br>{sup["mail"]}'
    blocks = {"bank": [bank], "columns2": [contact, bank], "columns3": [contact, bank, legal]}[spec["footer"]]
    cols = "".join(f'<div class=fcol>{"<br>".join(words(p) for p in b.split("<br>"))}</div>' for b in blocks)
    return f'<div class="foot cols" data-role="footer">{cols}</div>'
