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
PAGE_W = 794
PAGE_H = 1123

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
 let overflow=0;
 for(const w of words) overflow=Math.max(overflow,w.box[1]+w.box[3]-b.height);
 return {width:b.width,height:b.height,words,regions,overflow};
}))()"""


def css(spec):
    mx, my = spec["page_margin"]
    style = spec["table_style"]
    rule = f'{spec["rule_weight"]}px solid #333'
    light = f'{spec["rule_weight"]}px solid #999'
    table_css = {
        "grid": f'.items td,.items th{{border:{light}}}',
        "rules": f'.items tr{{border-bottom:{light}}}.items thead tr{{border-bottom:{rule}}}',
        "zebra": f'.items tbody tr.item:nth-child(even){{background:#eee}}',
        "borderless": '',
    }[style]
    header_css = {
        "bold": '.items th{font-weight:700}',
        "inverted": f'.items th{{background:{spec["accent"]};color:#fff;font-weight:700}}',
        "underline": f'.items th{{border-bottom:{rule};font-weight:700}}',
        "plain": '.items th{font-weight:400}',
        "boxed": f'.items th{{border:{rule};font-weight:700;background:#f0f0f0}}',
    }[spec["header_style"]]
    return f"""
@page{{size:A4;margin:0}}
*{{box-sizing:border-box}}
body{{margin:0;font-family:{spec["font"]};font-size:{spec["size"]}pt;line-height:{spec["leading"]};color:#111}}
.page{{width:{PAGE_W}px;height:{PAGE_H}px;position:relative;overflow:hidden;padding:{my}mm {mx}mm;
 page-break-after:always;background:#fff}}
.page:last-child{{page-break-after:auto}}
.head{{display:flex;justify-content:space-between;align-items:flex-start;gap:8mm}}
.head.rev{{flex-direction:row-reverse}}
.sender{{font-size:0.85em;line-height:1.35}}
.addr{{margin-top:6mm}}
.retline{{font-size:0.62em;border-bottom:0.5px solid #999;padding-bottom:1mm;margin-bottom:2mm;color:#444}}
.to{{line-height:1.4}}
.wordmark{{font-size:1.9em;font-weight:700;letter-spacing:0.03em}}
.bar{{display:block;height:3px;margin-top:2px}}
.band{{height:3.5mm;width:34mm;opacity:0.85;margin-bottom:1.5mm}}
.title{{font-size:1.65em;font-weight:700;margin:7mm 0 3mm;color:{spec["accent"]}}}
table.meta{{border-collapse:collapse;font-size:0.9em}}
table.meta td{{padding:0.3mm 2.5mm 0.3mm 0;vertical-align:top}}
table.meta td.k{{color:#333}}
table.meta.boxed{{border:{light}}}
table.meta.boxed td{{border:{light};padding:0.8mm 2mm}}
table.meta.stack td.k{{font-size:0.82em;color:#555}}
table.meta.row td{{padding-right:4mm;white-space:nowrap}}
.metawrap{{display:flex;justify-content:{'flex-start' if spec["meta_side"] == 'left' else 'flex-end'};margin:2mm 0}}
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
table.totals{{border-collapse:collapse;min-width:70mm}}
table.totals.full{{width:100%}}
table.totals td{{padding:0.7mm 3mm}}
table.totals td.v{{text-align:right;white-space:nowrap}}
table.totals.boxed{{border:{rule}}}
table.totals.boxed td{{border-bottom:{light}}}
table.totals.table td{{border-bottom:{light}}}
table.totals tr.grand td{{font-weight:700;border-top:{rule};font-size:1.08em}}
.pay{{margin-top:5mm;font-size:0.9em}}
.foot{{position:absolute;left:{mx}mm;right:{mx}mm;bottom:{max(5, my - 4)}mm;font-size:0.72em;
 color:#333;border-top:{light};padding-top:1.5mm}}
.foot.cols{{display:flex;gap:6mm;justify-content:space-between}}
.fcol{{flex:1}}
.pageno{{position:absolute;right:{mx}mm;top:{my - 4 if my > 8 else 4}mm;font-size:0.75em;color:#555}}
{table_css}
{header_css}
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
    usable = 297 - 2 * spec["page_margin"][1] - 118
    return max(2, int(usable / max(row_mm, 2.0)))


def page_html(spec, invoice, meta, rng, page_lines, index, total, carry):
    sup, cus = meta["supplier"], meta["customer"]
    head_class = "head rev" if spec["logo_side"] == "right" else "head"
    left = blocks.logo(spec, rng, sup["name"]) + blocks.sender(spec, sup)
    parts = [f'<div class="{head_class}"><div>{left}</div><div class=hmeta></div></div>']
    if spec["address_corner"] == "right":
        parts.append(f'<div style="display:flex;justify-content:flex-end">{blocks.address(sup, cus)}</div>')
    else:
        parts.append(blocks.address(sup, cus))
    parts.append(f'<div class=title>{blocks.words(spec["title"])}'
                 f'{"" if not index else " " + blocks.words(f"Seite {index + 1}")}</div>')
    parts.append(f'<div class=metawrap>{blocks.meta_block(spec, invoice, meta)}</div>')
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
    if total > 1:
        parts.append(f'<div class=pageno>{blocks.words(f"Seite {index + 1} von {total}")}</div>')
    parts.append(blocks.footer(spec, meta))
    return f'<div class=page data-role="page">{"".join(parts)}</div>'


def name_pct(spec, meta):
    longest = max((len(l["name"]) for l in meta["render_lines"]), default=20)
    span = min(52.0, 2.2 + longest * 1.05)
    return round(span * (0.45 if spec["narrow_name"] else 1.0), 1)


def document(spec, invoice, meta, seed, per_page=None):
    rng = random.Random(seed)
    spec = dict(spec)
    spec["title"] = rng.choice(layout.TITLES[meta["kind"]])
    spec["name_pct"] = name_pct(spec, meta)
    invoice = dict(invoice)
    invoice["date_text"] = date_text(spec, invoice["date"])
    meta = dict(meta)
    meta["delivery_text"] = date_text(spec, meta["delivery"])
    meta["due_text"] = date_text(spec, meta["due"])
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


def browser_page(count):
    global _page
    if _page is None:
        _page = cdp.Browser(CHROME, FLAGS).page()
    _page.metrics(PAGE_W, PAGE_H * count, 1)
    return _page


def reset():
    global _page
    if _page is not None:
        try:
            _page.close()
        except OSError:
            pass
        _page = None


def run(path, count=1):
    for attempt in (0, 1):
        try:
            page = browser_page(count)
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
        _, pages = run(path, len(bodies))
        overflow = max(p["overflow"] for p in pages)
        if overflow <= 1.0:
            return path, pages, used
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
    page = browser_page(len(probes))
    shots = []
    for i, probe in enumerate(probes):
        clip = {"x": 0, "y": i * PAGE_H, "width": PAGE_W, "height": PAGE_H, "scale": scale}
        part = os.path.join(workdir, f"page-{i + 1}.raw.png")
        with open(part, "wb") as f:
            f.write(page.screenshot(clip))
        shots.append((part, probe))
    return shots
