import datetime as dt
import os
import random
import shutil

import blocks
import cdp
import layout
import money

CHROME = os.environ.get("CORPUS_CHROME") or shutil.which("chromium") or shutil.which("chrome") \
    or "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
PAGE_W = 794          # A4 at 96 dpi, the default when a spec carries no format
PAGE_H = 1123
MM_PX = 96.0 / 25.4


def page_px(spec):
    """The spec's page box in CSS pixels. A4 reproduces the old constants exactly."""
    w_mm, h_mm = layout.PAGE_FORMATS[spec.get("page_format", "a4")]
    return round(w_mm * MM_PX), round(h_mm * MM_PX)

MONTHS = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September",
          "Oktober", "November", "Dezember"]

PROBE = """(() => document.querySelectorAll('.page').length && [...document.querySelectorAll('.page')].map(p=>{
 const b=p.getBoundingClientRect();
 const words=[...p.querySelectorAll('span.w')].map(s=>{
  const r=s.getBoundingClientRect();
  return {t:s.textContent,f:s.dataset.f,l:+s.dataset.l,
          box:[r.x-b.x,r.y-b.y,r.width,r.height]};
 }).filter(w=>w.box[2]>0&&w.box[3]>0);
 const regions=[...p.querySelectorAll('[data-role]')].map(e=>{
  const r=e.getBoundingClientRect();
  return {role:e.dataset.role,l:+(e.dataset.l||0),box:[r.x-b.x,r.y-b.y,r.width,r.height]};
 });
 let overflow=0,hoverflow=0;
 for(const w of words){
  overflow=Math.max(overflow,w.box[1]+w.box[3]-b.height);
  hoverflow=Math.max(hoverflow,w.box[0]+w.box[2]-b.width,-w.box[0]);
 }
 return {width:b.width,height:b.height,words,regions,overflow,hoverflow};
}))()"""


def css(spec):
    mx, my = spec["page_margin"]
    pw, ph = page_px(spec)
    rule = f'{spec["rule_weight"]}px solid {spec["rule_ink"]}'
    light = f'{spec["rule_weight"]}px solid {spec["rule_light"]}'
    accent = spec["accent"]
    table_css = {
        "grid": f'.items td,.items th{{border:{light}}}',
        "rules": f'.items tr{{border-bottom:{light}}}.items thead tr{{border-bottom:{rule}}}',
        "zebra": '.items tbody tr.item:nth-child(even){background:#eee}',
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
    }[spec["header_style"]]
    title_css = {
        "plain": '',
        "letter": '.title{letter-spacing:0.28em}',
        "smallcaps": '.title{font-variant:small-caps;letter-spacing:0.06em}',
        "underline": f'.title{{border-bottom:{spec["rule_weight"] * 2}px solid {accent};'
                     'padding-bottom:1.5mm}',
        "boxed": f'.title{{border:{rule};padding:1.8mm 3mm}}',
        "band": f'.title{{background:{accent};color:#fff;padding:1.8mm 3mm}}',
        "none": '',
    }[spec["title_style"]]
    return f"""
@page{{size:{pw}px {ph}px;margin:0}}
*{{box-sizing:border-box}}
body{{margin:0;font-family:{spec["font"]};font-size:{spec["size"]}pt;line-height:{spec["leading"]};
 color:{spec["ink"]}}}
.page{{width:{pw}px;height:{ph}px;position:relative;overflow:hidden;padding:{my}mm {mx}mm;
 page-break-after:always;background:#fff}}
.page:last-child{{page-break-after:auto}}
.head{{display:flex;justify-content:space-between;align-items:flex-start;gap:8mm;
 font-family:{spec["head_font"]}}}
.head.rev{{flex-direction:row-reverse}}
.head.mid{{flex-direction:column;align-items:center;text-align:center}}
.beside{{display:flex;align-items:flex-start;gap:5mm}}
.hmeta{{font-size:0.78em;line-height:1.45;text-align:right;white-space:nowrap}}
.head.rev .hmeta,.head.mid .hmeta{{text-align:left}}
.head.mid .hmeta{{text-align:center;margin-top:2mm}}
.sender{{font-size:0.85em;line-height:1.35}}
.sender.right{{text-align:right}}
.tagline{{font-size:0.62em;letter-spacing:0.16em;color:#444;margin-top:0.6mm}}
.addr{{margin-top:6mm}}
.addrwrap.right{{display:flex;justify-content:flex-end}}
.addrwrap.center{{display:flex;justify-content:center}}
.addrow{{display:flex;justify-content:space-between;align-items:flex-start;gap:6mm;margin-top:6mm}}
.addrow.right{{flex-direction:row-reverse}}
.addrow .addr{{margin-top:0}}
.retline{{font-size:0.62em;border-bottom:0.5px solid #999;padding-bottom:1mm;margin-bottom:2mm;color:#444}}
.aheading{{font-size:0.72em;color:#555;margin-bottom:0.8mm}}
.to{{line-height:1.4}}
.wordmark{{font-size:1.9em;font-weight:700;letter-spacing:0.03em;line-height:1.15}}
.wordmark.caps{{text-transform:uppercase}}
.bar{{display:block;height:3px;margin-top:2px}}
.band{{height:3.5mm;width:34mm;opacity:0.85;margin-bottom:1.5mm}}
.dateline{{text-align:right;margin-top:5mm;font-size:0.95em}}
.title{{font-size:1.65em;font-weight:700;margin:7mm 0 3mm;color:{accent};
 font-family:{spec["head_font"]}}}
table.meta{{border-collapse:collapse;font-size:0.9em}}
table.meta td{{padding:0.3mm 2.5mm 0.3mm 0;vertical-align:top}}
table.meta td.k{{color:#333}}
table.meta.boxed{{border:{light}}}
table.meta.boxed td{{border:{light};padding:0.8mm 2mm}}
table.meta.stack td.k{{font-size:0.82em;color:#555}}
table.meta.row td{{padding-right:4mm;white-space:nowrap}}
table.meta.stacked td.k{{font-size:0.8em;color:#555;padding-top:1.2mm}}
table.meta.grid td{{padding:0.7mm 3mm 0.7mm 0;white-space:nowrap}}
table.meta.grid tr.gk td{{font-size:0.82em;color:#555;font-weight:600}}
table.meta.grid.boxed td{{border:{light};padding:0.9mm 2.5mm}}
.metawrap{{display:flex;justify-content:{'flex-start' if spec["meta_side"] == 'left' else 'flex-end'};margin:2mm 0}}
.panel{{border:{rule};padding:1.5mm 3mm;margin:3mm 0}}
.panel table.meta{{width:100%}}
.items{{width:100%;border-collapse:collapse;margin-top:3mm;table-layout:auto}}
.items td.c-name,.items th.c-name{{width:{spec["name_pct"]}%}}
.items td.nowrap,.items th.nowrap{{white-space:nowrap}}
.items td,.items th{{padding:{spec["cell_pad_y"]}mm {spec["cell_pad_x"]}mm;vertical-align:top;
 overflow-wrap:break-word}}
.items th{{text-align:inherit}}
.a-right{{text-align:right}}
.a-left{{text-align:left}}
.a-center{{text-align:center}}
tr.detail td{{font-size:0.82em;color:#444;padding-top:0;padding-left:{spec["cell_pad_x"] + 3}mm}}
tr.group td{{font-weight:700;background:#f4f4f4;padding-top:1.4mm}}
.carry{{text-align:right;font-weight:600;margin-top:2mm;font-size:0.95em}}
.totalswrap{{display:flex;justify-content:flex-end;margin-top:5mm}}
.totalswrap.full{{display:block}}
.totalswrap.left{{justify-content:flex-start}}
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
table.totals tr.grand td{{font-weight:700;border-top:{rule};font-size:1.08em}}
.pay{{margin-top:5mm;font-size:0.9em}}
.foot{{position:absolute;left:{mx}mm;right:{mx}mm;bottom:{max(5, my - 4)}mm;font-size:0.72em;
 color:#333;border-top:{light};padding-top:1.5mm}}
.foot.cols{{display:flex;gap:6mm;justify-content:space-between}}
.fcol{{flex:1}}
.pageno{{position:absolute;font-size:0.75em;color:#555}}
.pageno.p-tr{{right:{mx}mm;top:{my - 4 if my > 8 else 4}mm}}
.pageno.p-bc{{left:0;right:0;text-align:center;bottom:{max(2, my - 9)}mm}}
.pageno.p-bl{{left:{mx}mm;bottom:{max(2, my - 9)}mm}}
.decor{{position:absolute;opacity:0.62}}
.qr{{width:20mm;height:20mm}}
.qr.q-bl{{left:{mx}mm;bottom:{my + 14}mm}}
.qr.q-tr{{right:{mx}mm;top:{my + 2}mm}}
.stampbox{{left:{mx + 18}mm;bottom:{my + 26}mm;width:34mm;height:15mm;border:2px solid;
 transform:rotate(-11deg);opacity:0.45;border-radius:2mm}}
{table_css}
{header_css}
{title_css}
"""


def date_text(spec, iso):
    d = dt.date.fromisoformat(iso)
    if spec["date_format"] == "%d. %B %Y":
        return f"{d.day}. {MONTHS[d.month - 1]} {d.year}"
    return d.strftime(spec["date_format"])


def chunk(lines, per_page):
    return [lines[i:i + per_page] for i in range(0, len(lines), per_page)] or [[]]


def rows_per_page(spec):
    row_mm = spec["size"] * 0.3528 * spec["leading"] + 2 * spec["cell_pad_y"]
    if spec["narrow_name"]:
        row_mm *= 1.55
    if spec["second_row_details"]:
        row_mm *= 1.25
    if spec["group_headings"]:
        row_mm *= 1.12
    h_mm = layout.PAGE_FORMATS[spec.get("page_format", "a4")][1]
    usable = h_mm - 2 * spec["page_margin"][1] - 118 * min(1.0, h_mm / 297.0)
    return max(2, int(usable / max(row_mm, 2.0)))


def page_html(spec, invoice, meta, rng, page_lines, index, total, carry):
    place = spec["meta_place"]
    parts = [blocks.letterhead(spec, rng, meta)]
    addr = blocks.address(spec, meta)
    if place in ("top_right", "split"):
        # Kopfdaten neben der Anschrift, nicht unter der Überschrift — auf echten
        # Rechnungen die Normalform. "split" zieht Nummer und Datum hierher und
        # lässt den Rest unten stehen.
        side = blocks.meta_block(spec, invoice, meta, "ours" if place == "split" else "all")
        parts.append(f'<div class="addrow {spec["address_corner"]}">'
                     f'<div class=acol>{addr}</div><div class=mcol>{side}</div></div>')
    else:
        parts.append(f'<div class="addrwrap {spec["address_corner"]}">{addr}</div>')
    parts.append(blocks.dateline(spec, invoice, meta))
    parts.append(blocks.title_line(spec, invoice, meta, index))
    if place in ("under_title", "split"):
        table = blocks.meta_block(spec, invoice, meta, "rest" if place == "split" else "all")
        if table:
            parts.append(f'<div class=metawrap>{table}</div>')
    elif place == "panel":
        table = blocks.meta_block(spec, invoice, meta, "all")
        if table:
            parts.append(f'<div class=panel>{table}</div>')
    elif place == "bottom":
        table = blocks.meta_block(spec, invoice, meta, "all")
        if table:
            parts.append(f'<div class=metawrap>{table}</div>')
    header = blocks.item_header(spec)
    body = blocks.item_rows(spec, page_lines)
    parts.append(f'<table class=items>{header}<tbody>{body}</tbody></table>')
    if index + 1 < total:
        parts.append(f'<div class=carry data-role="carry">{blocks.words(spec["carry_label"])} '
                     f'{blocks.words(money.cents(carry) + " €")}</div>')
    else:
        totals = blocks.totals_block(spec, invoice, meta)
        if totals:
            parts.append(f'<div class="totalswrap {spec["totals_side"]}">{totals}</div>')
        if meta["kind"] == "invoice":
            parts.append(f'<div class=pay>{blocks.words(meta["payment"])}</div>')
    parts.append(blocks.pageno(spec, index, total))
    parts.append(blocks.footer(spec, meta))
    parts.append(blocks.decor(spec, rng))
    return f'<div class=page data-role="page">{"".join(parts)}</div>'


def name_pct(spec, meta):
    longest = max((len(l["name"]) for l in meta["render_lines"]), default=20)
    span = min(52.0, 2.2 + longest * 1.05)
    return round(span * (0.45 if spec["narrow_name"] else 1.0), 1)


def document(spec, invoice, meta, seed, per_page=None):
    rng = random.Random(seed)
    spec = dict(spec)
    spec["title"] = rng.choice(layout.TITLES[meta["kind"]])
    spec["title_key"] = rng.choice(layout.TITLE_KEYS[meta["kind"]])
    spec["name_pct"] = name_pct(spec, meta)
    invoice = dict(invoice)
    invoice["date_text"] = date_text(spec, invoice["date"])
    meta = dict(meta)
    meta["delivery_text"] = date_text(spec, meta["delivery"])
    meta["due_text"] = date_text(spec, meta["due"])
    meta["skonto_text"] = date_text(spec, meta["skonto_date"])
    lines = meta["render_lines"]
    pages_html = []
    groups = chunk(lines, per_page or rows_per_page(spec))
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

_page = None


def browser_page(count, pw=PAGE_W, ph=PAGE_H):
    global _page
    if _page is None:
        _page = cdp.Browser(CHROME, FLAGS).page()
    _page.metrics(pw, ph * count, 1)
    return _page


def reset():
    global _page
    if _page is not None:
        try:
            _page.close()
        except OSError:
            pass
        _page = None


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


def build(spec, invoice, meta, seed, workdir):
    path = os.path.join(workdir, "doc.html")
    per_page = rows_per_page(spec)
    previous = None
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
            per_page = rows_per_page(spec)
            previous = None
            continue
        stalled = previous is not None and overflow > previous * 0.8
        previous = overflow
        if per_page > 3 and not stalled:
            row = max(12.0, (pages[0]["height"] - overflow) / max(per_page, 1))
            per_page = max(3, min(per_page - 1, int(per_page - overflow / row)))
        else:
            spec = dict(spec)
            spec["cell_pad_y"] = max(0.3, spec["cell_pad_y"] * 0.6)
            spec["size"] = max(6.2, spec["size"] * 0.88)
            spec["page_margin"] = (spec["page_margin"][0], max(7.0, spec["page_margin"][1] * 0.85))
            per_page = max(3, per_page)
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
