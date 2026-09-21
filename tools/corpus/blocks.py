import datetime as dt
import html as _html
import zlib

import money
import vocab

FIELD_OF = {"name": "name", "menge": "quantity", "einheit": "unit", "preis": "unitPrice",
            "betrag": "lineNet", "mwst": "vat", "artikel": "articleId"}


NOWRAP = {"pos", "menge", "einheit", "preis", "basis", "rabatt", "mwst", "betrag", "artikel",
          "gtin", "waehrung", "steuercode", "wg", "groesse", "brutto"}

# Spalten, die in der zweizeiligen Positionsform in der ersten Zeile stehen: alles,
# was den Artikel benennt. Die Zahlen rutschen in die zweite Zeile.
NAMING = {"pos", "artikel", "gtin", "groesse", "name", "wg"}


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
    """Tokens einer Zelle. Ein viertes Feld `glue` setzt das Token **ohne Leerzeichen**
    an das vorige.

    Gedruckt verschmilzt es dann mit seinem Vorgänger ("Rum 0,7lH87", "19,00%") und die
    OCR liest ein Wort — in der Wahrheit stehen weiter zwei Tokens mit ihren eigenen
    Klassen. Genau so entstehen die Verschmelzungen auf echten Belegen, und `align.py`
    trägt die Klasse des überlappenden Wahrheitsworts auf das OCR-Wort.
    """
    out = ""
    for token in tokens:
        text, field, line = token[0], token[1], token[2]
        if not str(text).strip():
            continue
        piece = words(text, field, line)
        glue = len(token) > 3 and token[3]
        out = piece if not out else out + ("" if glue else " ") + piece
    return out


# Werte, die nicht über zwei Zeilen brechen dürfen. Eine Rechnungsnummer mit
# Leerzeichen ("RG 000417") in einer gesperrten Überschrift oder einer schmalen
# Kopfzelle landete sonst zur Hälfte in der nächsten Zeile — in der Wahrheit sind das
# zwei `invoiceNumber`-Läufe, und die Montage nimmt den ersten. `render.build` misst den
# Breitenüberlauf und verkleinert die Schrift, der Satz bleibt also darstellbar.
NOWRAP_FIELDS = {"invoiceNumber", "customerNumber", "orderNumber", "deliveryNoteNumber",
                 "invoiceDate", "orderDate", "deliveryDate", "dueDate", "taxId"}


def value_words(text, field, line=0):
    body = words(text, field, line)
    if field in NOWRAP_FIELDS and " " in str(text).strip():
        return f'<span style="white-space:nowrap">{body}</span>'
    return body


MONTHS = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August",
          "September", "Oktober", "November", "Dezember"]


def fmt_date(spec, iso):
    """Ein ISO-Datum in der Schreibweise der Vorlage.

    Dieselbe Abbildung wie `render.date_text`; die neuen Kopfdaten (Bestelldatum,
    Leistungszeitraum) entstehen erst hier im Block und können nicht vorher in `meta`
    formatiert werden, weil `render.document` sie nicht kennt.
    """
    d = dt.date.fromisoformat(iso)
    fmt = spec["date_format"]
    if fmt == "%d. %B %Y":
        return f"{d.day:02d}. {MONTHS[d.month - 1]} {d.year}"
    if fmt == "%-d. %B %Y":
        return f"{d.day}. {MONTHS[d.month - 1]} {d.year}"
    if fmt == "%-d.%-m.%y":
        return f"{d.day}.{d.month}.{d.year % 100:02d}"
    return d.strftime(fmt)


def alt(spec, key, fallback):
    """Der Schlüsseltext in der Sprache der Vorlage.

    ~6 % der Vorlagen drucken englische oder regionale Schlüssel (`Invoice No.`,
    `MWST`, `Fällig per`). Die **Klasse ändert sich dadurch nicht**: `Invoice No.` ist
    `numberLabel` wie `Rechnungsnummer`. Ohne diese Achse hängt das Modell an den
    deutschen Wörtern statt an der Stellung.
    """
    region = spec.get("key_region") or ""
    # `lang` bleibt unberührt: die fremdsprachigen Familien (families.apply_lang)
    # haben ihre Schlüssel schon in `spec["meta_*"]`/`spec["total_*"]` gesetzt, und
    # ein zweites Übersetzen darüber nähme ihnen die Varianz.
    pool = vocab.ALT_LABELS.get(region, {}).get(key) if region else None
    if not pool:
        return fallback
    # Kein `hash()`: der ist je Prozess gesalzen, und derselbe Seed muss denselben
    # Korpus erzeugen. `crc32` ist stabil.
    return pool[zlib.crc32(f"{spec['id']}|{key}".encode()) % len(pool)]


# ------------------------------------------ Zahlen in der Schreibweise der Vorlage

def cents(spec, value):
    return money.cents(value, spec["group_sep"], spec["minus_style"])


def cur(spec):
    """Das gedruckte Währungszeichen der Vorlage.

    `€` für EUR, sonst der Code selbst — die Familie `swiss` rechnet in CHF, und ein
    Schweizer Beleg mit Eurozeichen im Summenblock ist keiner. `validate.same()` und
    `tools/eval/parse.py` entfernen beide Formen vor dem Vergleich.
    """
    code = spec.get("currency", "EUR")
    # Für Euro das Zeichen, das die Vorlage ohnehin gezogen hat (`€` oder `EUR`) —
    # sonst den Code der Familie (CHF).
    return spec.get("waehrung_text", "€") if code == "EUR" else code


def amount(spec, value):
    """Betrag samt Währung, wie der Summenblock ihn druckt."""
    return cents(spec, value) + " " + cur(spec)


def money_sign(spec, fallback):
    """Ein gezogenes Währungszeichen (`€`, `EUR`) auf die Währung der Vorlage bringen.

    Die Achsen `cell_currency` und `waehrung_text` sind vor der Familie gezogen und
    kennen CHF nicht; ein Schweizer Beleg mit Eurozeichen in der Betragsspalte ist
    aber keiner.
    """
    return fallback if spec.get("currency", "EUR") == "EUR" else cur(spec)


def unit_price(spec, value):
    return money.price(value, spec["price_decimals"], spec["group_sep"], spec["minus_style"])


def quantity(spec, value):
    return money.qty(value, spec["qty_style"], spec["group_sep"])


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
    als dass er daneben steht. Die vierte Form (`column`) stellt ihn in eine eigene
    Spalte — dort steht er weit von beidem entfernt und wird als eigenes Wort
    gelesen; Formularsätze drucken ihn so.
    """
    return words(":", field) if spec["meta_colon"] == "span" else ""


# --------------------------------------------------------------------- Briefkopf

def swoosh(accent):
    """Ein Schwung unter der Wortmarke. Form, kein Text — die OCR liest ihn nicht."""
    return (f'<svg class=swoosh viewBox="0 0 120 22" preserveAspectRatio="none">'
            f'<path d="M0,20 C34,2 84,0 120,8 L120,22 L0,22 Z" fill="{accent}"/></svg>')


def tagline_place(spec):
    """Der gewählte Platz des Werbesatzes, oder "" wenn die Vorlage keinen druckt."""
    return spec.get("tagline_place", "") if spec.get("tagline_axis") else ""


# Schriftbild des Werbesatzes. Die Akzentfarbe steht als Inline-Stil daneben.
SLOGAN_CLASS = {"plain": "", "italic": " it", "accent": "", "accent_italic": " it",
                "smallcaps": " sc", "light": " lt"}


def slogan(spec, meta, extra=""):
    """`Fleisch und Wurst aus eigener Schlachtung` — der gemischt gesetzte Werbesatz
    des Briefkopfs.

    **Jedes Wort ist `O`.** Er steht in einem eigenen Span (und meist in einer eigenen
    Zeile), damit er in der Wahrheit nie in einem `supplier`-Lauf landet: der Name
    steht darüber oder mit 6 mm Abstand daneben. Genau diese Zeile hat v11 auf 26 von
    109 echten Scans als Teil des Lieferantennamens gelesen — der Korpus kannte den
    Werbesatz bis dahin nur als VERSALZEILE oben rechts.
    """
    style = spec.get("tagline_style", "plain")
    colour = (f' style="color:{spec["accent"]}"'
              if style in ("accent", "accent_italic") else "")
    cls = "slogan" + SLOGAN_CLASS.get(style, "") + extra
    return f'<span class="{cls}"{colour}>{words(meta["slogan"])}</span>'


def slogan_caps(spec, meta, extra=""):
    """Dieselbe Stelle, aber die VERSALFORM (`vocab.CLAIMS`) — bisher stand sie
    ausschliesslich oben rechts im `head_block`."""
    return (f'<span class="slogan cp{extra}" style="color:{spec["accent"]}">'
            f'{words(meta["claim"])}</span>')


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
            f'<rect x=2 y=2 width=48 height=48 fill="none" stroke="{accent}" '
            f'stroke-width="4"/>',
        ])
        return (f'<svg width=52 height=52 viewBox="0 0 52 52">{shape}'
                f'<text x=26 y=34 text-anchor=middle font-size=22 font-weight=700 '
                f'fill="{"#fff" if "none" not in shape else accent}">{esc(initials)}</text></svg>')
    if spec["logo"] == "wordmark2":
        # Zweiteilige Bildwortmarke, wie sie Fotogroßhändler und Webshops führen:
        # der erste Teil fett in der Akzentfarbe, der zweite in einer Schreib-
        # oder Displayschrift, darunter ein Schwung. Beide Teile sind der
        # Lieferantenname, beide sind `supplier`.
        parts = name.split()
        cut = 1 if len(parts) < 3 else 2
        head, tail = " ".join(parts[:cut]), " ".join(parts[cut:]) or parts[-1]
        return (f'<div class=wordmark2>'
                f'<span class=wm1 style="color:{accent}">{words(head.upper(), "supplier")}</span>'
                f'<span class=wm2 style="color:{spec["accent2"]}">{words(tail, "supplier")}</span>'
                f'{swoosh(spec["accent2"])}</div>'
                + (f'<div class=subline>{slogan(spec, meta)}</div>'
                   if tagline_place(spec) == "logo" else ""))
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
        sub = (f'<div class=subline>{slogan(spec, meta)}</div>'
               if tagline_place(spec) == "logo" else "")
        return (f'<div class="wordmark{caps}" style="color:{accent}">{body}'
                f'<span class=bar style="background:{accent}"></span></div>{tag}{sub}')
    return f'<div class=band style="background:{accent}"></div>'


def place(sup_or_cus):
    """"1100 Wien" — die PLZ ist `postcode`, der Ort bleibt `O`.

    Eine vier- oder fünfstellige Zahl links neben einem Ortsnamen ist auf jeder
    Rechnung dreimal zu sehen (Absender, Kunde, Fußzeile) und sah für das Modell bis
    v10 aus wie jede andere Nummer im Kopf.
    """
    return cell([(sup_or_cus["zip"], "postcode", 0), (sup_or_cus["city"], "O", 0)])


def supplier_intro(spec, meta, key=None):
    """`Firmenname: Brückner Spirituosen & Barbedarf GmbH` — der Lieferant als
    *beschrifteter Wert*, vier bis sechs Wörter, die über zwei Zeilen umbrechen.

    Die Form steht in E-Rechnungsausdrucken (XRechnung, ZUGFeRD-Visualisierung) und
    kam im Korpus bis v10 nicht vor: dort war der Lieferant entweder eine Absenderzeile
    oder eine Wortmarke. Der Schlüssel ist `otherLabel`, **jedes** Namenswort
    `supplier` — auch das `&`.
    """
    key = key or spec.get("sender_key") or "Lieferant:"
    return (f'<span class=skey>{words(key_text(spec, key), "otherLabel")}'
            f'{colon(spec, "otherLabel")}</span> '
            f'{words(meta["supplier"]["long_name"], "supplier")}')


def sender(spec, meta):
    """Absenderblock: Name, Strasse, Ort, optional Inhaberzeile — und seit v11
    optional der gemischt gesetzte Werbesatz (`tagline_place`).

    Die Zeilen sind mit `<br>` getrennt, jede ist also eine eigene gedruckte Zeile;
    nur `owner` setzt den Werbesatz mit 6 mm Abstand *neben* die Inhaberzeile. Der
    Name steht damit in jeder Form allein in seiner Zeile, und der Werbesatz kann in
    der Wahrheit nicht in seinen Lauf geraten.
    """
    if spec["sender_place"] == "none":
        return ""
    sup = meta["supplier"]
    where = tagline_place(spec)
    if spec.get("sender_keyed"):
        rows = [supplier_intro(spec, meta)]
    else:
        rows = [words(sup["name"], "supplier")]
    # "Inh. Maria Frühwirth" sieht aus wie ein Lieferantenname und ist keiner —
    # und auf dem Beleg, der v11 gekostet hat, steht der Werbesatz genau daneben.
    owner = words(meta["owner_line"]) if spec["show_owner"] else ""
    if where == "owner" and owner:
        rows.append(owner + slogan(spec, meta, extra=" beside"))
        owner = ""
    elif where == "sender":
        rows.append(slogan(spec, meta))
    elif where == "sender_caps":
        rows.append(slogan_caps(spec, meta))
    rows += [words(sup["street"]), place(sup)]
    if owner:
        rows.append(owner)
        if where == "sender_owner":
            rows.append(slogan(spec, meta))
    cls = "sender right" if spec["sender_place"] == "right" else "sender"
    return f'<div class="{cls}">' + "<br>".join(rows) + "</div>"


def contacts(spec, meta):
    """Tel/Fax/Mail/Web/UID/Steuernummer neben dem Absender: Beschriftung
    `otherLabel`, Wert `O`. Die Telefonnummer war bisher der häufigste falsche
    Treffer für die Rechnungsnummer, weil der Briefkopf sie nie gedruckt hat."""
    sup = meta["supplier"]
    value = {"tel": (sup["phone"], "phone"), "fax": (sup["fax"], "phone"),
             "mail": (sup["mail"], "O"), "web": (sup["web"], "O"),
             "uid": (sup["vatId"], "taxId"), "stnr": (sup["taxNumber"], "taxId")}
    return "".join(f'<div>{words(spec["contact_labels"][k], "otherLabel")} '
                   f'{words(value[k][0], value[k][1])}</div>' for k in spec["contacts"])


def head_block(spec, meta):
    """Der Kopfblock rechts oben als Werbesatz plus Anschrift — ohne Firmennamen.

    Auf dem Beleg, an dem diese Form gemessen wurde, steht der Lieferantenname
    ausschließlich in der Bildwortmarke; rechts oben prangt in Versalien und
    Akzentfarbe ein Werbesatz, darunter Straße, Ort, Telefon, Mail, Web. Genau
    dieser Werbesatz ist die auffälligste Zeile der Seite — und er ist `O`.
    """
    sup = meta["supplier"]
    rows = [f'<div class=claim style="color:{spec["accent"]}">{words(meta["claim"])}</div>',
            f'<div>{words(sup["street"])}</div>',
            f'<div>{place(sup)}</div>',
            f'<div>{words(spec["contact_labels"]["tel"], "otherLabel")} '
            f'{words(sup["phone"], "phone")}</div>',
            f'<div>{words(sup["mail"])}</div>',
            f'<div>{words(sup["web"])}</div>']
    return "".join(rows)


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
    side = head_block(spec, meta) if spec["head_contact"] == "block" else contacts(spec, meta)
    return f'<div class="{cls}"><div class=lh>{main}</div><div class=hmeta>{side}</div></div>'


def address(spec, meta):
    """Das Anschriftenfenster. Der Kundenname ist `buyer`, die PLZ `postcode`.

    `buyer` ist der exakte Gegenpart zu `supplier`, und genau daran hängt der Nutzen:
    bis v10 war der Kundenname `O`, also war „ein Firmenname im oberen Drittel" ein
    Merkmal ohne Gegenbeispiel — und der Kundenname stammt in 40 % der Fälle aus
    demselben Generator wie der Lieferantenname.

    Die Rücksendezeile darüber bleibt `O`, auch der Lieferantenname darin: sie ist
    5 pt hoch, mit `·` zerhackt und der Ablenker, den v9 dafür eingeführt hat. Nur
    ihre PLZ ist `postcode`.
    """
    sup, cus = meta["supplier"], meta["customer"]
    ret = (f'<div class=retline>{cell([(sup["name"] + " · " + sup["street"] + " · ", "O", 0), (sup["zip"], "postcode", 0), (sup["city"], "O", 0)])}</div>'
           if spec["retline"] else "")
    head = (f'<div class=aheading>{words(spec["addr_heading"], "otherLabel")}</div>'
            if spec["addr_heading"] else "")
    # Die Kundennummer im Anschriftenfenster: eine Nummer direkt über dem Namen,
    # an genau der Stelle, an der die Rechnungsnummer nie steht.
    number = (f'<div class=acust>{words(alt(spec, "customer", spec["meta_customer"]), "otherLabel")} '
              f'{words(cus["number"], "customerNumber")}</div>' if spec["addr_customer_no"] else "")
    return (f'<div class=addr>{ret}{head}{number}<div class=to>{words(cus["name"], "buyer")}<br>'
            f'{words(cus["street"])}<br>{place(cus)}</div></div>')


def delivery_line(spec, meta):
    """Die Lieferanschrift als Fließsatz auf einer Zeile — der Kundenname steht
    mitten im Satz und ist trotzdem kein Lieferant."""
    if not spec["delivery_sentence"]:
        return ""
    cus = meta["customer"]
    body = cell([(spec["delivery_sentence"], "O", 0), (cus["name"], "buyer", 0),
                 (cus["street"], "O", 0), (cus["zip"], "postcode", 0), (cus["city"], "O", 0)])
    return f'<div class=delivline>{body}</div>'


# ------------------------------------------------------------------ Kopfdaten

def dateline(spec, invoice, meta):
    """"Wien, 12.03.2025" über der Überschrift — Ort und Bindewort sind `O`, eine
    Datumsbeschriftung gibt es hier gerade nicht."""
    if not spec["dateline"]:
        return ""
    city = words(meta["supplier"]["city"] + ",")
    joiner = words(spec["dateline_word"]) + " " if spec["dateline_word"] else ""
    return f'<div class=dateline>{city} {joiner}{words(invoice["date_text"], "invoiceDate")}</div>'


def spaced(text, field):
    """"Rechnung Nr." -> "R E C H N U N G   N R." — gesperrt mit *echten*
    Leerzeichen, nicht per CSS. Die OCR liefert die Buchstaben dann einzeln, und
    genauso liegen sie in der Wahrheit: der ganze Lauf ist `numberLabel`.

    Die Wortgrenze kann nicht aus mehreren Leerzeichen kommen — HTML faltet sie zu
    einem, und "RECHNUNG NR." liest sich dann als ein Buchstabenband. Jedes Wort
    steht deshalb in einem eigenen `span` mit Abstand."""
    return "".join(f'<span class=sw>{words(" ".join(word), field)}</span>'
                   for word in text.upper().split())


def title_line(spec, invoice, meta, index):
    style = spec["title_style"]
    if spec["title_number"]:
        # "Rechnung Nr. 2025/0123 vom 12.03.2025": die Überschrift selbst ist hier
        # die Beschriftung der Nummer, es gibt dazu keine Meta-Zeile mehr.
        if spec["title_spaced_key"]:
            style = "spaced"
            head = spaced(spec["title_key"], "numberLabel")
        else:
            head = words(spec["title_key"], "numberLabel")
        bits = [head, value_words(invoice["number"], "invoiceNumber")]
        if spec["title_date"]:
            if spec["title_sep"]:
                bits.append(words(spec["title_sep"]))
            bits.append(words(spec["title_date_key"], "dateLabel"))
            bits.append(value_words(invoice["date_text"], "invoiceDate"))
    elif style == "none":
        return ""
    else:
        bits = [words(spec["title"])]
    if index:
        bits.append(words(f"Seite {index + 1}"))
    return f'<div class="title t-{style} a-{spec["title_align"]}">{" ".join(bits)}</div>'


def meta_items(spec, invoice, meta, page_text="1 / 1"):
    """(Beschriftung, Klasse der Beschriftung, Wert, Klasse des Werts) je Kopfzeile.

    Die Klasse der Beschriftung ist das Neue: ohne sie hat das Modell keinen Anker,
    der "Rechnungsnummer" an das Wort daneben bindet, und kann sie nicht von
    "Kundennummer" unterscheiden. Alles, was nicht unsere Beschriftung ist, ist
    `otherLabel` — und sein Wert bleibt `O`.
    """
    ours = []
    if not spec["title_number"]:
        ours.append((alt(spec, "number", spec["meta_number"]), "numberLabel",
                     invoice["number"], "invoiceNumber"))
    if not spec["title_date"] and not spec["dateline"]:
        ours.append((alt(spec, "date", spec["meta_date"]), "dateLabel",
                     invoice["date_text"], "invoiceDate"))
    if spec["meta_date_first"]:
        ours.reverse()
    v11 = spec.get("meta_v11_labels", {})
    # Der Wert eines fremden Schlüssels ist seit v11 **nicht** mehr `O`: er trägt seine
    # eigene feine Klasse. Die Beschriftung bleibt `otherLabel` — daran hängt weiter die
    # Unterscheidung von unseren Schlüsseln. Die Klasse hängt am *Schlüssel*, nicht an
    # der Form des Werts: dieselbe Ziffernfolge ist unter `Rechnungsnummer`
    # `invoiceNumber` und unter `Lieferschein-Nr.` `deliveryNoteNumber`.
    known = {
        "customer": (alt(spec, "customer", spec["meta_customer"]),
                     meta["customer"]["number"], "customerNumber"),
        "order": (alt(spec, "order", spec["meta_order"]), meta["order"], "orderNumber"),
        "delivery": (alt(spec, "delivery", spec["meta_delivery"]),
                     meta["delivery_text"] if spec["show_delivery"] else None, "deliveryDate"),
        "due": (alt(spec, "due", spec["meta_due"]),
                meta["due_text"] if spec["show_due"] and meta["kind"] != "delivery_note" else None,
                "dueDate"),
        # v11: Bestelldatum, Leistungszeitraum und eine erzwungene Lieferschein-Nummer.
        "orderdate": (alt(spec, "orderdate", v11.get("orderdate")),
                      fmt_date(spec, meta["order_date"]) if v11.get("orderdate") else None,
                      "orderDate"),
        "service": (alt(spec, "service", v11.get("service")),
                    (f'{fmt_date(spec, meta["service_from"])} - '
                     f'{fmt_date(spec, meta["service_to"])}') if v11.get("service") else None,
                    "deliveryDate"),
        "deliverynote": (alt(spec, "deliverynote", v11.get("deliverynote")),
                         meta["extras"]["delivery_note"] if v11.get("deliverynote") else None,
                         "deliveryNoteNumber"),
    }
    # Fremde Schlüssel, deren Wert weiter `O` bleibt: ein Sachbearbeiter ist ein Name
    # und keine Nummer, ein Zeichen ist ein Zeichen, ein Spediteur ein Wort.
    extra_field = {"delivery_note": "deliveryNoteNumber", "order2": "orderNumber",
                   "taxno": "taxId", "clerk": "O", "ref": "O", "shipping": "O", "pageref": "O"}
    rest = []
    for key in spec["meta_keys"]:
        if key == "pageref":
            rest.append((spec["meta_extra_labels"]["pageref"], "otherLabel", page_text, "O"))
            continue
        if key in known:
            label, value, field = known[key]
        else:
            label = spec["meta_extra_labels"].get(key)
            value, field = meta["extras"].get(key), extra_field.get(key, "O")
        if not label or not value or label == spec["meta_number"]:
            continue
        rest.append((label, "otherLabel", value, field))
    if spec["meta_empty_key"]:
        # Ein Formularfeld, das leer geblieben ist: die Beschriftung steht da, der
        # Wert fehlt, der Doppelpunkt dahinter hängt in der Luft.
        rest.append((spec["meta_empty_key"], "otherLabel", "", "O"))
    return ours, rest


def meta_rows(spec, items):
    """Die Paarzeilen — Doppelpunkt geklebt, daneben oder in eigener Spalte."""
    out = ""
    for k, kf, v, vf in items:
        if spec["meta_colon"] == "column":
            out += (f'<tr><td class=k>{words(k, kf)}</td><td class=sep>{words(":", kf)}</td>'
                    f'<td class=v>{value_words(v, vf)}</td></tr>')
        else:
            out += (f'<tr><td class=k>{words(key_text(spec, k), kf)}{colon(spec, kf)}</td>'
                    f'<td class=v>{value_words(v, vf)}</td></tr>')
    return out


def meta_block(spec, invoice, meta, part="all", page_text="1 / 1"):
    ours, rest = meta_items(spec, invoice, meta, page_text)
    items = {"all": ours + rest, "ours": ours, "rest": rest}[part]
    if not items:
        return ""
    style = spec["meta_table"]
    if part != "all" and style in ("row", "grid", "line"):
        style = "pairs"
    if style == "grid":
        # Eine Zeile Beschriftungen, eine Zeile Werte darunter. Auf echten
        # Rechnungen die häufigste Kopfform überhaupt.
        picked = items[:spec["grid_cols"]]
        keys = "".join(f'<td>{words(k, kf)}</td>' for k, kf, _, _ in picked)
        vals = "".join(f'<td>{value_words(v, vf)}</td>' for _, _, v, vf in picked)
        box = " boxed" if spec["grid_box"] else ""
        return (f'<table class="meta grid{box}"><tr class=gk>{keys}</tr>'
                f'<tr class=gv>{vals}</tr></table>')
    if style == "line":
        # Alles auf einer Zeile mit weiten Abständen: "Kunden-Nr. K31550133
        # Rechnung R185518416   14.07.2026   Seite 1/1". Das Datum steht hier oft
        # ohne Beschriftung mitten zwischen fremden Schlüsseln.
        bits = []
        for k, kf, v, vf in items[:4]:
            if vf == "invoiceDate" and spec["meta_bare_date"]:
                bits.append(f'<span class=mi>{value_words(v, vf)}</span>')
            else:
                bits.append(f'<span class=mi>{words(key_text(spec, k), kf)}{colon(spec, kf)} '
                            f'{value_words(v, vf)}</span>')
        if spec["meta_line_page"]:
            bits.append(f'<span class=mi>{words("Seite " + page_text)}</span>')
        return f'<div class=metaline>{"".join(bits)}</div>'
    if style == "row":
        # Höchstens fünf Angaben: eine Zeile nicht umbrechender Paare ist die
        # breiteste Kopfform überhaupt, und mit allen Ablenkern darin schrumpft
        # `render.build` bis an den Schriftboden und schneidet sie trotzdem ab.
        cells = "".join(f'<td class=k>{words(key_text(spec, k), kf)}{colon(spec, kf)}</td>'
                        f'<td class=v>{value_words(v, vf)}</td>' for k, kf, v, vf in items[:5])
        return f'<table class="meta row"><tr>{cells}</tr></table>'
    if style == "stacked":
        # Beschriftung auf eigener Zeile, Wert darunter. Höher als breit, deshalb
        # nur die ersten vier Angaben.
        body = "".join(f'<tr><td class=k>{words(key_text(spec, k), kf)}{colon(spec, kf)}</td></tr>'
                       f'<tr><td class=v>{value_words(v, vf)}</td></tr>' for k, kf, v, vf in items[:4])
        return f'<table class="meta stacked">{body}</table>'
    return f'<table class="meta {style}">{meta_rows(spec, items)}</table>'


# --------------------------------------------------------------- Positionen

def header_label(spec, column):
    if spec["header_two_line"] and column in spec["headers2"]:
        first, second = spec["headers2"][column]
        if spec["uppercase_headers"]:
            first, second = first.upper(), second.upper()
        return f'{words(first)}<br>{words(second)}'
    label = spec["headers"][column]
    if spec["uppercase_headers"]:
        label = label.upper()
    out = words(label)
    if spec["header_unit_hint"] and column in spec["header_hints"]:
        out += " " + words(spec["header_hints"][column])
    return out


def caption(spec):
    if not spec["caption"]:
        return ""
    return f'<div class=caption>{words(spec["caption"])}</div>'


def item_header(spec):
    if spec["no_header_row"]:
        return ""
    cells = ""
    for c in spec["columns"]:
        cells += (f'<th class="c-{c} a-{spec["align"][c]}{nowrap(c)}">'
                  f'{header_label(spec, c)}</th>')
    return f'<thead data-role="column-header"><tr>{cells}</tr></thead>'


def pos_text(spec, no):
    return {"plain": str(no), "dot": f"{no}.", "pad": f"{no:03d}", "step": f"{no * 10:04d}"}[
        spec["pos_format"]]


def name_cell(spec, line):
    """Die Bezeichnung — und was hinter ihr klebt.

    Zwei v11-Formen, beide von echten Belegen:

    * **GTIN im Namensfeld**: `Pommes frites 7 mm TK 4 x 2,5 kg · GTIN 4056489011231`.
      Die Ziffernfolge ist `gtin`, der Trenner und das Wort `GTIN`/`EAN` sind `O`, und
      **nichts davon gehört zum `name`** — in `expected.json` endet der Name vor dem
      Trenner. `validate.py` prüft das.
    * **Einheitencode am Namensende**, ohne Leerzeichen: `Rum 0,7lH87`. Gedruckt
      verschmolzen, in der Wahrheit ein eigenes Token der Klasse `unit`.
    """
    no = line["no"]
    tokens = [(line["name"], "name", no)]
    # Nur, wenn es keine eigene GTIN-Spalte gibt: zweimal dieselbe Nummer auf einer
    # Zeile ist keine Vorlage, die es gibt, und `validate.py` prüft die Zeile gegen
    # genau eine GTIN.
    if spec.get("name_gtin") and line["gtin"] and "gtin" not in spec["columns"]:
        tokens.append((spec.get("name_gtin_sep") or "·", "O", no))
        tokens.append((spec.get("name_gtin_key") or "GTIN", "O", no))
        tokens.append((line["gtin"], "gtin", no))
    if spec.get("name_unit_code") and line["unitText"]:
        tokens.append((line["unitCode"], "unit", no, True))
    return tokens


def vat_cell(spec, line):
    """Der Steuersatz in der Positionszeile.

    `19,00%` direkt rechts neben dem Preis hat das Modell auf einem echten Beleg auf
    *allen vier* Zeilen als `unitPrice` gelesen: eine zweistellige Zahl mit zwei
    Nachkommastellen in einer rechtsbündigen Spalte neben Preisen. Der Korpus druckte
    bis v10 nur `19` oder `19 %`. Jetzt auch `19,00`, `19,00 %` und das geklebte
    `19,00%` — die Zahl ist `vat`, das Prozentzeichen `O`.
    """
    no = line["no"]
    form = spec.get("vat_pct_form", "plain")
    text = money.pct(line["vat"])
    if form in ("d2", "d2sign", "d2glued"):
        text = money.de(line["vat"], money.BP // 100, 2)
    if form == "d2glued":
        return [(text, "vat", no), ("%", "O", no, True)]
    if form in ("sign", "d2sign") or spec["vat_percent_sign"]:
        return [(text, "vat", no), ("%", "O", no)]
    return [(text, "vat", no)]


def item_cells(spec, line):
    no = line["no"]
    free = line.get("free")
    out = {}
    out["pos"] = [(pos_text(spec, no), "O", no)]
    out["artikel"] = [(line["sellerArticleId"] or "", "articleId", no)]
    # Die EAN/GTIN-Spalte: die Ziffernfolge ist `gtin`. Bis v10 war sie `O` und damit
    # eine dreizehnstellige Zahl ohne Bedeutung neben der Artikelnummer.
    out["gtin"] = [(line["gtin"] or "", "gtin", no)]
    out["groesse"] = [(line.get("variant") or "", "O", no)]
    out["name"] = name_cell(spec, line)
    qty = [(quantity(spec, line["quantity"]), "quantity", no)]
    if spec["glue_unit"] and line["unitText"] and not spec.get("name_unit_code"):
        # `qty_unit_glue`: die Einheit klebt ohne Leerzeichen an der Zahl — "17Fl",
        # "10XBO", "5,450kg". Gedruckt ist das ein Wort, in der Wahrheit bleiben es
        # zwei Tokens mit ihren eigenen Klassen (`quantity` und `unit`). Genau so
        # steht es auf den echten Weingut- und Fischbelegen, und v11 kannte nur die
        # getrennte Form.
        qty.append((line["unitText"], "unit", no, bool(spec.get("qty_unit_glue"))))
    out["menge"] = qty
    out["einheit"] = [(line["unitText"] or "", "unit", no)]
    # Freizeilen drucken Preis und Betrag als *leere* Zelle, nicht als "0,00" —
    # in `expected.json` stehen sie mit 0. Eine gedruckte Null wäre eine andere
    # Rechnung.
    price = [] if free else [(unit_price(spec, line["unitPrice"]), "unitPrice", no)]
    amount = [] if free else [(cents(spec, line["lineNet"]), "lineNet", no)]
    if spec["cell_currency"] and not free:
        # Das Währungszeichen in der Zelle ist ein eigenes Token und bleibt `O`;
        # die Zahl behält ihre Klasse. Es war bisher nirgends im Korpus zu sehen
        # und hat in der Auswertung reihenweise Summen gekapert.
        side, sign = spec["cell_currency"]
        mark = (money_sign(spec, sign), "O", no)
        price = [mark] + price if side == "pre" else price + [mark]
        amount = [mark] + amount if side == "pre" else amount + [mark]
    # Die Preisbasis *im* Preisfeld: "12,50 / 100 g". Eine zweite Zahl in der
    # Preisspalte, die kein Preis ist.
    if spec.get("basis_inline") and price and line["priceBaseText"]:
        price = price + [("/", "O", no)] + [(t, f, no) + tok[2:]
                                            for tok in line["priceBaseText"]
                                            for t, f in [tok[:2]] if f != "O"]
    out["preis"] = price
    out["betrag"] = amount
    # Brutto je Position — nur sinnvoll neben der Nettospalte, und nur, wenn die Zeile
    # überhaupt einen Betrag druckt (Freizeilen und Lieferscheine drucken keinen).
    gross = money.round_div(line["lineNet"] * (money.BP + line["vat"]), money.BP)
    out["brutto"] = ([] if free or not line["lineNet"]
                     else [(cents(spec, gross), "lineGross", no)])
    out["waehrung"] = [] if free else [(money_sign(spec, spec["waehrung_text"]), "O", no)]
    out["steuercode"] = [(line["taxCode"], "O", no)]
    out["wg"] = [(line["wg"], "O", no)]
    # Preiseinheit: die Zahl ist `priceBasis`, das Einheitenwort `unit`. Auf einer
    # Zeile mit nackter Menge (unitText null) steht dort nur die Zahl — sonst hätte
    # die Wahrheit ein `unit`-Wort für eine Zeile, die keine Einheit druckt.
    # Die Tokens der Preisbasis sind (Text, Klasse) oder (Text, Klasse, _, glue) —
    # die geklebte Form "/kg" braucht das vierte Feld von `cell()`.
    out["basis"] = [(tok[0], tok[1], no) + tuple(tok[3:]) for tok in line["priceBaseText"]]
    out["rabatt"] = discount_cell(spec, line)
    out["mwst"] = vat_cell(spec, line)
    return out


def discount_cell(spec, line):
    """Die Rabattspalte: Satz oder Betrag, beides `lineDiscount`.

    Bis v10 war sie `O` — eine Zahl zwischen Preis und Betrag, die genauso aussieht wie
    beide und keine Klasse hatte."""
    no = line["no"]
    if not line["discount"]:
        return []
    if spec.get("discount_form") == "amount":
        return [(cents(spec, line["discountAmount"]), "lineDiscount", no)]
    if spec.get("discount_form") == "pctsign":
        return [(money.pct(line["discount"]), "lineDiscount", no), ("%", "O", no)]
    return [(money.pct(line["discount"]), "lineDiscount", no)]


def row_html(spec, cells, keep, indent=False):
    tds = ""
    for c in spec["columns"]:
        extra = " indent" if indent and c == "name" else ""
        body = cell(cells[c]) if c in keep else ""
        tds += f'<td class="c-{c} a-{spec["align"][c]}{nowrap(c)}{extra}">{body}</td>'
    return tds


def item_block(spec, line, index):
    """Eine Position als eigener `tbody` mit der Region `line-item`.

    Eine Position kann über mehrere gedruckte Zeilen gehen — umbrechender Name,
    Preisangaben in einer Zeile für sich. Die Region muss sie alle umfassen, und
    es darf *eine* Region je Position geben: `align.py` nummeriert die Positionen
    über die Reihenfolge der `line-item`-Regionen, zwei Regionen für eine Position
    brächten die Zusammensetzung aus dem Tritt. Ein `tbody` um alle Zeilen der
    Position leistet genau das; die Folgezeilen bekommen in `align.wrap_rows` von
    selbst die Rolle `line-wrap`.
    """
    cells = item_cells(spec, line)
    columns = set(spec["columns"])
    mode = spec["multi_row"]
    parts = line["name"].split()
    if mode == "desc2" and len(parts) >= 3:
        cut = max(1, round(len(parts) * 0.6))
        first, second = dict(cells), dict(cells)
        first["name"] = [(" ".join(parts[:cut]), "name", line["no"])]
        second["name"] = [(" ".join(parts[cut:]), "name", line["no"])]
        body = (f'<tr class=item>{row_html(spec, first, columns)}</tr>'
                f'<tr class=wrap>{row_html(spec, second, {"name"}, indent=True)}</tr>')
    elif mode == "priceline" and len(columns) >= 4 and columns - NAMING:
        body = (f'<tr class=item>{row_html(spec, cells, columns & NAMING)}</tr>'
                f'<tr class=wrap>{row_html(spec, cells, columns - NAMING)}</tr>')
    else:
        body = f'<tr class=item>{row_html(spec, cells, columns)}</tr>'
    zebra = " z1" if index % 2 else ""
    return (f'<tbody class="itembox{zebra}" data-role="line-item" '
            f'data-l="{line["no"]}">{body}</tbody>')


def item_rows(spec, lines, meta):
    body = ""
    heading = None
    span = len(spec["columns"])
    for index, line in enumerate(lines):
        if spec["info_rows"] and index == 0:
            # Eine Zeile mitten in der Tabelle, die keine Position ist: eine
            # Bestellnummer und ein Datum, beides `O`, beides mitten im Raster.
            body += (f'<tbody><tr class=info data-role="group"><td colspan={span}>'
                     f'{cell([(t, f, 0) for t, f in meta["info_text"]])}</td></tr></tbody>')
        if spec["group_headings"] and line.get("group") and line["group"] != heading:
            heading = line["group"]
            body += (f'<tbody><tr class=group data-role="group"><td colspan={span}>'
                     f'{words(heading)}</td></tr></tbody>')
        body += item_block(spec, line, index)
        if spec["second_row_details"] and line.get("detail"):
            bold = " strong" if spec["detail_bold"] else ""
            body += (f'<tbody><tr class="detail{bold}" data-role="continuation" '
                     f'data-l="{line["no"]}"><td colspan={span}>'
                     f'{cell([(t, f, line["no"]) for t, f in line["detail"]])}'
                     f'</td></tr></tbody>')
    return body


# ------------------------------------------------------------------- Summen

def pay_box(spec, meta):
    if not spec["pay_box"] or meta["kind"] == "delivery_note":
        return ""
    return f'<div class=paybox>{words(spec["pay_method"])}</div>'


def totals_extras(spec, invoice, meta):
    """Skonto, Anzahlung, Zahlungsziel im Summenblock: Beschriftung `otherLabel`,
    Betrag `O`. Genau diese Zeilen stehen auf echten Rechnungen unter dem
    Rechnungsbetrag und sehen ihm zum Verwechseln ähnlich."""
    rows = []
    kinds = list(spec["total_extras"])
    # `show_amount_due` erzwingt das Paar Anzahlung + Zahlbetrag: ohne eine
    # Vorauszahlungszeile gibt es keinen `amountDue`, und ohne genug `amountDue`
    # lernt das Modell den Unterschied zum Bruttobetrag nicht.
    if spec.get("show_amount_due") and "paid" not in kinds:
        kinds.append("paid")
    for kind in kinds:
        if kind == "skonto":
            # Der Skontobetrag mindert, was zu zahlen ist → `discount`. Satz und Datum
            # daneben sind kein Betrag und bleiben `O`.
            rows.append(([(spec["extra_labels"]["skonto"], "otherLabel", 0),
                          (f'{meta["skonto_pct"]} % bis {meta["skonto_text"]}', "O", 0)],
                         amount(spec, meta["skonto"]), "discount"))
        elif kind == "paid":
            rows.append(([(alt(spec, "paid", spec["extra_labels"]["paid"]), "otherLabel", 0)],
                         amount(spec, meta["paid"]), "discount"))
        elif kind == "payuntil":
            rows.append(([(spec["extra_labels"]["payuntil"], "otherLabel", 0)],
                         meta["due_text"], "dueDate"))
    if spec.get("show_amount_due") and meta["paid"] and meta["paid"] != invoice["grossTotal"]:
        # Zahlbetrag = Brutto − Anzahlung. Er steht unter dem Bruttobetrag, sieht aus wie
        # er und ist ein anderer Betrag; der Bruttobetrag bleibt `grossTotal`.
        rows.append(([(alt(spec, "duetotal", spec.get("due_amount_label") or "Zahlbetrag"),
                       "otherLabel", 0)],
                     amount(spec, invoice["grossTotal"] - meta["paid"]), "amountDue"))
    return rows


def charge_rows(spec, meta):
    """Versand, Fracht, Verpackung, Pfand, Rabatt, Rundung.

    Sie stehen in keiner Position und *verändern den Nettobetrag*. Beschriftung
    `otherLabel`, Betrag `O` — der Nettobetrag darunter ist der einzige `netTotal`
    der Seite, auch wenn die Positionssumme darüber anders lautet.
    """
    return [([(spec["charge_labels"][c["kind"]], "otherLabel", 0)],
             amount(spec, c["amount"]),
             # Vorzeichen der Wirkung entscheidet: was den Betrag erhöht oder ausgleicht,
             # ist `charge`; was ihn mindert (Rabatt, Nachlass), ist `discount`.
             "discount" if c["kind"] == "discount" else "charge")
            for c in meta["charges"]]


# Die v12-Formen, bei denen der Steuersatz im Beschriftungslauf steht (Fehlerbild 4).
VAT_GLUE_FORMS = ("gesamt", "gesamt_base", "zzgl", "enthalten")


def vat_rows(spec, invoice, bare=False):
    """Nur der Satz selbst ist `vat`, nicht die Beschriftung, nicht das "%", nicht
    der Nettobetrag, auf den er sich bezieht. `assemble` greift auf den
    Summenblock zurück, sobald die Positionstabelle keine MwSt-Spalte hat — und
    das ist die Mehrheit der Rechnungen."""
    out = []
    form = "bare" if bare else spec["vat_row_form"]
    glue = bool(spec.get("vat_rate_glued"))
    for b in invoice["vatBreakdown"]:
        rate = money.pct(b["vat"])
        if form in VAT_GLUE_FORMS:
            # Der Satz steckt **in** der Beschriftung, und die Zeile steht je Satz
            # einmal untereinander:
            #
            #     USt. gesamt 7%      37,03
            #     USt. gesamt 19%      2,83
            #
            # `7` ist `vat`, `%` und der Bemessungsbetrag sind `O`, alle
            # Beschriftungswörter `vatLabel` (CONVENTIONS.md §1). Bis v11 druckte der
            # Korpus nur `MwSt 19 %` mit *einem* Satz je Beleg; auf echten Belegen mit
            # zwei Sätzen hat das Modell den zweiten gar nicht getaggt.
            if form == "zzgl":
                key = [(spec.get("vat_zzgl_label") or "zzgl.", "vatLabel", 0),
                       (rate, "vat", 0), ("%", "O", 0, glue),
                       (alt(spec, "vat", spec["total_vat"]), "vatLabel", 0)]
            else:
                key = [(spec.get("vat_total_label") or "USt. gesamt", "vatLabel", 0),
                       (rate, "vat", 0), ("%", "O", 0, glue)]
                if form == "gesamt_base":
                    key += [(spec.get("vat_base_join") or "auf", "O", 0),
                            (cents(spec, b["net"]), "O", 0)]
            out.append((key, amount(spec, b["tax"]), "O"))
            continue
        key = [(alt(spec, "vat", spec["total_vat"]), "vatLabel", 0), (rate, "vat", 0)]
        if form == "of":
            key.append(("% von " + cents(spec, b["net"]), "O", 0))
        elif form == "base":
            key.append(("% auf " + amount(spec, b["net"]), "O", 0))
        else:
            key.append(("%", "O", 0))
        out.append((key, amount(spec, b["tax"]), "O"))
    return out


def totals_lines(spec, invoice, meta, bare_vat=False):
    """Alle Zeilen des Summenblocks in Druckreihenfolge."""
    rows = []
    # Ohne Zuschlagszeile *ist* die Zwischensumme der Nettobetrag. v10 hat sie
    # ausnahmslos zum Warenwert erklärt und damit auf echten, zuschlagsfreien Belegen
    # den `netTotal` verloren (1.000 -> 0.954, v10/REPORT.md Lücke 4). `net_alias`
    # zieht deshalb "Zwischensumme"/"Warenwert"/"Nettowarenwert" *in* den
    # Netto-Beschriftungstopf, aber nur wenn keine Zuschläge folgen.
    net_text = alt(spec, "net", spec["total_net"] if meta["charges"]
                   else (spec.get("net_alias") or spec["total_net"]))
    if meta["charges"]:
        # Warenwert / Zwischensumme **mit** folgenden Zuschlagszeilen: der Betrag ist
        # `subtotal`, die Beschriftung `otherLabel`. Der Nettobetrag steht darunter.
        #
        # Die beiden Beschriftungen dürfen nicht dieselben sein: `GOODS_LABELS` und
        # `TOTAL_LABELS["net"]` überschneiden sich ("Warenwert netto", "Zwischensumme"),
        # und zweimal dasselbe Wort zwei Zeilen übereinander, einmal `otherLabel` und
        # einmal `netLabel`, ist keine Rechnung, die es gibt — es ist Rauschen.
        goods = alt(spec, "subtotal", spec["goods_label"])
        if goods.casefold() == net_text.casefold():
            goods = next(g for g in ("Summe Positionen", "Warenwert", "Positionssumme",
                                     "Summe Artikel")
                         if g.casefold() != net_text.casefold())
        rows.append(([(goods, "otherLabel", 0)],
                     amount(spec, meta["goods_net"]), "subtotal"))
        rows += charge_rows(spec, meta)
    net = ([(net_text, "netLabel", 0)],
           amount(spec, invoice["netTotal"]), "netTotal")
    gross = ([(alt(spec, "gross", spec["total_gross"]), "grossLabel", 0)],
             amount(spec, invoice["grossTotal"]), "grossTotal")
    vats = vat_rows(spec, invoice, bare_vat)
    return rows + ([gross] + vats + [net] if spec["gross_first"] else [net] + vats + [gross])


def totals_sentences(spec, invoice, meta):
    """Die Summen als Sätze: "Rechnungsbetrag: 70,81 EUR"."""
    sep = "" if spec["meta_colon"] == "" else ":"
    out = [f'<div class=tsent>{words(spec["sentence_net"] + sep, "netLabel")} '
           f'{words(cents(spec, invoice["netTotal"]), "netTotal")} '
           f'{words(money_sign(spec, "EUR"))}</div>']
    for key, value, _ in vat_rows(spec, invoice, bare=True):
        out.append(f'<div class=tsent>{cell(key)} {words(value)}</div>')
    tail = (f' bis {meta["due_text"]}' if spec["show_due"] and meta["kind"] != "delivery_note"
            else "")
    out.append(f'<div class="tsent grand">'
               f'{words(spec["sentence_gross"] + tail + sep, "grossLabel")} '
               f'{words(cents(spec, invoice["grossTotal"]), "grossTotal")} '
               f'{words(money_sign(spec, spec["waehrung_text"]))}</div>')
    return "".join(out)


def thanks(spec):
    """Der Dankes- oder Hinweissatz **unter** dem Summenblock.

    Er sieht einer Freizeile zum Verwechseln ähnlich — Text ohne Zahlen in einer
    eigenen Zeile — und v10 hat daraus Positionen erfunden: `Vielen Dank für Ihren
    Auftrag!` unter der Bankverbindung bekam Rolle `line-item` und `name` mit 0,85–0,89
    Konfidenz (v10/REPORT.md, Lücke 3). Der Unterschied, den das Modell lernen muss,
    ist die **Rolle**: Freizeilen stehen im Positionsblock, dieser Satz steht im
    Fußbereich. Also trägt er `data-role="footer"` und ist `O`.
    """
    if not spec.get("thanks_note"):
        return ""
    return f'<div class=tnote data-role="footer">{words(spec["thanks_note"])}</div>'


def totals_block(spec, invoice, meta):
    if meta["kind"] == "delivery_note":
        return ""
    style = spec["totals_style"]
    shade = " shaded" if spec["totals_shade"] else ""
    if style == "sentence":
        return (f'<div class="totals sentence side-{spec["totals_side"]}{shade}" '
                f'data-role="total">{totals_sentences(spec, invoice, meta)}</div>'
                f'{thanks(spec)}')
    if style == "grid":
        # Eine Zeile Beschriftungen, darunter eine Zeile Beträge: Warenwert,
        # Versandkosten, steuerpflichtiger Betrag, Satz, Steuerbetrag, Summe.
        rows = totals_lines(spec, invoice, meta, bare_vat=True)
        keys = "".join(f'<td>{cell(k)}</td>' for k, _, _ in rows)
        vals = "".join(f'<td>{words(v, f)}</td>' for _, v, f in rows)
        return (f'<table class="totals grid side-{spec["totals_side"]}{shade}" data-role="total">'
                f'<tr class=gk>{keys}</tr><tr class=gv>{vals}</tr></table>{thanks(spec)}')
    inline = style == "inline" and len(invoice["vatBreakdown"]) <= 2 and not meta["charges"]
    rows = totals_lines(spec, invoice, meta, bare_vat=inline)
    if inline:
        cells = "".join(f'<td>{cell(k)} {words(v, f)}</td>' for k, v, f in rows)
        return (f'<table class="totals inline side-{spec["totals_side"]}{shade}" '
                f'data-role="total"><tr>{cells}</tr></table>{thanks(spec)}')
    rows += totals_extras(spec, invoice, meta)
    body = "".join(f'<tr class="{"grand" if f == "grossTotal" else ""}">'
                   f'<td class=k>{cell(k)}</td><td class=v>{words(v, f)}</td></tr>'
                   for k, v, f in rows)
    style = "block" if style == "inline" else style
    return (f'<table class="totals {style} side-{spec["totals_side"]}{shade}" '
            f'data-role="total">{body}</table>{thanks(spec)}')


# ------------------------------------------------------------------- Fußzeile

def footer_columns(spec, meta):
    sup = meta["supplier"]
    heads = spec["footer_heads_text"]
    blocks_ = {
        # Die Fußzeile ist die dichteste Stelle für die feinen Klassen: PLZ, Telefon,
        # IBAN, BIC, UID und Steuernummer stehen dort auf jeder zweiten Rechnung
        # untereinander — und waren bis v10 samt und sonders `O`.
        "contact": [words(sup["street"]), place(sup),
                    f'{words("Tel.", "otherLabel")} {words(sup["phone"], "phone")}'],
        "bank": [words(sup["bank"]),
                 f'{words("IBAN", "otherLabel")} {words(sup["iban"], "bankId")}',
                 f'{words("BIC", "otherLabel")} {words(sup["bic"], "bankId")}'],
        "legal": [words(sup["register"]),
                  f'{words("UID", "otherLabel")} {words(sup["vatId"], "taxId")}',
                  f'{words("St.-Nr.", "otherLabel")} {words(sup["taxNumber"], "taxId")}']
                 + ([f'{words("CHE", "otherLabel")} {words(sup["vatId2"], "taxId")}']
                    if sup.get("vatId2") else []),
        "hours": [words(spec["footer_hours"]), words(sup["mail"]), words(sup["web"])],
        "terms": [words(spec["footer_terms"])],
    }
    picked = {"bank": ["bank"], "columns2": ["contact", "bank"],
              "columns3": ["contact", "bank", "legal"],
              "columns4": ["contact", "bank", "legal", "hours"],
              "columns5": ["contact", "bank", "legal", "hours", "terms"]}[spec["footer"]]
    out = ""
    for key in picked:
        title = f'<div class=fhead>{words(heads[key])}</div>' if spec["footer_heads"] else ""
        out += f'<div class=fcol>{title}{"<br>".join(blocks_[key])}</div>'
    return out


def footer(spec, meta):
    sup = meta["supplier"]
    if spec["footer"] == "none":
        return ""
    # Steht der Name sonst nirgends beschriftet (kein Absender, keine Wortmarke),
    # dann trägt ihn die Fußzeile — sonst hätte die Seite keinen Lieferanten.
    name = words(sup["name"], "supplier" if spec["footer_supplier"] else "O")
    # Ein Druckcode wie "R1 31550133": Ziffern ganz unten, die aussehen wie eine
    # Belegnummer und keine sind.
    code = f' {words("·")} {words(meta["print_code"])}' if spec["footer_code"] else ""
    # Fließtext in der Fußzeile: `O`, aber mit der Rolle `footer` — derselbe Fall wie
    # `thanks()`. Ein Satz ohne Zahlen in einer eigenen Zeile ist sonst von einer
    # Freizeile im Positionsblock nicht zu unterscheiden.
    prose = (f'<div class=fprose>{words(spec["footer_prose"])}</div>'
             if spec.get("footer_prose") else "")
    if spec["footer"] == "line":
        return (f'<div class=foot data-role="footer" data-floor=1>{name} {words("·")} '
                f'{words("UID", "otherLabel")} {words(sup["vatId"], "taxId")} {words("·")} '
                f'{words(sup["mail"])}{code}{prose}</div>')
    if spec["footer"] == "address":
        return (f'<div class=foot data-role="footer" data-floor=1>{name} {words("·")} '
                f'{cell([(sup["street"] + " ·", "O", 0), (sup["zip"], "postcode", 0), (sup["city"], "O", 0)])} '
                f'{words("·")} '
                f'{words("Tel.", "otherLabel")} {words(sup["phone"], "phone")}{code}{prose}</div>')
    tail = f'<div class=fcode>{words(meta["print_code"])}</div>' if spec["footer_code"] else ""
    return (f'<div class="foot cols" data-role="footer" data-floor=1>'
            f'{footer_columns(spec, meta)}{tail}{prose}</div>')


def pageno(spec, index, total):
    if total < 2:
        return ""
    text = {"von": f"Seite {index + 1} von {total}", "slash": f"Seite {index + 1}/{total}",
            "dash": f"- {index + 1} -", "bare": f"{index + 1} / {total}"}[spec["pageno_form"]]
    return (f'<div class="pageno p-{spec["pageno_place"]}" data-floor=1>'
            f'{words(text)}</div>')


# ------------------------------------------------------------- Flächen und Deko
#
# Wortlose Flächen tragen kein `span.w`, stehen also in keiner Wahrheit; die OCR
# sieht sie trotzdem und liefert Rauschwörter. Genau das tun echte Scans auch.
# Der Barcode ist die Ausnahme: unter ihm steht seine Nummer *gedruckt*, und die
# ist Text — sie bleibt `O`, wie jede andere fremde Ziffernfolge auch.

def background(spec):
    """Große, blasse Form hinter dem Satz. Niemals Text: eine verblasste Wortmarke
    aus Buchstaben läse die OCR mit, und die Wahrheit könnte sie nicht sauber
    beschriften — also ausschließlich Formen."""
    if not spec["bg_art"]:
        return ""
    accent = spec["accent"]
    art = {
        "circle": f'<circle cx=50 cy=50 r=44 fill="{accent}"/>',
        "rings": (f'<circle cx=50 cy=50 r=44 fill="none" stroke="{accent}" stroke-width=7/>'
                  f'<circle cx=50 cy=50 r=30 fill="none" stroke="{accent}" stroke-width=7/>'
                  f'<circle cx=50 cy=50 r=16 fill="none" stroke="{accent}" stroke-width=7/>'),
        "triangle": f'<polygon points="50,4 96,92 4,92" fill="{accent}"/>',
        "stripes": "".join(f'<rect x={i * 14} y=-20 width=7 height=140 fill="{accent}" '
                           f'transform="rotate(-22 50 50)"/>' for i in range(9)),
        "blob": (f'<path d="M12,58 C6,24 38,4 62,10 C92,18 98,54 86,74 C72,96 26,94 12,58 Z" '
                 f'fill="{accent}"/>'),
        "grid": "".join(f'<rect x={i * 11} y=0 width=1.6 height=100 fill="{accent}"/>'
                        f'<rect x=0 y={i * 11} width=100 height=1.6 fill="{accent}"/>'
                        for i in range(10)),
    }[spec["bg_art"]]
    return (f'<div class="bgart bg-{spec["bg_place"]}" style="opacity:{spec["bg_opacity"]}">'
            f'<svg viewBox="0 0 100 100" width="100%" height="100%" '
            f'preserveAspectRatio="none">{art}</svg></div>')


def barcode(spec, meta, rng):
    if not spec["decor_barcode"]:
        return ""
    bars, x = "", 0
    while x < 150:
        width = rng.choice([1, 1, 2, 3])
        if rng.random() < 0.58:
            bars += f'<rect x="{x}" y="0" width="{width}" height="30"/>'
        x += width + rng.choice([1, 1, 2])
    return (f'<div class="barcode b-{spec["decor_barcode"]}" data-floor=1>'
            f'<svg viewBox="0 0 {x} 30" width="100%" height="16">{bars}</svg>'
            f'<div class=bcnum>{words(meta["barcode_text"])}</div></div>')


def decor(spec, rng, meta):
    out = background(spec)
    if spec["head_band"]:
        out += f'<div class=headband style="background:{spec["accent"]}"></div>'
    if spec["decor_qr"]:
        n = 13
        cells = "".join(f'<rect x="{x}" y="{y}" width="1" height="1"/>'
                        for x in range(n) for y in range(n) if rng.random() < 0.45)
        eyes = "".join(f'<rect x="{x}" y="{y}" width="3" height="3" fill="none" '
                       f'stroke="#111" stroke-width="1"/>'
                       for x, y in ((0, 0), (n - 3, 0), (0, n - 3)))
        out += (f'<div class="decor qr q-{spec["decor_qr"]}"><svg viewBox="0 0 {n} {n}" '
                f'width="100%" height="100%">{cells}{eyes}</svg></div>')
    if spec["decor_stamp"]:
        out += f'<div class="decor stampbox" style="border-color:{spec["accent"]}"></div>'
    return out + barcode(spec, meta, rng)


# --------------------------------------------------------------- Kassenbon
#
# 80 mm Thermorolle: keine Tabelle im üblichen Sinn, keine Linien, alles zentriert
# oder in zwei Spalten. "2 Stk x 1,50" auf einer Zeile, der Betrag daneben oder auf
# der nächsten. Für Gastronomen sind Abhol-Bons echte Eingangsbelege, und der
# Korpus kannte sie bisher überhaupt nicht.

def rule(spec):
    return {"dash": '<div class=rrule>- - - - - - - - - - - - - - - -</div>',
            "line": '<div class=rline></div>', "none": ""}[spec["rc_rule"]]


def receipt_head(spec, invoice, meta, index, total):
    sup = meta["supplier"]
    out = [f'<div class=rsup>{words(sup["name"], "supplier")}</div>']
    if tagline_place(spec) == "receipt":
        # Auch der Bon trägt den Werbesatz unter dem Namen — auf einem Thermobon
        # steht er in derselben Spalte wie der Name und ist trotzdem `O`.
        out.append(f'<div class=rslogan>{slogan(spec, meta)}</div>')
    out += [f'<div>{words(sup["street"])}</div>',
           f'<div>{place(sup)}</div>',
           f'<div>{words(spec["contact_labels"]["uid"], "otherLabel")} '
           f'{words(sup["vatId"], "taxId")}</div>',
           rule(spec)]
    if spec["title_style"] != "none":
        out.append(f'<div class="rtitle t-{spec["title_style"]}">{words(spec["title"])}</div>')
    out.append(f'<div>{words(spec["meta_number"], "numberLabel")} '
               f'{words(invoice["number"], "invoiceNumber")}</div>')
    out.append(f'<div>{words(spec["meta_date"], "dateLabel")} '
               f'{words(invoice["date_text"], "invoiceDate")} {words(meta["receipt_time"])}</div>')
    out.append(f'<div>{words(alt(spec, "customer", spec["meta_customer"]), "otherLabel")} '
               f'{words(meta["customer"]["number"], "customerNumber")}</div>')
    # Der Kunde auf dem Bon: oft eine Privatperson, und trotzdem der `buyer`. Ohne ihn
    # hätte das Kassenbon-Format die Klasse gar nicht.
    out.append(f'<div>{words(meta["customer"]["name"], "buyer")}</div>')
    out.append(f'<div>{words(spec["meta_extra_labels"]["clerk"], "otherLabel")} '
               f'{words(meta["extras"]["clerk"])}</div>')
    # Telefon und ein oder zwei Kopfangaben auch auf dem Bon. Ohne sie hat das
    # Bonformat `phone`, `orderNumber`, `deliveryDate`, `dueDate` und
    # `deliveryNoteNumber` überhaupt nicht — und das Modell lernt "auf einem Bon gibt
    # es das nicht", was auf einem echten Abhol-Bon prompt falsch ist. Die Auswahl
    # hängt an der Vorlagen-Id, damit sie ohne neue Achse reproduzierbar bleibt.
    out.append(f'<div>{words(spec["contact_labels"]["tel"], "otherLabel")} '
               f'{words(sup["phone"], "phone")}</div>')
    pool = [(spec["meta_order"], meta["order"], "orderNumber"),
            (spec["meta_delivery"], meta["delivery_text"], "deliveryDate"),
            (spec["meta_due"], meta["due_text"], "dueDate"),
            (spec.get("meta_v11_labels", {}).get("deliverynote") or "Lieferschein-Nr.",
             meta["extras"]["delivery_note"], "deliveryNoteNumber")]
    start = zlib.crc32(spec["id"].encode()) % len(pool)
    for key, value, field in [pool[start], pool[(start + 1) % len(pool)]]:
        if value:
            out.append(f'<div>{words(key, "otherLabel")} {words(value, field)}</div>')
    if total > 1:
        out.append(f'<div>{words(f"Blatt {index + 1} / {total}")}</div>')
    out.append(rule(spec))
    return f'<div class=rhead>{"".join(out)}</div>'


def receipt_items(spec, lines):
    """Die Positionen auf der Rolle.

    Der Bon hat keine Tabelle, muss sich aber trotzdem an die Spaltenliste halten, die
    `layout.fit` übrig gelassen hat: auf einem **Lieferschein** sind die Preisspalten
    gestrichen, und ein Bon, der dann „6,00 Btl x 0,00" und „0,00" druckt, widerspricht
    seiner eigenen Wahrheit (`unitPrice`/`lineNet` stehen dort mit 0 und dürfen gar
    nicht gedruckt werden). Die Preisbasis wird mitgedruckt, wo es sie gibt — auf einem
    Tankstellenbon ist das „1 l" hinter dem Literpreis.
    """
    columns = set(spec["columns"])
    with_price = "preis" in columns
    with_amount = "betrag" in columns
    body = ""
    for index, line in enumerate(lines):
        no = line["no"]
        head = ""
        if spec["rc_articleid"] and line["sellerArticleId"]:
            head += words(line["sellerArticleId"], "articleId", no) + " "
        head += cell(name_cell(spec, line))
        rows = f'<tr><td class=rn colspan=2>{head}</td></tr>'
        left = [(quantity(spec, line["quantity"]), "quantity", no)]
        if line["unitText"]:
            left.append((line["unitText"], "unit", no))
        if not line.get("free") and with_price:
            left.append(("x", "O", no))
            left.append((unit_price(spec, line["unitPrice"]), "unitPrice", no))
            if "basis" in columns and line["priceBaseText"]:
                left.append(("/", "O", no))
                left += [(tok[0], tok[1], no) for tok in line["priceBaseText"]
                         if tok[1] != "O"]
        amount = ("" if line.get("free") or not with_amount
                  else cell([(cents(spec, line["lineNet"]), "lineNet", no)]))
        tax = words(line["taxCode"], "O", no) if spec["rc_articleid"] else ""
        if spec["rc_amount_row"]:
            rows += (f'<tr><td class=rq colspan=2>{cell(left)} {tax}</td></tr>'
                     f'<tr><td class=rq></td><td class=ra>{amount}</td></tr>')
        else:
            rows += f'<tr><td class=rq>{cell(left)} {tax}</td><td class=ra>{amount}</td></tr>'
        body += (f'<tbody class=itembox data-role="line-item" data-l="{no}">{rows}</tbody>')
    return f'<table class="items receipt">{body}</table>'


def receipt_totals(spec, invoice, meta):
    if meta["kind"] == "delivery_note":
        return ""
    # Auch der Bon trägt Skonto-, Anzahlungs- und Restbetragszeilen: ein Catering- oder
    # Abhol-Bon mit Anzahlung ist genau der Fall, in dem der zu zahlende Betrag nicht
    # die Bruttosumme ist. Ohne sie hätte das Bonformat `discount` und `amountDue` nie.
    rows = totals_lines(spec, invoice, meta, bare_vat=True) + totals_extras(spec, invoice, meta)
    body = "".join(f'<tr class="{"grand" if f == "grossTotal" else ""}">'
                   f'<td class=k>{cell(k)}</td><td class=v>{words(v, f)}</td></tr>'
                   for k, v, f in rows)
    return (f'{rule(spec)}<table class="totals rtotals" data-role="total">{body}</table>'
            f'{rule(spec)}')


def receipt_foot(spec, meta):
    sup = meta["supplier"]
    # Der Bonfuß ist Fließtext und trägt deshalb, wie jede Fußzeile, die Rolle `footer`.
    out = [f'<div>{words(spec["pay_method"])}</div>',
           f'<div>{words(spec.get("thanks_note") or "Vielen Dank für Ihren Einkauf")}</div>']
    if spec["footer"] != "none":
        name = words(sup["name"], "supplier" if spec["footer_supplier"] else "O")
        out.append(f'<div data-role="footer">{name} {words("·")} {words(sup["web"])}</div>')
    if spec["footer_code"]:
        out.append(f'<div>{words(meta["print_code"])}</div>')
    # Bankeinzug-Bons drucken die IBAN mit — sonst hätte das Bonformat die Klasse
    # `bankId` überhaupt nicht, und das Modell lernte "auf einem Bon gibt es keine".
    if "Lastschrift" in spec["pay_method"] or "Bankeinzug" in spec["pay_method"]:
        out.append(f'<div>{words("IBAN", "otherLabel")} {words(sup["iban"], "bankId")}</div>')
    return f'<div class=rfoot data-role="footer">{"".join(out)}</div>'
