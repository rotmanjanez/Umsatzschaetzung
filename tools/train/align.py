"""truth.json + page-N.ocr.json -> page.jsonl.

Labels are carried from the render-time boxes onto the OCR words by IoU, with
text similarity breaking the doubtful cases. The words the OCR never saw are the
ceiling on every model trained from this.
"""
import argparse
import json
import re
import unicodedata
from collections import Counter
from difflib import SequenceMatcher
from multiprocessing import Pool
from pathlib import Path

import schema

ACCEPT_IOU = 0.3
DOUBT_IOU = 0.1
DOUBT_SIM = 0.5

_strip = re.compile(r"[^0-9a-zäöüß]+")


def _sym(t):
    return re.sub(r"\s+", "", unicodedata.normalize("NFC", t).casefold())


def norm(t):
    return _strip.sub("", unicodedata.normalize("NFC", t).casefold())


def inter(a, b):
    x = max(a[0], b[0])
    y = max(a[1], b[1])
    x2 = min(a[0] + a[2], b[0] + b[2])
    y2 = min(a[1] + a[3], b[1] + b[3])
    return max(0.0, x2 - x) * max(0.0, y2 - y)


def iou(a, b):
    i = inter(a, b)
    if i <= 0:
        return 0.0
    return i / (a[2] * a[3] + b[2] * b[3] - i)


def _bucket(box, size):
    return int(box[1] // size)


def _index(words, size):
    idx = {}
    for w in words:
        lo = _bucket(w["box"], size)
        hi = _bucket([w["box"][0], w["box"][1] + w["box"][3], 0, 0], size)
        for b in range(lo, hi + 1):
            idx.setdefault(b, []).append(w)
    return idx


def _candidates(idx, box, size):
    lo = _bucket(box, size)
    hi = _bucket([box[0], box[1] + box[3], 0, 0], size)
    out = []
    for b in range(lo - 1, hi + 2):
        out.extend(idx.get(b, ()))
    return out


def label_words(ocr_words, truth_words, band):
    """Best truth word per OCR word; ties on overlap area, doubt on text."""
    idx = _index(truth_words, band)
    out = []
    for w in ocr_words:
        best = None
        for t in _candidates(idx, w["box"], band):
            area = inter(w["box"], t["box"])
            if area <= 0:
                continue
            u = iou(w["box"], t["box"])
            if u < DOUBT_IOU:
                continue
            if best is None or area > best[0]:
                best = (area, u, t)
        if best is None:
            out.append(("O", None))
            continue
        _, u, t = best
        if u >= ACCEPT_IOU:
            out.append((t["f"], t))
        elif SequenceMatcher(None, norm(w["t"]), norm(t["t"])).ratio() >= DOUBT_SIM:
            out.append((t["f"], t))
        else:
            out.append(("O", None))
    return out


def structure(w, t):
    """v13: cell/column structure of the item table, carried from the truth word."""
    if t is None or t.get("tbl", -1) < 0:
        w["col"], w["tbl"], w["cell"] = -1, -1, -1
    else:
        w["col"], w["tbl"], w["cell"] = int(t.get("ci", -1)), int(t["tbl"]), int(t.get("cid", -1))


def mark_cell_starts(page_words):
    """cell_start = 1 on the first word of each table cell in reading order of its row."""
    by_row = {}
    for w in page_words:
        by_row.setdefault(w["row"], []).append(w)
    for ws in by_row.values():
        seen = set()
        for w in sorted(ws, key=lambda q: q["box"][0]):
            if w["cell"] < 0:
                w["cell_start"] = 0
            else:
                w["cell_start"] = 0 if w["cell"] in seen else 1
                seen.add(w["cell"])


def covered(truth_words, ocr_words, band):
    """Per truth word: (seen by some OCR word, read verbatim)."""
    idx = _index(ocr_words, band)
    out = []
    for t in truth_words:
        hits = [w for w in _candidates(idx, t["box"], band)
                if iou(t["box"], w["box"]) >= DOUBT_IOU]
        if not hits:
            out.append((False, False))
            continue
        n = norm(t["t"])
        if n:
            out.append((True, any(n in norm(w["t"]) for w in hits)))
        else:
            # A token that is all punctuation ("%", "€", "&") normalises to
            # nothing, and an empty needle would score every one of them misread.
            n = _sym(t["t"])
            out.append((True, any(n and n in _sym(w["t"]) for w in hits)))
    return out


# A line item is a region, not a row: a wrapped name, a detail row and an amount
# that lands on its own all belong to one item. Regions match expected.json lines
# exactly, so carrying the index is what lets assembly rebuild the invoice. -1 is
# a word outside any line item.
def in_quad(x, y, quad):
    """Point in a convex quadrilateral (same-sign cross products around the edges)."""
    sign = 0
    for i in range(4):
        ax, ay = quad[i]
        bx, by = quad[(i + 1) % 4]
        cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax)
        if cross == 0:
            continue
        s = 1 if cross > 0 else -1
        if sign and s != sign:
            return False
        sign = s
    return True


# v11: a skewed region's bounding box is up to three times taller than the line
# and overlaps its neighbours, which put 19.5 % of the labelled words on a 2° page
# into the wrong item. degrade.warp_quads writes the rotated quad next to the box;
# where it exists the test is point-in-quad, and only pre-v11 corpora fall back to
# the box.
def item_of(box, items):
    cx = box[0] + box[2] / 2
    cy = box[1] + box[3] / 2
    for i, r in enumerate(items):
        if r["quad"] is not None:
            if in_quad(cx, cy, r["quad"]):
                return i
        elif r["box"][0] <= cx <= r["box"][0] + r["box"][2] and r["box"][1] <= cy <= r["box"][1] + r["box"][3]:
            return i
    return -1


def _row_item(row, items):
    hits = [i for i in (item_of(w["box"], items) for w in row["words"]) if i >= 0]
    return Counter(hits).most_common(1)[0][0] if hits else -1


# Only the first row of a line-item region starts an item; the rest are a wrapped
# name or an amount pushed onto its own row. That distinction is invisible to a
# model reading one row at a time, so it is carried in the role rather than in a
# second head: assembly needs nothing else to rebuild the item.
def wrap_rows(page_rows, roles, items):
    seen = set()
    out = []
    for row, role in zip(page_rows, roles):
        if role != "line-item":
            out.append(role)
            continue
        i = _row_item(row, items)
        if i < 0:
            out.append(role)
            continue
        out.append("line-wrap" if i in seen else role)
        seen.add(i)
    return out


def row_roles(page_rows, regions):
    roles = []
    for r in page_rows:
        box = r["box"]
        best, area = "header", 0.0
        for reg in regions:
            if reg["role"] == "page":
                continue
            a = inter(box, reg["box"])
            if a > area:
                best, area = reg["role"], a
        roles.append(best if area >= 0.3 * box[2] * box[3] else "header")
    return roles


# Port of Rows.GroupRows from Umsatzschätzung.Core/Extract/Rows.cs. Training and
# inference must group identically; a divergence here is invisible and poisons
# every geometric feature. Keep this in step with the C#.
def _overlaps(anchor, box):
    ca = anchor[1] + anchor[3] / 2
    cb = box[1] + box[3] / 2
    return (anchor[1] <= cb < anchor[1] + anchor[3]) or (box[1] <= ca < box[1] + box[3])


def group_rows(words):
    """words: [{'t':..., 'box':[x,y,w,h], ...}] -> list of rows, each a word list.

    Rows come back ordered by y, words within a row ordered by x, matching the
    C# exactly: the anchor is the first word assigned to the row and never
    widens, so the first vertically-overlapping row wins.
    """
    rows = []
    for w in sorted((w for w in words if w["t"].strip()), key=lambda w: w["box"][1]):
        hit = next((r for r in rows if _overlaps(r["anchor"], w["box"])), None)
        if hit is None:
            rows.append({"anchor": w["box"], "box": list(w["box"]), "words": [w]})
            continue
        hit["words"].append(w)
        hit["box"] = union(hit["box"], w["box"])
    for r in rows:
        r["words"].sort(key=lambda w: w["box"][0])
    rows.sort(key=lambda r: r["box"][1])
    return rows


def union(a, b):
    if a[2] == 0 and a[3] == 0:
        return list(b)
    x = min(a[0], b[0])
    y = min(a[1], b[1])
    return [x, y, max(a[0] + a[2], b[0] + b[2]) - x, max(a[1] + a[3], b[1] + b[3]) - y]


def one_variation(args):
    path, perfect = args
    vdir = path.parent
    truth = json.loads(path.read_text())
    pages, stats = [], Counter()
    for i, tp in enumerate(truth["pages"]):
        if perfect:
            ocr = {"width": tp["width"], "height": tp["height"],
                   "words": [{"t": w["t"], "box": w["box"], "field": w["f"],
                              "col": int(w.get("ci", -1)) if w.get("tbl", -1) >= 0 else -1,
                              "tbl": int(w.get("tbl", -1)), "cell": int(w.get("cid", -1)) if w.get("tbl", -1) >= 0 else -1}
                             for w in tp["words"] if w["t"].strip()]}
        else:
            ocr = schema.ocr_pages(vdir, i)
        if ocr is None:
            stats["pages_without_ocr"] += 1
            continue
        band = max(20.0, tp["height"] / 100.0)
        if not perfect:
            for w, (f, t) in zip(ocr["words"], label_words(ocr["words"], tp["words"], band)):
                w["field"] = f
                structure(w, t)
        grouped = group_rows(ocr["words"])
        items = [{"box": r["box"], "quad": r.get("quad")}
                 for r in tp.get("regions", []) if r["role"] == "line-item"]
        roles = wrap_rows(grouped, row_roles(grouped, tp.get("regions", [])), items)
        words = []
        for ri, (r, role) in enumerate(zip(grouped, roles)):
            for w in r["words"]:
                words.append({"t": w["t"], "box": [round(float(v), 1) for v in w["box"]],
                              "field": w["field"], "row": ri, "role": role,
                              "item": item_of(w["box"], items),
                              "col": w.get("col", -1), "tbl": w.get("tbl", -1), "cell": w.get("cell", -1)})
        mark_cell_starts(words)
        pages.append({"template": truth["template"], "split": truth["split"],
                      "profile": truth["profile"], "invoice": vdir.parent.name,
                      "page": i + 1, "w": ocr["width"], "h": ocr["height"],
                      "words": words})

        stats["ocr_words"] += len(ocr["words"])
        stats["ocr_field"] += sum(1 for w in ocr["words"] if w["field"] != "O")
        for t, (seen, verbatim) in zip(tp["words"], covered(tp["words"], ocr["words"], band)):
            if t["f"] == "O":
                continue
            stats["truth_field"] += 1
            stats["truth_unseen"] += not seen
            stats["truth_misread"] += seen and not verbatim
            stats["f:%s:n" % t["f"]] += 1
            stats["f:%s:unseen" % t["f"]] += not seen
            stats["p:%s:n" % truth["profile"]] += 1
            stats["p:%s:unseen" % truth["profile"]] += not seen
        stats["rows"] += len(grouped)
        for role in roles:
            stats["role:" + role] += 1
        stats["pages"] += 1
    return pages, stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", required=True)
    ap.add_argument("--out", default="page.jsonl")
    ap.add_argument("--workers", type=int, default=0)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--perfect", action="store_true",
                    help="label the render-time words themselves: no OCR, no ceiling")
    a = ap.parse_args()

    jobs = [(p, a.perfect) for p in sorted(Path(a.corpus).glob("*/*/truth.json"))]
    if a.limit:
        jobs = jobs[:a.limit]

    stats = Counter()
    fields = Counter()
    with open(a.out, "w") as out:
        with Pool(a.workers or None) as pool:
            for pages, st in pool.imap_unordered(one_variation, jobs, chunksize=8):
                stats.update(st)
                for p in pages:
                    fields.update(w["field"] for w in p["words"])
                    out.write(json.dumps(p, ensure_ascii=False, separators=(",", ":")) + "\n")

    tf = max(1, stats["truth_field"])
    print(f"variations {len(jobs)}  pages {stats['pages']}  rows {stats['rows']}")
    print(f"ocr words {stats['ocr_words']}  labelled "
          f"{stats['ocr_field'] / max(1, stats['ocr_words']):.3%}")
    print(f"truth field words {stats['truth_field']}")
    print(f"OCR ceiling: unseen {stats['truth_unseen'] / tf:.3%}  "
          f"misread {stats['truth_misread'] / tf:.3%}  "
          f"recoverable {1 - (stats['truth_unseen'] + stats['truth_misread']) / tf:.3%}")
    if stats["pages_without_ocr"]:
        print(f"pages without an ocr dump: {stats['pages_without_ocr']}")
    for kind, title in (("f", "per field"), ("p", "per profile")):
        print(title + " unseen:", {k.split(":")[1]:
              f"{stats[k[:-1] + 'unseen'] / max(1, stats[k]):.1%}"
              for k in sorted(stats) if k.startswith(kind + ":") and k.endswith(":n")})
    print("labels:", dict(fields.most_common()))
    print("roles:", {k[5:]: v for k, v in sorted(stats.items()) if k.startswith("role:")})


if __name__ == "__main__":
    main()
