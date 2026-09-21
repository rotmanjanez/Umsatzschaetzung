import datetime as dt
import os
import random
import shutil

import blocks
import cdp
import families
import layout
import money

CHROME = os.environ.get("CORPUS_CHROME") or shutil.which("chromium") or shutil.which("chrome") \
    or "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
PAGE_W = 794          # A4 at 96 dpi, the default when a spec carries no format
PAGE_H = 1123
MM_PX = 96.0 / 25.4


def page_mm(spec):
    """Das Blattmaß in mm. Der Kassenbon bringt seine eigene Länge mit, weil eine
    Rolle abgeschnitten wird und nicht auf ein Format gedruckt."""
    w_mm, h_mm = layout.PAGE_FORMATS[spec.get("page_format", "a4")]
    return w_mm, spec.get("page_h_mm") or h_mm


def page_px(spec):
    """The spec's page box in CSS pixels. A4 reproduces the old constants exactly."""
    w_mm, h_mm = page_mm(spec)
    return round(w_mm * MM_PX), round(h_mm * MM_PX)


# Zeilen, die eine Fußzeilenform übereinander setzt.
FOOTER_LINES = {"line": 1, "address": 1, "bank": 3, "columns2": 3, "columns3": 3,
                "columns4": 3, "columns5": 3}


def floor_mm(spec):
    """Wie viel Platz am Blattfuß der Fließsatz freilassen muss.

    Fußzeile und Barcode stehen absolut am unteren Rand. Lief der Satz in sie
    hinein, überlagerten sich Summenblock und Fußzeile — auf dem Bild unlesbar,
    in der OCR ein Wortsalat genau dort, wo `netTotal` und `grossTotal` stehen.
    """
    my = spec["page_margin"][1]
    reserve = my
    if spec["footer"] != "none":
        lines = FOOTER_LINES[spec["footer"]] + (1 if spec["footer_heads"] else 0)
        body = spec["size"] * 0.3528 * spec["footer_size"] * spec["leading"] * lines
        reserve = max(reserve, max(5.0, my - 4.0) + body + 3.0)
    if spec["decor_barcode"] in ("br", "bl"):
        reserve = max(reserve, my + 21.0)
    extra = families.floor_extra(spec)
    if extra:
        reserve = max(reserve, my + extra)
    return round(reserve, 1)

MONTHS = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September",
          "Oktober", "November", "Dezember"]

PROBE = """(() => document.querySelectorAll('.page').length && [...document.querySelectorAll('.page')].map(p=>{
 const b=p.getBoundingClientRect();
 const limit=+p.dataset.bottom||b.height;
 const words=[],tables=[],tds=[];
 let overflow=0,hoverflow=0;
 for(const s of p.querySelectorAll('span.w')){
  const r=s.getBoundingClientRect();
  if(r.width<=0||r.height<=0) continue;
  const box=[r.x-b.x,r.y-b.y,r.width,r.height];
  // v13: Zellen- und Spaltenstruktur der Positionstabelle. `ci` ist der Spaltenindex
  // der umschließenden <td> (auch bei colspan-Unterzeilen), `tbl` die Nummer der
  // umschließenden Positionstabelle auf der Seite, `cid` eine laufende Nummer der <td>.
  const td=s.closest('td,th'), tb=td?td.closest('table.items'):null;
  let ci=-1,tbl=-1,cid=-1;
  if(td&&tb){ci=td.cellIndex;tbl=tables.indexOf(tb);if(tbl<0){tables.push(tb);tbl=tables.length-1;}
   cid=tds.indexOf(td);if(cid<0){tds.push(td);cid=tds.length-1;}}
  words.push({t:s.textContent,f:s.dataset.f,l:+s.dataset.l,box,ci,tbl,cid});
  // Fußzeile, Seitenzahl und Barcode stehen absolut am Blattfuß und sind kein
  // Überlauf. Der Fließsatz darf dafür nicht bis zu ihnen hinunterlaufen: er
  // wird gegen `data-bottom` gemessen, nicht gegen die Blatthöhe.
  if(s.closest('[data-floor]')) continue;
  overflow=Math.max(overflow,box[1]+box[3]-limit);
  hoverflow=Math.max(hoverflow,box[0]+box[2]-b.width,-box[0]);
 }
 const regions=[...p.querySelectorAll('[data-role]')].map(e=>{
  const r=e.getBoundingClientRect();
  return {role:e.dataset.role,l:+(e.dataset.l||0),box:[r.x-b.x,r.y-b.y,r.width,r.height]};
 });
 return {width:b.width,height:b.height,words,regions,overflow,hoverflow};
}))()"""


def tint(colour, alpha):
    """#1f3864 -> rgba(31,56,100,0.08). Für getönte Flächen hinter dem Satz."""
    h = colour.lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    r, g, b = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    return f"rgba({r},{g},{b},{alpha})"


def css(spec):
    extra_css = families.css(spec)
    mx, my = spec["page_margin"]
    pw, ph = page_px(spec)
    rule = f'{spec["rule_weight"]}px solid {spec["rule_ink"]}'
    light = f'{spec["rule_weight"]}px solid {spec["rule_light"]}'
    accent = spec["accent"]
    table_css = {
        "grid": f'.items td,.items th{{border:{light}}}',
        "rules": f'.items tr{{border-bottom:{light}}}.items thead tr{{border-bottom:{rule}}}',
        "zebra": '.items tbody.z1 tr{background:#eee}',
        "borderless": '',
        # Doppelte Linie unter dem Kopf, nur eine dicke Kopflinie, nur senkrechte
        # Striche, gepunktet, abwechselnd getönte Spalten: alles Tabellenbilder,
        # die auf echten Rechnungen vorkommen und die der Korpus bisher nicht kannte.
        "double": f'.items thead tr{{border-bottom:3px double {spec["rule_ink"]}}}'
                  f'.items tbody tr{{border-bottom:{light}}}',
        "headrule": f'.items thead tr{{border-bottom:{spec["rule_weight"] * 2.4}px solid {accent}}}',
        "vrules": f'.items td,.items th{{border-left:{light};border-right:{light}}}',
        "dotted": f'.items tr{{border-bottom:{spec["rule_weight"]}px dotted {spec["rule_light"]}}}',
        "colshade": '.items td:nth-child(even),.items th:nth-child(even){background:#f2f2f2}',
    }[spec["table_style"]]
    header_css = {
        "bold": '.items th{font-weight:700}',
        "inverted": f'.items th{{background:{accent};color:#fff;font-weight:700}}',
        "underline": f'.items th{{border-bottom:{rule};font-weight:700}}',
        "plain": '.items th{font-weight:400}',
        "boxed": f'.items th{{border:{rule};font-weight:700;background:#f0f0f0}}',
        "smallcaps": '.items th{font-variant:small-caps;font-weight:600}',
        "accent": f'.items th{{color:{accent};font-weight:700}}',
        "letterspaced": '.items th{letter-spacing:0.12em;font-weight:600;font-size:0.9em}',
        # Die Kopfzeile sieht aus wie eine Datenzeile: gleiche Schrift, gleicher
        # Grad, kein Fettsatz, keine Linie. Auf echten Rechnungen häufiger, als es
        # aussieht — und für den Tagger die schwerste Kopfzeile.
        "bodyrow": '.items th{font-weight:400;font-size:1em;border:0}',
    }[spec["header_style"]]
    title_css = {
        "plain": '',
        "letter": '.title{letter-spacing:0.28em}',
        # Gesperrt gesetzt steht schon im Text (echte Leerzeichen), also kein
        # zusätzlicher CSS-Sperrsatz — sonst reißt die Zeile die Seite auf.
        "spaced": '.title{letter-spacing:0.02em}',
        "smallcaps": '.title{font-variant:small-caps;letter-spacing:0.06em}',
        "underline": f'.title{{border-bottom:{spec["rule_weight"] * 2}px solid {accent};'
                     'padding-bottom:1.5mm}',
        "boxed": f'.title{{border:{rule};padding:1.8mm 3mm}}',
        "band": f'.title{{background:{accent};color:#fff;padding:1.8mm 3mm}}',
        "none": '',
    }[spec["title_style"] if not spec["title_spaced_key"] else "spaced"]
    band_mm = round(min(max(my * 0.75, 3.0), 12.0), 1)
    # Der ans Blattende geschobene Summenblock muss auf demselben Boden stehen wie
    # der übrige Satz. Stand er tiefer, meldete die Sonde einen Überlauf, den keine
    # Verkleinerung je beseitigt — die Schleife schrumpfte acht Durchgänge lang bis
    # auf 6,2 pt und kam trotzdem nicht darunter.
    tail_gap = round(max(2.0, floor_mm(spec) - my), 1)
    flex = '.page{display:flex;flex-direction:column}' if spec["totals_bottom"] else ''
    panel_tint = f'background:{tint(accent, 0.07)};' if spec["tint_panel"] else ''
    head_tint = (f'.items thead tr{{background:{tint(accent, 0.09)}}}'
                 if spec["tint_panel"] and spec["header_style"] != "inverted" else '')
    return f"""
@page{{size:{pw}px {ph}px;margin:0}}
*{{box-sizing:border-box}}
body{{margin:0;font-family:{spec["font"]};font-size:{spec["size"]}pt;line-height:{spec["leading"]};
 color:{spec["ink"]};font-weight:{spec["body_weight"]};letter-spacing:{spec["letter_tight"]}em}}
.page{{width:{pw}px;height:{ph}px;position:relative;overflow:hidden;padding:{my}mm {mx}mm;
 page-break-after:always;background:#fff;isolation:isolate}}
.page:last-child{{page-break-after:auto}}
{flex}
.head{{display:flex;justify-content:space-between;align-items:flex-start;gap:8mm;
 font-family:{spec["head_font"]}}}
.head.rev{{flex-direction:row-reverse}}
.head.mid{{flex-direction:column;align-items:center;text-align:center}}
.beside{{display:flex;align-items:flex-start;gap:5mm}}
.hmeta{{font-size:0.78em;line-height:1.45;text-align:right;white-space:nowrap}}
.head.rev .hmeta,.head.mid .hmeta{{text-align:left}}
.head.mid .hmeta{{text-align:center;margin-top:2mm}}
.claim{{font-weight:700;letter-spacing:0.06em;font-size:0.95em;margin-bottom:1mm;
 white-space:normal;max-width:74mm}}
.sender{{font-size:0.85em;line-height:1.35}}
.sender.right{{text-align:right}}
.tagline{{font-size:0.62em;letter-spacing:0.16em;color:#444;margin-top:0.6mm}}
/* v11, Werbesatz im Briefkopf: gemischte Schreibung, direkt am Namen — und `O`.
   `.beside` ist die Form, die v11 gekostet hat: derselbe Zeilenkasten wie die
   Inhaberzeile, 6 mm Abstand, damit die Wahrheit zwei getrennte Läufe sieht. */
.slogan{{font-size:0.94em}}
.slogan.it{{font-style:italic}}
.slogan.sc{{font-variant:small-caps;letter-spacing:0.05em}}
.slogan.lt{{font-weight:300;color:#444}}
.slogan.cp{{font-weight:700;letter-spacing:0.05em;font-size:0.9em}}
.slogan.beside{{margin-left:6mm;display:inline-block;max-width:62mm;vertical-align:top}}
.subline{{font-size:0.72em;color:#444;margin-top:0.8mm;max-width:78mm}}
.rslogan{{font-size:0.92em;line-height:1.25;margin-bottom:0.4mm}}
.addr{{margin-top:6mm}}
.addrwrap.right{{display:flex;justify-content:flex-end}}
.addrwrap.center{{display:flex;justify-content:center}}
.addrow{{display:flex;justify-content:space-between;align-items:flex-start;gap:6mm;margin-top:6mm}}
.addrow.right{{flex-direction:row-reverse}}
.addrow .addr{{margin-top:0}}
.retline{{font-size:0.62em;border-bottom:0.5px solid #999;padding-bottom:1mm;margin-bottom:2mm;color:#444}}
.aheading{{font-size:0.72em;color:#555;margin-bottom:0.8mm}}
.acust{{font-size:0.75em;color:#444;margin-bottom:0.8mm}}
.to{{line-height:1.4}}
.delivline{{margin-top:3mm;font-size:0.88em}}
.wordmark{{font-size:1.9em;font-weight:700;letter-spacing:0.03em;line-height:1.15}}
.wordmark.caps{{text-transform:uppercase}}
.wordmark2{{line-height:1.05}}
.wordmark2 .wm1{{font-size:2.2em;font-weight:800;letter-spacing:0.02em}}
.wordmark2 .wm2{{font-size:1.5em;font-family:{spec["mark_font"]};margin-left:1.5mm}}
.swoosh{{display:block;width:44mm;height:3.4mm;margin-top:0.6mm}}
.bar{{display:block;height:3px;margin-top:2px}}
.band{{height:3.5mm;width:34mm;opacity:0.85;margin-bottom:1.5mm}}
.dateline{{text-align:right;margin-top:5mm;font-size:0.95em}}
.title{{font-size:{spec["title_em"]}em;font-weight:700;margin:7mm 0 3mm;color:{accent};
 font-family:{spec["head_font"]}}}
table.meta{{border-collapse:collapse;font-size:0.9em}}
table.meta td{{padding:0.3mm 2.5mm 0.3mm 0;vertical-align:top}}
table.meta td.k{{color:#333}}
table.meta td.sep{{padding:0.3mm 2mm;color:#333}}
table.meta.boxed{{border:{light}}}
table.meta.boxed td{{border:{light};padding:0.8mm 2mm}}
table.meta.stack td.k{{font-size:0.82em;color:#555}}
table.meta.row td{{padding-right:4mm;white-space:nowrap}}
table.meta.stacked td.k{{font-size:0.8em;color:#555;padding-top:1.2mm}}
table.meta.grid td{{padding:0.7mm 3mm 0.7mm 0;white-space:nowrap}}
table.meta.grid tr.gk td{{font-size:0.82em;color:#555;font-weight:600}}
table.meta.grid.boxed td{{border:{light};padding:0.9mm 2.5mm}}
.metaline{{margin:3mm 0;font-size:0.92em}}
.metaline .mi{{margin-right:9mm;white-space:nowrap}}
.metawrap{{display:flex;justify-content:{'flex-start' if spec["meta_side"] == 'left' else 'flex-end'};margin:2mm 0}}
.panel{{border:{rule};{panel_tint}padding:1.5mm 3mm;margin:3mm 0}}
.panel table.meta{{width:100%}}
.caption{{margin-top:4mm;font-weight:700;font-size:1.02em}}
.items{{width:100%;border-collapse:collapse;margin-top:3mm;table-layout:auto}}
.items td.c-name,.items th.c-name{{width:{spec["name_pct"]}%}}
.items td.nowrap,.items th.nowrap{{white-space:nowrap}}
.items td,.items th{{padding:{spec["cell_pad_y"]}mm {spec["cell_pad_x"]}mm;vertical-align:top;
 overflow-wrap:break-word}}
.items th{{text-align:inherit}}
.a-right{{text-align:right}}
.a-left{{text-align:left}}
.a-center{{text-align:center}}
tr.wrap td{{padding-top:0;border-top:0}}
tr.wrap td.indent{{padding-left:{spec["cell_pad_x"] + 4}mm}}
tr.detail td{{font-size:0.82em;color:#444;padding-top:0;padding-left:{spec["cell_pad_x"] + 3}mm}}
tr.detail.strong td{{font-weight:700;color:{spec["ink"]}}}
tr.group td{{font-weight:700;background:#f4f4f4;padding-top:1.4mm}}
tr.info td{{font-size:0.88em;color:#333;padding-top:1.2mm}}
.paybox{{display:inline-block;border:{rule};padding:1.2mm 3mm;margin-top:4mm;font-size:0.92em}}
.carry{{text-align:right;font-weight:600;margin-top:2mm;font-size:0.95em}}
.totalswrap{{display:flex;justify-content:flex-end;margin-top:5mm}}
.totalswrap.full{{display:block}}
.totalswrap.left{{justify-content:flex-start}}
.totalswrap.duewrap{{margin-top:0}}
.tailblock{{margin-top:auto;margin-bottom:{tail_gap}mm}}
table.totals{{border-collapse:collapse;min-width:70mm}}
table.totals.full{{width:100%}}
table.totals td{{padding:0.7mm 3mm}}
table.totals td.v{{text-align:right;white-space:nowrap}}
table.totals.boxed{{border:{rule}}}
table.totals.boxed td{{border-bottom:{light}}}
table.totals.table td{{border-bottom:{light}}}
table.totals.panel{{border-top:{spec["rule_weight"] * 2.6}px solid {accent};background:#f6f6f6}}
table.totals.panel td{{padding:1mm 3.5mm}}
table.totals.inline{{min-width:0}}
table.totals.inline td{{white-space:nowrap;padding:0.7mm 4mm 0.7mm 0;font-weight:600}}
table.totals.grid{{min-width:0}}
table.totals.grid td{{border:{light};padding:0.9mm 2.5mm;white-space:nowrap;text-align:right}}
table.totals.grid tr.gk td{{font-size:0.84em;color:#444;font-weight:600}}
table.totals.shaded,div.totals.shaded{{background:#f1f1f1}}
table.totals tr.grand td{{font-weight:700;border-top:{rule};font-size:1.08em}}
div.totals.sentence{{min-width:0}}
.tsent{{margin:0.8mm 0}}
.tsent.grand{{font-weight:700;font-size:1.1em;margin-top:2mm}}
.pay{{margin-top:5mm;font-size:0.9em}}
.foot{{position:absolute;left:{mx}mm;right:{mx}mm;bottom:{max(5, my - 4)}mm;
 font-size:{spec["footer_size"]}em;color:#333;border-top:{light};padding-top:1.5mm}}
.foot.cols{{display:flex;gap:5mm;justify-content:space-between}}
.fcol{{flex:1}}
.fhead{{font-variant:small-caps;letter-spacing:0.1em;font-weight:700;color:{accent}}}
.fcode{{position:absolute;right:0;bottom:-3mm;font-size:0.9em;color:#555}}
.pageno{{position:absolute;font-size:0.75em;color:#555}}
.pageno.p-tr{{right:{mx}mm;top:{my - 4 if my > 8 else 4}mm}}
.pageno.p-bc{{left:0;right:0;text-align:center;bottom:{max(2, my - 9)}mm}}
.pageno.p-bl{{left:{mx}mm;bottom:{max(2, my - 9)}mm}}
.bgart{{position:absolute;z-index:-1;pointer-events:none}}
.bgart.bg-page{{left:8%;right:8%;top:14%;bottom:14%}}
.bgart.bg-table{{left:14%;right:14%;top:38%;height:40%}}
.headband{{position:absolute;left:0;right:0;top:0;height:{band_mm}mm;z-index:-1}}
.decor{{position:absolute;opacity:0.62}}
.qr{{width:20mm;height:20mm}}
.qr.q-bl{{left:{mx}mm;bottom:{my + 14}mm}}
.qr.q-tr{{right:{mx}mm;top:{my + 2}mm}}
.stampbox{{left:{mx + 18}mm;bottom:{my + 26}mm;width:34mm;height:15mm;border:2px solid;
 transform:rotate(-11deg);opacity:0.45;border-radius:2mm}}
.barcode{{position:absolute;width:38mm}}
.barcode.b-br{{right:{mx}mm;bottom:{my + 12}mm}}
.barcode.b-bl{{left:{mx}mm;bottom:{my + 12}mm}}
.barcode.b-tr{{right:{mx}mm;top:{my + 2}mm}}
.bcnum{{font-size:0.62em;text-align:center;letter-spacing:0.16em}}
.rhead{{text-align:center;line-height:1.35}}
.rsup{{font-size:1.45em;font-weight:700;line-height:1.15}}
.rtitle{{font-size:1.2em;font-weight:700;margin:1.5mm 0}}
.rrule{{letter-spacing:0.04em;margin:1.2mm 0;overflow:hidden;white-space:nowrap}}
.rline{{border-top:1px solid #444;margin:1.5mm 0}}
.items.receipt{{margin-top:1mm}}
.items.receipt td{{padding:0.2mm 0;vertical-align:top;border:0}}
td.rn{{font-weight:600}}
td.ra{{text-align:right;white-space:nowrap}}
table.totals.rtotals{{width:100%;min-width:0}}
table.totals.rtotals td{{padding:0.3mm 0}}
.rfoot{{text-align:center;margin-top:2.5mm;font-size:0.92em;line-height:1.35}}
{table_css}
/* Eine Position über mehrere Zeilen ist EINE Position: die Trennlinie gehört
   unter die letzte Zeile, nicht zwischen Name und Preiszeile. */
.items tbody.itembox tr:not(:last-child){{border-bottom:0}}
.items tbody.itembox tr:not(:last-child) td{{border-bottom:0}}
.title .sw{{margin-right:0.75em}}
/* Eine Rechnungsnummer in der Überschrift darf nicht *innerhalb* des Tokens
   umbrechen. "F-2026-0162" bricht sonst am Bindestrich, und die Wahrheitsbox
   dieses einen Wortes ist dann die Vereinigung über zwei Zeilen — sie deckt
   halb leere Fläche ab, überlappt die Nachbarwörter und ruiniert die
   Ausrichtung. Gemessen in der v11-Probe an der gesperrten Überschrift der
   Familien `form` und `nordform`. */
.title span.w{{white-space:nowrap}}
{header_css}
{title_css}
{head_tint}
{extra_css}
"""


def date_text(spec, iso):
    d = dt.date.fromisoformat(iso)
    fmt = spec["date_format"]
    if fmt == "%d. %B %Y":
        return f"{d.day:02d}. {MONTHS[d.month - 1]} {d.year}"
    if fmt == "%-d. %B %Y":
        return f"{d.day}. {MONTHS[d.month - 1]} {d.year}"
    if fmt == "%-d.%-m.%y":
        return f"{d.day}.{d.month}.{d.year % 100:02d}"
    return d.strftime(fmt)


def chunk(lines, budget):
    """Positionen auf Seiten verteilen.

    Zwei Budgets, nicht eines: jede Seite außer der letzten trägt nur die
    Übertragszeile, die letzte den ganzen Summenblock samt Zahlungssatz. Mit
    einem gemeinsamen Budget zahlt jede Seite den Summenblock mit, den sie nie
    druckt — auf einer zwölfseitigen Rechnung sind das elf verschenkte Blöcke.
    """
    first, last = budget if isinstance(budget, tuple) else (budget, budget)
    first, last = max(1, first), max(1, last)
    groups, rest = [], list(lines)
    while len(rest) > last:
        take = first
        if take >= len(rest):
            # Sonst nimmt die vorletzte Seite alles und die letzte bleibt leer —
            # eine Seite mit Spaltenkopf, Summenblock und keiner einzigen Position.
            take = max(1, len(rest) // 2)
        groups.append(rest[:take])
        rest = rest[take:]
    groups.append(rest)
    return groups


def rows_per_page(spec):
    """Der Notnagel, wenn `plan()` nicht messen kann (keine Position auf der Seite)."""
    line_mm = spec["size"] * 0.3528 * spec["leading"]
    w_mm, h_mm = page_mm(spec)
    my = spec["page_margin"][1]
    scale = min(1.0, h_mm / 297.0)
    if spec.get("receipt"):
        # Der Bon druckt je Position zwei bis drei Zeilen, dazu ein langer Kopf
        # und ein langer Fuß — beides in Textzeilen gerechnet.
        per_item = line_mm * (3.3 if spec["rc_amount_row"] else 2.3)
        usable = h_mm - 2 * my - line_mm * 26
        return max(2, int(usable / max(per_item, 2.0)))
    # Eine Position belegt zwei gedruckte Zeilen, sobald Name oder Preise auf eine
    # eigene Zeile rutschen. Der Zellenabstand kommt *einmal* dazu, nicht je Zeile —
    # der alte Ansatz multiplizierte ihn mit und schätzte die Zeile so hoch, dass
    # auf einer A4-Seite sieben Positionen standen und der Rest leer blieb.
    rows = 2.0 if spec["multi_row"] in ("desc2", "priceline") else 1.0
    row_mm = line_mm * rows + 2 * spec["cell_pad_y"]
    if spec["narrow_name"]:
        row_mm *= 1.4
    if spec["second_row_details"]:
        row_mm *= 1.22
    if spec["group_headings"]:
        row_mm *= 1.1
    head_mm = 118.0 * scale
    tail_mm = 34.0 * scale
    if spec["totals_bottom"]:
        tail_mm *= 1.9
    usable = h_mm - head_mm - tail_mm - floor_mm(spec)
    return max(2, int(usable / max(row_mm, 2.0)))


def receipt_page(spec, invoice, meta, rng, page_lines, index, total, carry):
    parts = [blocks.receipt_head(spec, invoice, meta, index, total),
             blocks.receipt_items(spec, page_lines)]
    if index + 1 < total:
        amount = ("" if meta["kind"] == "delivery_note" else
                  " " + blocks.words(blocks.cents(spec, carry) + " " + spec["waehrung_text"]))
        parts.append(f'<div class=carry data-role="carry">'
                     f'{blocks.words(spec["carry_label"])}{amount}</div>')
    else:
        parts.append(blocks.receipt_totals(spec, invoice, meta))
        # Auch auf dem Bon: die Anzahlungszeile mit dem Restbetrag (`amountDue`),
        # der Paragrafenhinweis und die Bewirtungsfelder. Ohne diese drei Zeilen
        # trug das Format `receipt` keine einzige dieser Klassen — und acht der
        # neuen Familien drucken auf einer Rolle.
        parts.append(families.prepaid_block(spec, invoice, meta))
        parts.append(families.notes(spec, invoice, meta))
        parts.append(families.host_fields(spec, invoice, meta))
        parts.append(blocks.receipt_foot(spec, meta))
    parts.append(blocks.decor(spec, rng, meta))
    return page_div(spec, parts)


def page_div(spec, parts):
    _, h_mm = page_mm(spec)
    bottom = round((h_mm - floor_mm(spec)) * MM_PX)
    return (f'<div class=page data-role="page" data-bottom="{bottom}">'
            f'{"".join(parts)}</div>')


def page_html(spec, invoice, meta, rng, page_lines, index, total, carry):
    if spec.get("receipt"):
        return receipt_page(spec, invoice, meta, rng, page_lines, index, total, carry)
    if spec["einvoice"]:
        # Der Ausdruck eines E-Rechnungs-Viewers baut die Seite vollständig selbst:
        # Käuferblock vor Verkäuferblock, jeder Wert hinter seinem Schlüssel,
        # Positionen als Blöcke statt als Tabellenzeilen.
        return page_div(spec, families.einvoice_page(spec, invoice, meta, rng, page_lines,
                                                     index, total, carry))
    page_text = f"{index + 1} / {total}"
    place = spec["meta_place"]
    parts = [blocks.letterhead(spec, rng, meta)]
    addr = blocks.address(spec, meta)
    if place in ("top_right", "split"):
        # Kopfdaten neben der Anschrift, nicht unter der Überschrift — auf echten
        # Rechnungen die Normalform. "split" zieht Nummer und Datum hierher und
        # lässt den Rest unten stehen.
        side = blocks.meta_block(spec, invoice, meta, "ours" if place == "split" else "all",
                                 page_text)
        parts.append(f'<div class="addrow {spec["address_corner"]}">'
                     f'<div class=acol>{addr}</div><div class=mcol>{side}</div></div>')
    else:
        parts.append(f'<div class="addrwrap {spec["address_corner"]}">{addr}</div>')
    parts.append(blocks.delivery_line(spec, meta))
    parts.append(blocks.dateline(spec, invoice, meta))
    parts.append(blocks.title_line(spec, invoice, meta, index))
    if place in ("under_title", "split", "bottom"):
        table = blocks.meta_block(spec, invoice, meta,
                                  "rest" if place == "split" else "all", page_text)
        if table:
            parts.append(table if spec["meta_table"] == "line"
                         else f'<div class=metawrap>{table}</div>')
    elif place == "panel":
        table = blocks.meta_block(spec, invoice, meta, "all", page_text)
        if table:
            parts.append(f'<div class=panel>{table}</div>')
    parts.append(families.period_block(spec, invoice, meta))
    parts.append(blocks.caption(spec))
    if spec["item_form"] == "twocol":
        parts.append(families.twocol_items(spec, page_lines, meta))
    elif spec["item_form"] == "kv":
        parts.append(families.kv_items(spec, page_lines, meta))
    else:
        header = blocks.item_header(spec)
        body = blocks.item_rows(spec, page_lines, meta)
        parts.append(f'<table class=items>{header}{body}</table>')
    if index + 1 < total:
        # Ein Lieferschein trägt keine Beträge — auch nicht im Übertrag. "Übertrag
        # 0,00 €" stand dort bis v10 und ist auf keinem echten Lieferschein zu finden.
        amount = ("" if meta["kind"] == "delivery_note" else
                  " " + blocks.words(blocks.cents(spec, carry) + " " + spec["waehrung_text"]))
        parts.append(f'<div class=carry data-role="carry">'
                     f'{blocks.words(spec["carry_label"])}{amount}</div>')
    else:
        tail = [blocks.pay_box(spec, meta)]
        totals = blocks.totals_block(spec, invoice, meta)
        if totals:
            # Vorauszahlungen und der Restbetrag stehen direkt unter dem
            # Summenblock: dort steht der `amountDue`, der nicht die Bruttosumme ist.
            due = families.prepaid_block(spec, invoice, meta)
            tail.append(f'<div class="totalswrap {spec["totals_side"]}">{totals}</div>')
            if due:
                # Eigener Wrapper: `.totalswrap` ist eine Flex-Zeile, ein zweiter
                # Block darin stellte sich *neben* den Summenblock statt darunter.
                tail.append(f'<div class="totalswrap duewrap {spec["totals_side"]}">'
                            f'{due}</div>')
        if meta["kind"] != "delivery_note":
            tail.append(f'<div class=pay data-role="footer">{blocks.words(meta["payment"])}</div>')
        # Der Summenblock ganz unten auf der Seite, weit unter einer kurzen
        # Tabelle: `margin-top:auto` in der Flex-Spalte schiebt ihn dorthin, ohne
        # dass er je die Tabelle oder die Fußzeile überlaufen könnte.
        parts.append(f'<div class=tailblock>{"".join(tail)}</div>' if spec["totals_bottom"]
                     else "".join(tail))
        parts.append(families.notes(spec, invoice, meta))
        parts.append(families.host_fields(spec, invoice, meta))
    parts.append(blocks.pageno(spec, index, total))
    parts.append(blocks.footer(spec, meta))
    parts.append(families.qr_bill(spec, invoice, meta))
    parts.append(families.sidebar(spec, meta))
    parts.append(blocks.decor(spec, rng, meta))
    return page_div(spec, parts)


def name_pct(spec, meta):
    longest = max((len(l["name"]) for l in meta["render_lines"]), default=20)
    span = min(52.0, 2.2 + longest * 1.05)
    return round(span * (0.45 if spec["narrow_name"] else 1.0), 1)


def document(spec, invoice, meta, seed, per_page=None):
    rng = random.Random(seed)
    spec = dict(spec)
    spec["title"] = rng.choice(layout.TITLES[meta["kind"]])
    spec["title_key"] = rng.choice(layout.TITLE_KEYS[meta["kind"]])
    if spec["lang"] != "de":
        spec["title"], spec["title_key"] = families.lang_title(spec, meta["kind"])
    if spec["title_text"] and meta["kind"] == "invoice":
        # Proforma, Abschlagsrechnung, Bewirtungsbeleg: die Belegart steht in der
        # Überschrift und nirgends sonst. Auf eine Gutschrift oder einen
        # Lieferschein darf sie nicht, die haben ihre eigene.
        spec["title"] = spec["title_text"]
        spec["title_key"] = spec["title_text"] + " Nr."
    spec["name_pct"] = name_pct(spec, meta)
    invoice = dict(invoice)
    invoice["date_text"] = date_text(spec, invoice["date"])
    meta = dict(meta)
    meta["delivery_text"] = date_text(spec, meta["delivery"])
    meta["due_text"] = date_text(spec, meta["due"])
    meta["skonto_text"] = date_text(spec, meta["skonto_date"])
    # Zwischenüberschriften der Belegart (Lohn/Material, Liefertage, Leistungsarten)
    # über die der Warengruppe legen. Reiner Text, `O`, keine Zahl ändert sich.
    families.apply_sections(spec, meta, rng)
    lines = meta["render_lines"]
    pages_html = []
    groups = chunk(lines, per_page or rows_per_page(spec))
    if spec.get("ei_split"):
        # Der Drei-Seiten-Schnitt des KoSIT-Ausdrucks: Uebersicht / Details /
        # Zusaetze. Die erste und die letzte Seite tragen keine Positionen, also
        # bekommen sie eine leere Gruppe. `families.einvoice_page` erkennt sie an
        # `index == 0` bzw. `index + 1 == total`.
        groups = [[]] + groups + [[]]
    carry = 0
    for i, group in enumerate(groups):
        carry += sum(l["lineNet"] for l in group)
        pages_html.append(page_html(spec, invoice, meta, rng, group, i, len(groups), carry))
    return css(spec), pages_html, spec


def page_document(sheet, body):
    return (f'<!doctype html><html lang=de><head><meta charset=utf-8><style>{sheet}</style>'
            f'</head><body>{body}<script>{PROBE}</script></body></html>')


FLAGS = ["--headless", "--disable-gpu", "--no-sandbox", "--hide-scrollbars", "--mute-audio",
         "--no-first-run", "--no-default-browser-check", "--disable-extensions",
         "--disable-background-networking", "--disable-sync", "--disable-default-apps",
         "--disable-dev-shm-usage", "--disable-translate", "--disable-backgrounding-occluded-windows",
         "--disable-client-side-phishing-detection", "--disable-component-update"]

_browser = None
_page = None


def browser_page(count, pw=PAGE_W, ph=PAGE_H):
    global _browser, _page
    if _page is None:
        _browser = cdp.Browser(CHROME, FLAGS)
        _page = _browser.page()
    _page.metrics(pw, ph * count, 1)
    return _page


def reset():
    """Seite *und* Browser schließen.

    Bis v9 schloss `reset()` nur die Seite; der Chrome-Prozess blieb stehen. Und
    weil ein multiprocessing-Worker den Prozess mit `os._exit()` verlässt, läuft
    weder `atexit` noch sonst ein Haken — nach jedem Lauf hingen Hunderte
    verwaiste Chrome-Bäume an init. `generate.one()` ruft das jetzt am Ende jeder
    Rechnung auf: ein Browserstart je Rechnung ist gegen 25 gerenderte Seiten
    nicht messbar.
    """
    global _browser, _page
    if _page is not None:
        try:
            _page.close()
        except OSError:
            pass
    if _browser is not None:
        try:
            _browser.close()
        except OSError:
            pass
    _browser = _page = None


def run(path, count=1, pw=PAGE_W, ph=PAGE_H):
    for attempt in (0, 1):
        try:
            page = browser_page(count, pw, ph)
            page.navigate(f"file://{path}")
            probes = page.evaluate(PROBE)
            if not probes:
                raise RuntimeError("probe returned nothing")
            return page, probes
        except (ConnectionError, TimeoutError, RuntimeError, OSError):
            reset()
            if attempt:
                raise
    raise RuntimeError("unreachable")


def plan(spec, invoice, meta, seed, path):
    """Wie viele Positionen auf eine Seite passen — gemessen, nicht geschätzt.

    Ein Durchgang mit *allen* Positionen auf einer Seite: die Seite läuft dabei
    über, aber `getBoundingClientRect` misst trotzdem, wo der Kopf aufhört, wie
    hoch eine Position wirklich ist und wie viel der Summenblock braucht. Aus
    diesen drei Zahlen steht die Seitenlänge fest.

    Vorher stand hier eine Formel aus Schriftgrad, Durchschuss und Zellenabstand,
    und die Schleife darunter hat ihren Fehler in bis zu acht weiteren
    Durchgängen abgetragen — und blieb oft bei sechs Positionen je Seite stehen,
    wo zwölf hingepasst hätten. Das kostete ein Drittel mehr Seiten, und jede
    Seite kostet später wieder OCR.
    """
    count = len(meta["render_lines"]) or 1
    if spec.get("ei_split"):
        # Gemessen wird das Positionsbudget, nicht der Schnitt: mit Schnitt traegt
        # `bodies[0]` die Uebersichtsseite und gar keine Position, und `plan()`
        # faende keine `line-item`-Region.
        spec = dict(spec)
        spec["ei_split"] = False
    sheet, bodies, _ = document(spec, invoice, meta, seed, (count, count))
    with open(path, "w", encoding="utf-8") as f:
        f.write(page_document(sheet, bodies[0]))
    _, pages = run(path, 1, *page_px(spec))
    probe = pages[0]
    items = [r for r in probe["regions"] if r["role"] == "line-item"]
    if not items:
        return None
    top = min(r["box"][1] for r in items)
    bottom = max(r["box"][1] + r["box"][3] for r in items)
    row = (bottom - top) / len(items)
    if row < 4.0:
        return None
    tail = max((r["box"][3] for r in probe["regions"] if r["role"] == "total"), default=0.0)
    _, h_mm = page_mm(spec)
    limit = (h_mm - floor_mm(spec)) * MM_PX
    # Letzte Seite: Summenblock, Zahlungssatz und der Abstand darüber. Alle
    # anderen: nur die Übertragszeile.
    last = int((limit - top - tail - 12.0 * MM_PX) / row)
    first = int((limit - top - 8.0 * MM_PX) / row)
    return max(2, first), max(2, last)


def build(spec, invoice, meta, seed, workdir):
    path = os.path.join(workdir, "doc.html")
    per_page = plan(spec, invoice, meta, seed, path)
    if per_page is None:
        fallback = rows_per_page(spec)
        per_page = (fallback, fallback)
    previous = None
    stuck = False
    for attempt in range(8):
        sheet, bodies, used = document(spec, invoice, meta, seed, per_page)
        with open(path, "w", encoding="utf-8") as f:
            f.write(page_document(sheet, "".join(bodies)))
        _, pages = run(path, len(bodies), *page_px(spec))
        overflow = max(p["overflow"] for p in pages)
        hoverflow = max(p.get("hoverflow", 0.0) for p in pages)
        if overflow <= 1.0 and hoverflow <= 1.0:
            return path, pages, used
        if hoverflow > 1.0:
            # Ein Wort läuft seitlich aus der Seite — rechts hinaus, oder links, weil
            # ein zu breiter Block mit `justify-content:flex-end` nach links überläuft:
            # die Tabelle ist zu breit für das
            # Blatt. Nur kleinerer Satz und engere Zellen helfen — weniger Zeilen pro
            # Seite ändert an der Breite nichts. Ungefangen verschwindet das Wort beim
            # `overflow:hidden` der Seite und fehlt still in der Wahrheit, was auf A5
            # reihenweise `lineNet` gekostet hat.
            spec = dict(spec)
            spec["size"] = max(5.6, spec["size"] * 0.9)
            spec["cell_pad_x"] = max(0.3, spec["cell_pad_x"] * 0.7)
            fallback = rows_per_page(spec)
            per_page = (fallback, fallback)
            previous = None
            continue
        stalled = previous is not None and overflow > previous * 0.8
        if stalled and stuck:
            # Zwei Runden ohne Fortschritt: der Rest ist kein Zeilenproblem.
            return path, pages, used
        stuck = stalled
        previous = overflow
        first, last = per_page
        if max(first, last) > 3 and not stalled:
            row = max(12.0, (pages[0]["height"] - overflow) / max(last, 1))
            drop = max(1, int(overflow / row))
            per_page = (max(3, first - drop), max(3, last - drop))
        else:
            spec = dict(spec)
            spec["cell_pad_y"] = max(0.3, spec["cell_pad_y"] * 0.6)
            spec["size"] = max(6.2, spec["size"] * 0.88)
            spec["page_margin"] = (spec["page_margin"][0], max(7.0, spec["page_margin"][1] * 0.85))
    return path, pages, used


def shoot(path, probes, workdir, scale):
    pw = round(probes[0]["width"])
    ph = round(probes[0]["height"])
    page = browser_page(len(probes), pw, ph)
    shots = []
    for i, probe in enumerate(probes):
        clip = {"x": 0, "y": i * ph, "width": pw, "height": ph, "scale": scale}
        part = os.path.join(workdir, f"page-{i + 1}.raw.png")
        with open(part, "wb") as f:
            f.write(page.screenshot(clip))
        shots.append((part, probe))
    return shots
